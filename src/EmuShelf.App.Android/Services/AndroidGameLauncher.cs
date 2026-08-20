using System;
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
    /// Fires <paramref name="request"/> at its emulator. Returns false (without throwing) when there is no
    /// context to start from or the target activity cannot be resolved — e.g. the emulator is not
    /// installed, which the caller should have caught with a package-visibility check first.
    /// </summary>
    public bool Launch(AndroidIntentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ctx = context();
        if (ctx is null)
        {
            logger.Warning($"Cannot launch {request.Component}: no Android context is available.");
            return false;
        }

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

        if (request.GrantReadUriPermission)
            intent.AddFlags(ActivityFlags.GrantReadUriPermission);

        // The emulator runs as its own task and becomes the top-resumed activity; NEW_TASK is required
        // because we may be starting it from a non-Activity context, and it is what makes the eventual
        // onTopResumedActivityChanged exit signal (Milestone B) fire when the user returns to EmuShelf.
        intent.AddFlags(ActivityFlags.NewTask);

        try
        {
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
}
