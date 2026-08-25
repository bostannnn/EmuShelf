using System;
using System.Collections.Generic;
using System.IO;
using Android.App;
using Android.Content;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching.Android;
using AndroidUri = Android.Net.Uri;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// How a game's ROM is handed to the target emulator (DECISIONS 2026-08-25). EmuShelf reads ROMs by real
/// path under all-files access, then re-exposes each one through its <em>own</em> FileProvider so it can
/// delegate a read grant to any emulator — no per-launch SAF folder pick, and no dependence on the emulator
/// holding its own <c>roms/&lt;system&gt;</c> grant. Built in <see cref="AndroidEmulatorLaunchService"/> from
/// the resolved profile; the pure <see cref="AndroidRomHandoffRules"/> decides the FileProvider-vs-real-path
/// split so it is asserted off-device.
/// </summary>
/// <param name="DelegateViaFileProvider">
/// True for a scoped-storage emulator (the ROM travels as a <c>content://</c> URI): mint a FileProvider URI
/// for <paramref name="RealPath"/> and grant it. False for RetroArch, which holds all-files and takes the
/// plain path already baked into the request — nothing to mint or grant.
/// </param>
/// <param name="RealPath">The ROM's real filesystem path (EmuShelf reads it under all-files access).</param>
/// <param name="PayloadExtraName">
/// The string-extra key the ROM rides in (DuckStation <c>bootPath</c>, Dolphin <c>AutoStartFile</c>,
/// WatermelonDS <c>uri</c>), or null when it rides in the intent's data slot (Azahar/PPSSPP/ARMSX2).
/// </param>
/// <param name="PreferRealPath">
/// True when the emulator must receive a real <c>file://</c> path instead of a FileProvider URI — a
/// multi-file descriptor (.cue/.gdi/.m3u) handed to DuckStation, which resolves its sibling tracks by
/// relative path (<see cref="AndroidRomHandoffRules.PrefersRealPath"/>).
/// </param>
public sealed record AndroidRomHandoff(
    bool DelegateViaFileProvider,
    string RealPath,
    string? PayloadExtraName,
    bool PreferRealPath);

/// <summary>
/// Translates a pure <see cref="AndroidIntentRequest"/> (built and tested in the shared assemblies from
/// the intents measured on the Thor) into a framework <c>Intent</c> and starts the emulator activity.
/// This is the one piece of the launch path that must touch the Android SDK, so it is deliberately thin:
/// all of the "which emulator, which URI, which extra" decisions live in
/// <c>AndroidLaunchResolver</c>/<c>AndroidIntentFactory</c> where the desktop suite can assert them.
///
/// The ROM handoff is the exception that unavoidably touches the SDK: minting a FileProvider URI and
/// granting it to the emulator's package can only happen here, with a live <c>Context</c> — see
/// <see cref="AndroidRomHandoff"/>.
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
    // EmuShelf's own FileProvider for ROMs, declared in the head's AndroidManifest with a root-path map so
    // it covers any mount (internal storage and removable microSD alike). Distinct from the update installer's
    // provider so the two path maps never overlap. A URI minted here is one EmuShelf inherently has authority
    // to grant, which is what lets the read delegate to any emulator without a per-launch SAF pick.
    private const string RomFileProviderAuthority = "com.emushelf.app.romprovider";

    // Files a multi-file descriptor can name as siblings: the raw tracks it points at, plus the nested
    // descriptors an .m3u points at (each .cue/.gdi, which in turn names its own .bin tracks). Used only to
    // scope the extra per-sibling FileProvider grants for the real-path (DuckStation) case; over-granting is
    // avoided by also requiring the sibling to share the descriptor's base name.
    private static readonly string[] SiblingExtensions =
        [".bin", ".iso", ".img", ".sub", ".wav", ".flac", ".dat", ".raw", ".ogg", ".mp3", ".cue", ".gdi"];

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
    /// Fires <paramref name="request"/> at its emulator, handing the ROM off per <paramref name="handoff"/>.
    /// Returns false (without throwing) when there is no context to start from or the target activity cannot
    /// be resolved — e.g. the emulator is not installed, which the caller should have caught with a
    /// package-visibility check first. <paramref name="launchDisplayId"/>, when set, targets a specific
    /// physical display (the Thor's second screen) via <c>ActivityOptions.setLaunchDisplayId</c>; null
    /// launches on the default display. The target is a request Android forwards to the emulator — an app that
    /// forces its own display or ignores the option still lands where it insists, which is why the caller
    /// verifies on-device.
    /// </summary>
    public bool Launch(AndroidIntentRequest request, AndroidRomHandoff handoff, int? launchDisplayId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handoff);

        var ctx = context();
        if (ctx is null)
        {
            logger.Warning($"Cannot launch {request.Component}: no Android context is available.");
            return false;
        }

        return TryStart(ctx, request, handoff, launchDisplayId);
    }

    private bool TryStart(Context ctx, AndroidIntentRequest request, AndroidRomHandoff handoff, int? launchDisplayId)
    {
        using var intent = new Intent();
        intent.SetComponent(new ComponentName(request.PackageName, request.ActivityName));

        if (!string.IsNullOrEmpty(request.Action))
            intent.SetAction(request.Action);

        if (!string.IsNullOrEmpty(request.DataUri))
            intent.SetData(AndroidUri.Parse(request.DataUri));

        foreach (var (key, value) in request.StringExtras)
            intent.PutExtra(key, value);

        foreach (var (key, value) in request.BoolExtras)
            intent.PutExtra(key, value);

        // Replace the ROM reference the pure factory baked in (a synthesized externalstorage URI) with a
        // FileProvider URI EmuShelf can actually grant, and delegate the read to the emulator's package.
        // RetroArch (DelegateViaFileProvider == false) keeps its plain-path extra untouched.
        if (handoff.DelegateViaFileProvider)
            ApplyRomHandoff(ctx, intent, request, handoff);

        // The emulator runs as its own task and becomes the top-resumed activity; NEW_TASK is required
        // because we may be starting it from a non-Activity context, and it is what makes the eventual
        // onTopResumedActivityChanged exit signal (Milestone B) fire when the user returns to EmuShelf.
        intent.AddFlags(ActivityFlags.NewTask);

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
        catch (Exception ex)
        {
            logger.Error($"Could not launch {request.Component}.", ex);
            return false;
        }
    }

    // Puts the ROM into the slot the emulator expects (data URI or a named string extra) as a URI EmuShelf
    // can grant, and grants it to the emulator's package before startActivity so the read is available on the
    // first frame. Two shapes:
    //   • single-file ROM  → a FileProvider content:// URI, granted (+ ClipData so the grant follows a URI
    //     that rides in a string extra, not just the data slot);
    //   • multi-file descriptor for DuckStation → a real file:// path (a FileProvider URI hides the base
    //     folder it resolves sibling tracks against), with the descriptor and its siblings additionally
    //     granted as FileProvider URIs in ClipData as a fallback for content-based reads.
    private void ApplyRomHandoff(Context ctx, Intent intent, AndroidIntentRequest request, AndroidRomHandoff handoff)
    {
        try
        {
            if (handoff.PreferRealPath)
            {
                ApplyRealPathHandoff(ctx, intent, request, handoff);
                return;
            }

            var romUri = FileProviderUriFor(ctx, handoff.RealPath);
            if (romUri is null)
            {
                logger.Warning(
                    $"Could not expose '{handoff.RealPath}' through EmuShelf's FileProvider; " +
                    $"handing {request.Component} the original reference (it may prompt for its own access).");
                return;
            }

            PlacePayload(intent, handoff.PayloadExtraName, romUri.ToString()!);
            GrantAndAttach(ctx, intent, request.PackageName, romUri);
        }
        catch (Exception ex)
        {
            // A handoff failure must not turn a launchable game into a dead button: log and fall through to
            // start the intent with whatever the pure factory baked in.
            logger.Warning($"Could not apply the FileProvider ROM handoff for {request.Component}.", ex);
        }
    }

    private void ApplyRealPathHandoff(Context ctx, Intent intent, AndroidIntentRequest request, AndroidRomHandoff handoff)
    {
        // The descriptor rides as a real file:// path in its string extra. It rides in an extra, never the
        // data slot or ClipData, so it is not subject to Android's file-URI-exposure check (that only guards
        // getData()/getClipData()). DuckStation is the only emulator that takes this path today.
        PlacePayload(intent, handoff.PayloadExtraName, "file://" + handoff.RealPath);

        // Belt and suspenders: also grant the descriptor and its sibling tracks as FileProvider URIs in
        // ClipData, so a content-based read still resolves even though the primary reference is a path.
        var descriptorUri = FileProviderUriFor(ctx, handoff.RealPath);
        if (descriptorUri is not null)
            GrantAndAttach(ctx, intent, request.PackageName, descriptorUri);

        foreach (var sibling in SiblingTrackPaths(handoff.RealPath))
        {
            var siblingUri = FileProviderUriFor(ctx, sibling);
            if (siblingUri is not null)
                GrantAndAttach(ctx, intent, request.PackageName, siblingUri);
        }
    }

    private static void PlacePayload(Intent intent, string? payloadExtraName, string payload)
    {
        if (string.IsNullOrEmpty(payloadExtraName))
            intent.SetData(AndroidUri.Parse(payload));
        else
            intent.PutExtra(payloadExtraName, payload);
    }

    // Grants a FileProvider URI to the emulator's package (synchronously, so the permission exists before
    // startActivity — the intent flag alone is applied asynchronously and can lose a first-launch race) and
    // attaches it to the intent's ClipData so the grant follows the URI wherever it rides.
    private void GrantAndAttach(Context ctx, Intent intent, string packageName, AndroidUri uri)
    {
        try
        {
            ctx.GrantUriPermission(
                packageName, uri,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not grant {packageName} read access to {uri}.", ex);
        }

        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        intent.AddFlags(ActivityFlags.GrantWriteUriPermission);

        // Whichever URI attaches first creates the ClipData; the rest append. Self-sufficient on purpose:
        // a fallback grant (a sibling track) still lands in ClipData even if an earlier grant — e.g. the
        // descriptor's own FileProvider wrap — was skipped because it could not be minted.
        if (intent.ClipData is null)
            intent.ClipData = ClipData.NewRawUri("ROM", uri);
        else
            intent.ClipData.AddItem(new ClipData.Item(uri));
    }

    private AndroidUri? FileProviderUriFor(Context ctx, string realPath)
    {
        try
        {
            var file = new Java.IO.File(realPath);
            if (!file.Exists())
                return null;

            return AndroidX.Core.Content.FileProvider.GetUriForFile(ctx, RomFileProviderAuthority, file);
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not build a FileProvider URI for '{realPath}'.", ex);
            return null;
        }
    }

    // The sibling files a multi-file descriptor references: same directory, and either the exact same base
    // name (Game.cue → Game.bin) or a sibling extension whose name starts with the descriptor's base
    // (Game.m3u → "Game (Disc 1).cue" → "Game (Disc 1).bin"). Scoped this way so a shared folder does not
    // over-grant other games.
    private static IEnumerable<string> SiblingTrackPaths(string descriptorPath)
    {
        var directory = Path.GetDirectoryName(descriptorPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            yield break;

        var baseName = Path.GetFileNameWithoutExtension(descriptorPath);

        foreach (var path in Directory.EnumerateFiles(directory))
        {
            if (string.Equals(path, descriptorPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileBase = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);

            var sameBase = string.Equals(fileBase, baseName, StringComparison.OrdinalIgnoreCase);
            var isSibling =
                Array.Exists(SiblingExtensions, e => string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)) &&
                fileBase.StartsWith(baseName, StringComparison.OrdinalIgnoreCase);

            if (sameBase || isSibling)
                yield return path;
        }
    }
}
