using Android.Content;
using JavaSystem = Java.Lang.JavaSystem;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Restarts EmuShelf's own process (the ProcessPhoenix approach). Used to hand off from first-run onboarding
/// to the real shell: Avalonia's Android single-view host captures its <c>MainView</c> at startup and does
/// not re-render when it is reassigned in-process, so a fresh process is the reliable way to bring up the
/// composed shell — which then resolves the just-persisted data-location pointer and boots straight to the
/// library. (The same mechanism serves the Settings "change data folder" row.)
///
/// The launch activity is started <b>while EmuShelf is still foreground</b> (onboarding just completed), so
/// the start is permitted; then the process exits. The earlier AlarmManager-then-kill scheme failed on
/// Android 13 (the Thor) because a background activity start from a dead process is blocked — it dropped the
/// user to the home screen. Starting foreground with <c>CLEAR_TASK</c> and exiting makes Android recreate the
/// task's root activity in a new process.
/// </summary>
public static class AndroidAppRelaunch
{
    public static void Restart(Context? context)
    {
        if (context is null)
            return;

        var launch = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        if (launch is null)
            return;

        launch.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTask);
        context.StartActivity(launch);

        // Kill this process now that the new task is queued; Android relaunches the root activity fresh.
        JavaSystem.Exit(0);
    }
}
