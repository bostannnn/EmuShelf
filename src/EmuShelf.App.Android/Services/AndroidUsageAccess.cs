using System;
using System.Collections.Generic;
using Android.App;
using Android.App.Usage;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using Uri = Android.Net.Uri;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Android's Usage access (<c>PACKAGE_USAGE_STATS</c>), the special permission the user grants on a system
/// Settings screen, as Cocoon asks for it. With it, the emulator's activity resumed / paused events and the
/// screen-off events say how long a game was really on screen (M46 P2). Every call degrades to "not
/// granted" or null rather than throwing: play time then falls back to the wall-clock estimate. The activity
/// resumed / paused event types exist from Android 10 (API 29); below that the feature reports not granted.
/// </summary>
public sealed class AndroidUsageAccess(Func<Context?> context, IAppLogger logger) : IAppUsageAccess
{
    private const string UsageStatsPermission = "android.permission.PACKAGE_USAGE_STATS";

    public bool IsGranted
    {
        get
        {
            var ctx = context();
            if (ctx is null || !OperatingSystem.IsAndroidVersionAtLeast(29))
                return false;
            try
            {
                var appOps = (AppOpsManager?)ctx.GetSystemService(Context.AppOpsService);
                if (appOps is null)
                    return false;
#pragma warning disable CA1422 // obsoleted on API 36 for a raw variant, but still the documented check below it
                var mode = appOps.UnsafeCheckOpNoThrow(AppOpsManager.OpstrGetUsageStats!, Process.MyUid(), ctx.PackageName!);
#pragma warning restore CA1422
                // "Default" means the permission decides (some firmware leaves the op unset after a grant).
                return mode == AppOpsManagerMode.Allowed ||
                       (mode == AppOpsManagerMode.Default &&
                        ctx.CheckSelfPermission(UsageStatsPermission) == Permission.Granted);
            }
            catch (Exception ex)
            {
                logger.Warning($"Could not read the Usage access grant: {ex.Message}");
                return false;
            }
        }
    }

    public void RequestGrant()
    {
        var ctx = context();
        if (ctx is null)
            return;
        // The package-scoped screen lands on EmuShelf's own toggle where the firmware supports it; otherwise
        // the full list, where the user finds EmuShelf.
        if (TryStart(ctx, Uri.Parse("package:" + ctx.PackageName)) || TryStart(ctx, data: null))
            return;
        logger.Warning("Could not open Android's Usage access screen on this firmware.");
    }

    public IReadOnlyList<AppForegroundEvent>? ReadForegroundEvents(
        string packageName,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var ctx = context();
        if (ctx is null || !OperatingSystem.IsAndroidVersionAtLeast(29) || !IsGranted)
            return null;
        try
        {
            var usage = (UsageStatsManager?)ctx.GetSystemService(Context.UsageStatsService);
            using var events = usage?.QueryEvents(from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds());
            if (events is null)
                return null;

            var result = new List<AppForegroundEvent>();
            using var e = new UsageEvents.Event();
            while (events.HasNextEvent && events.GetNextEvent(e))
            {
                var at = DateTimeOffset.FromUnixTimeMilliseconds(e.TimeStamp);
                switch (e.EventType)
                {
                    case UsageEventType.ActivityResumed when e.PackageName == packageName:
                        result.Add(new AppForegroundEvent(at, AppForegroundEventKind.Resumed, e.ClassName));
                        break;
                    case UsageEventType.ActivityPaused or UsageEventType.ActivityStopped
                        when e.PackageName == packageName:
                        result.Add(new AppForegroundEvent(at, AppForegroundEventKind.Paused, e.ClassName));
                        break;
                    case UsageEventType.ScreenNonInteractive:
                        result.Add(new AppForegroundEvent(at, AppForegroundEventKind.ScreenOff));
                        break;
                }
            }
            return result;
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not read usage events for {packageName}: {ex.Message}");
            return null;
        }
    }

    private bool TryStart(Context ctx, Uri? data)
    {
        try
        {
            using var intent = data is null
                ? new Intent(Settings.ActionUsageAccessSettings)
                : new Intent(Settings.ActionUsageAccessSettings, data);
            intent.AddFlags(ActivityFlags.NewTask);
            if (intent.ResolveActivity(ctx.PackageManager!) is null)
                return false;
            ctx.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not start the Usage access screen: {ex.Message}");
            return false;
        }
    }
}
