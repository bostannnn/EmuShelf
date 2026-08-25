using System;
using Android.App;
using Android.Content;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching.Android;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Translates a pure <see cref="AndroidIntentRequest"/> (built and tested in the shared assemblies from
/// the intents measured on the Thor) into a framework <c>Intent</c> and starts the emulator activity.
/// This is the one piece of the launch path that must touch the Android SDK, so it is deliberately thin:
/// all of the "which emulator, which URI, which extra" decisions live in
/// <c>AndroidLaunchResolver</c>/<c>AndroidIntentFactory</c> where the desktop suite can assert them.
///
/// Not yet wired into <c>IEmulatorLaunchService</c>: the shared launch pipeline is built around a
/// <c>ProcessStartSpec</c> (executable + args + exit code), which an intent does not fit, and the
/// "returned from the game" signal is an Activity-lifecycle callback rather than a process exit. Choosing
/// how the Android launch path plugs in (a dedicated <c>IEmulatorLaunchService</c> vs. an
/// <c>ITrackedProcessRunner</c> that speaks intents) and making it survive process death is the remaining
/// Milestone B integration — see <c>docs/android-port-plan.md</c>.
/// </summary>
public sealed class AndroidGameLauncher(Func<Context?> context, IAppLogger logger)
{
    /// <summary>
    /// True when <paramref name="packageName"/> is installed and visible to EmuShelf. Lets the caller
    /// fail loudly with "X is not installed" before attempting a launch, instead of firing an intent
    /// and interpreting a generic failure. Every emulator package is declared in the Android head's
    /// <c>&lt;queries&gt;</c> block, so visibility resolves on API 30+; without that declaration this
    /// would report a false negative. Returns null-safe false when there is no context yet.
    /// </summary>
    public bool IsInstalled(string packageName)
    {
        if (string.IsNullOrEmpty(packageName))
            return false;

        var manager = context()?.PackageManager;
        // GetLaunchIntentForPackage returns null when the package is absent — the emulators here all
        // have a launcher activity, so a present package always yields a non-null intent. Preferred
        // over GetPackageInfo because it needs no exception path for the not-installed case.
        return manager?.GetLaunchIntentForPackage(packageName) is not null;
    }

    /// <summary>
    /// Fires <paramref name="request"/> at its emulator. Returns false (without throwing) when there is no
    /// context to start from or the target activity cannot be resolved — e.g. the emulator is not
    /// installed, which the caller should have caught with a package-visibility check first.
    /// <paramref name="launchDisplayId"/>, when set, targets a specific physical display (the Thor's
    /// second screen) via <c>ActivityOptions.setLaunchDisplayId</c>; null launches on the default display.
    /// The target is a request Android forwards to the emulator — an app that forces its own display or
    /// ignores the option still lands where it insists, which is why the caller verifies on-device.
    /// </summary>
    public bool Launch(AndroidIntentRequest request, int? launchDisplayId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ctx = context();
        if (ctx is null)
        {
            logger.Warning($"Cannot launch {request.Component}: no Android context is available.");
            return false;
        }

        // FLAG_GRANT_READ_URI_PERMISSION grants the intent's data URI (and its ClipData), and Android rejects
        // the whole startActivity with a SecurityException if we ask to pass a grant for a URI we do not
        // ourselves hold. Historically the ROM URI was synthesized from a MANAGE_EXTERNAL_STORAGE path (never
        // obtained via SAF), so we held no grantable permission and the flag was always dropped — which made
        // emulators that do not read through their own persisted roms/<system> tree grant (Azahar) fall back
        // to prompting the user for media/storage access. Now the launch service acquires EmuShelf's own
        // persisted SAF grant to the library folder first (IAndroidReadGrantBroker), so this CheckUriPermission
        // passes and we delegate the read — removing the dependency on each emulator's own grant.
        var withGrant = request.GrantReadUriPermission && CanGrantUri(ctx, request.RomContentUri);

        if (TryStart(ctx, request, withGrant, launchDisplayId))
            return true;

        // Safety net: if the grant slipped through the CheckUriPermission gate and startActivity still
        // rejected it, retry once without the flag rather than reporting the game as unlaunchable.
        if (withGrant && TryStart(ctx, request, withGrant: false, launchDisplayId))
            return true;

        return false;
    }

    // True only when EmuShelf itself holds a read grant for the ROM URI, so passing it on will not be
    // rejected. False when EmuShelf holds no SAF grant covering it, or when there is no content URI to grant
    // (RetroArch's plain path).
    private static bool CanGrantUri(Context ctx, string? romContentUri)
    {
        if (string.IsNullOrEmpty(romContentUri))
            return false;

        return ctx.CheckUriPermission(
            global::Android.Net.Uri.Parse(romContentUri),
            global::Android.OS.Process.MyPid(),
            global::Android.OS.Process.MyUid(),
            ActivityFlags.GrantReadUriPermission) == global::Android.Content.PM.Permission.Granted;
    }

    private bool TryStart(Context ctx, AndroidIntentRequest request, bool withGrant, int? launchDisplayId = null)
    {
        using var intent = new Intent();
        intent.SetComponent(new ComponentName(request.PackageName, request.ActivityName));

        if (!string.IsNullOrEmpty(request.Action))
            intent.SetAction(request.Action);

        if (!string.IsNullOrEmpty(request.DataUri))
            intent.SetData(global::Android.Net.Uri.Parse(request.DataUri));

        foreach (var (key, value) in request.StringExtras)
            intent.PutExtra(key, value);

        foreach (var (key, value) in request.BoolExtras)
            intent.PutExtra(key, value);

        if (withGrant)
        {
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

            // A read grant follows the intent's data URI and its ClipData, never an arbitrary string extra.
            // For the emulators that take the ROM as an extra (Dolphin's AutoStartFile, DuckStation's
            // bootPath, WatermelonDS's uri) the URI is not in the data slot, so attach it as ClipData too;
            // otherwise the flag would grant nothing and the emulator would be back to needing its own grant.
            if (request.RomUriRidesInExtra)
            {
                intent.ClipData = ClipData.NewRawUri(
                    "rom", global::Android.Net.Uri.Parse(request.RomContentUri));
            }
        }

        // The emulator runs as its own task and becomes the top-resumed activity; NEW_TASK is required
        // because we may be starting it from a non-Activity context, and it is what makes the eventual
        // onTopResumedActivityChanged exit signal (Milestone B) fire when the user returns to EmuShelf.
        intent.AddFlags(ActivityFlags.NewTask);

        // A single-activity emulator (Citra/Azahar) that is already in recents would otherwise be merely
        // re-foregrounded by NEW_TASK, ignoring the new ROM — the "3DS game does nothing" symptom. CLEAR_TASK
        // + CLEAR_TOP force a fresh start so onCreate reads the new ROM. Set only for profiles that need it.
        if (request.ClearTask)
        {
            intent.AddFlags(ActivityFlags.ClearTask);
            intent.AddFlags(ActivityFlags.ClearTop);
        }

        try
        {
            // Target a specific display when asked and the platform supports it (setLaunchDisplayId is
            // API 26+). MakeBasic can return null on some OEM builds; fall back to a plain start there so
            // the launch still happens (on the default display) rather than failing.
            if (launchDisplayId is { } displayId && OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                using var options = ActivityOptions.MakeBasic();
                if (options is not null)
                {
                    options.SetLaunchDisplayId(displayId);
                    ctx.StartActivity(intent, options.ToBundle());
                    logger.Information($"Launched {request.Component} on display {displayId}.");
                    return true;
                }
            }

            ctx.StartActivity(intent);
            logger.Information($"Launched {request.Component}.");
            return true;
        }
        catch (ActivityNotFoundException ex)
        {
            logger.Error($"Could not launch {request.Component}: activity not found.", ex);
            return false;
        }
        catch (Java.Lang.SecurityException ex) when (withGrant)
        {
            // We asked to pass a read grant for a URI we cannot grant. This is expected under the all-files
            // model; the caller retries without the flag, so log at a lower level and let it fall through.
            logger.Warning(
                $"Cannot pass a read grant to {request.Component} (EmuShelf does not hold the URI permission); " +
                $"retrying without it: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            logger.Error($"Could not launch {request.Component}.", ex);
            return false;
        }
    }
}
