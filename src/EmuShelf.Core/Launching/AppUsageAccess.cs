using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

/// <summary>
/// The platform's record of which app was on screen when, used to time play accurately (M46 P2). On
/// Android this is <c>UsageStatsManager</c> behind the user's Usage access grant; desktop has no such
/// source and leaves it null, since it times the emulator process it waits for.
/// </summary>
public interface IAppUsageAccess
{
    /// <summary>Whether the user has granted access. A binder round trip on Android: cache it.</summary>
    bool IsGranted { get; }

    /// <summary>Opens the platform's grant screen; the answer is read later through <see cref="IsGranted"/>.</summary>
    void RequestGrant();

    /// <summary>
    /// The foreground events of <paramref name="packageName"/>, plus screen-off events, between the two
    /// times, oldest first. Null when access is not granted or the platform could not answer.
    /// </summary>
    IReadOnlyList<AppForegroundEvent>? ReadForegroundEvents(string packageName, DateTimeOffset from, DateTimeOffset to);
}

public enum AppForegroundEventKind
{
    /// <summary>One of the app's activities came to the foreground.</summary>
    Resumed,

    /// <summary>One of the app's activities left the foreground (paused or stopped).</summary>
    Paused,

    /// <summary>The screen turned off: nothing is on screen until an activity resumes again.</summary>
    ScreenOff,
}

/// <param name="At">When it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Activity">The activity's class name; null for <see cref="AppForegroundEventKind.ScreenOff"/>.</param>
public sealed record AppForegroundEvent(DateTimeOffset At, AppForegroundEventKind Kind, string? Activity = null);

/// <summary>How long an app was on screen, from its foreground events.</summary>
public static class ForegroundTime
{
    /// <summary>
    /// The time between <paramref name="from"/> and <paramref name="to"/> during which at least one of the
    /// app's activities was resumed and the screen was on. Null when no event of the app falls in the window,
    /// which means the platform no longer holds them (or never saw the app), not that it was never played.
    /// </summary>
    public static TimeSpan? Measure(IEnumerable<AppForegroundEvent> events, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (to <= from)
            return TimeSpan.Zero;

        var resumed = new HashSet<string>(StringComparer.Ordinal);
        var total = TimeSpan.Zero;
        var segmentStart = from;
        var sawApp = false;

        foreach (var e in events.Where(e => e.At >= from && e.At <= to).OrderBy(e => e.At))
        {
            switch (e.Kind)
            {
                case AppForegroundEventKind.Resumed:
                    if (resumed.Count == 0)
                        segmentStart = e.At;
                    resumed.Add(e.Activity ?? string.Empty);
                    sawApp = true;
                    break;

                case AppForegroundEventKind.Paused:
                    var activity = e.Activity ?? string.Empty;
                    if (resumed.Remove(activity))
                    {
                        if (resumed.Count == 0)
                            total += e.At - segmentStart;
                    }
                    else if (!sawApp)
                    {
                        // The first thing seen is a pause: the activity resumed before the window opened (the
                        // launch is stamped a moment before the emulator starts), so it was on screen from then.
                        total += e.At - from;
                    }
                    sawApp = true;
                    break;

                case AppForegroundEventKind.ScreenOff:
                    if (resumed.Count > 0)
                    {
                        total += e.At - segmentStart;
                        resumed.Clear();
                    }
                    break;
            }
        }

        // Still on screen when EmuShelf came back (on a dual-screen device the game keeps the other display).
        if (resumed.Count > 0)
            total += to - segmentStart;
        return sawApp ? total : null;
    }
}

/// <summary>How the length of an Android session (launched, then returned to) is decided.</summary>
public static class DeferredPlayTiming
{
    /// <summary>
    /// The emulator's on-screen time when usage events cover the session, else the wall-clock gap from launch
    /// to return, marked as an estimate.
    /// </summary>
    public static (TimeSpan Duration, PlaySessionMeasurement Measurement) Resolve(
        DateTimeOffset startedAt,
        DateTimeOffset returnedAt,
        IReadOnlyList<AppForegroundEvent>? events)
    {
        if (events is not null && ForegroundTime.Measure(events, startedAt, returnedAt) is { } onScreen)
            return (onScreen, PlaySessionMeasurement.UsageAccess);

        var wallClock = returnedAt - startedAt;
        return (wallClock > TimeSpan.Zero ? wallClock : TimeSpan.Zero, PlaySessionMeasurement.WallClockEstimate);
    }
}
