using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public class ForegroundTimeTests
{
    private static readonly DateTimeOffset Launch = DateTimeOffset.FromUnixTimeMilliseconds(1786000000000);
    private const string Emu = "org.duckstation.MainActivity";

    private static AppForegroundEvent At(double minutes, AppForegroundEventKind kind, string? activity = Emu) =>
        new(Launch.AddMinutes(minutes), kind, kind == AppForegroundEventKind.ScreenOff ? null : activity);

    [Fact]
    public void OneSitting_IsTheTimeBetweenResumeAndPause()
    {
        var events = new[] { At(0.1, AppForegroundEventKind.Resumed), At(30.1, AppForegroundEventKind.Paused) };

        Assert.Equal(TimeSpan.FromMinutes(30), ForegroundTime.Measure(events, Launch, Launch.AddMinutes(31)));
    }

    [Fact]
    public void ScreenOffMidGame_CountsOnlyTheAwakeMinutes()
    {
        // 20 minutes, the device sleeps for 8 hours, then 10 more minutes before returning to EmuShelf.
        var events = new[]
        {
            At(0, AppForegroundEventKind.Resumed),
            At(20, AppForegroundEventKind.Paused),
            At(20, AppForegroundEventKind.ScreenOff),
            At(500, AppForegroundEventKind.Resumed),
            At(510, AppForegroundEventKind.Paused),
        };

        Assert.Equal(TimeSpan.FromMinutes(30), ForegroundTime.Measure(events, Launch, Launch.AddMinutes(511)));
    }

    [Fact]
    public void ScreenOffWithoutAPause_StillEndsTheSitting()
    {
        var events = new[]
        {
            At(0, AppForegroundEventKind.Resumed),
            At(15, AppForegroundEventKind.ScreenOff),
            At(300, AppForegroundEventKind.Resumed),
            At(305, AppForegroundEventKind.Paused),
        };

        Assert.Equal(TimeSpan.FromMinutes(20), ForegroundTime.Measure(events, Launch, Launch.AddMinutes(306)));
    }

    [Fact]
    public void TimeInAnotherApp_DoesNotCount_AndTwoActivitiesOverlapOnce()
    {
        var events = new[]
        {
            At(0, AppForegroundEventKind.Resumed, "Main"),
            At(5, AppForegroundEventKind.Resumed, "Game"),
            At(6, AppForegroundEventKind.Paused, "Main"),
            At(10, AppForegroundEventKind.Paused, "Game"), // away in a browser from 10 to 40
            At(40, AppForegroundEventKind.Resumed, "Game"),
            At(45, AppForegroundEventKind.Paused, "Game"),
        };

        Assert.Equal(TimeSpan.FromMinutes(15), ForegroundTime.Measure(events, Launch, Launch.AddMinutes(46)));
    }

    [Fact]
    public void StillOnScreenAtReturn_CountsUpToTheReturn()
    {
        // On a dual-screen device the game keeps its display while EmuShelf comes back on the other one.
        var events = new[] { At(1, AppForegroundEventKind.Resumed) };

        Assert.Equal(TimeSpan.FromMinutes(59), ForegroundTime.Measure(events, Launch, Launch.AddMinutes(60)));
    }

    [Fact]
    public void APauseFirst_MeansTheGameWasOnScreenFromTheLaunch()
    {
        var events = new[] { At(12, AppForegroundEventKind.Paused) };

        Assert.Equal(TimeSpan.FromMinutes(12), ForegroundTime.Measure(events, Launch, Launch.AddMinutes(13)));
    }

    [Fact]
    public void NoEventOfTheApp_IsUnknown_NotZero()
    {
        var events = new[] { At(3, AppForegroundEventKind.ScreenOff) };

        Assert.Null(ForegroundTime.Measure(events, Launch, Launch.AddMinutes(60)));
        Assert.Null(ForegroundTime.Measure([], Launch, Launch.AddMinutes(60)));
    }

    [Fact]
    public void EventsOutsideTheSession_AreIgnored()
    {
        var events = new[]
        {
            At(-30, AppForegroundEventKind.Resumed),
            At(-20, AppForegroundEventKind.Paused),
            At(0, AppForegroundEventKind.Resumed),
            At(10, AppForegroundEventKind.Paused),
            At(90, AppForegroundEventKind.Resumed),
        };

        Assert.Equal(TimeSpan.FromMinutes(10), ForegroundTime.Measure(events, Launch, Launch.AddMinutes(60)));
    }

    [Fact]
    public void ARealThorSession_WithThreeSleeps_CountsOnlyTheTwentyNineAwakeMinutes()
    {
        // Recorded on the AYN Thor (Android 13) on 2026-11-07 with `adb shell dumpsys usagestats`: an Azahar
        // game launched from EmuShelf at 12:57:30, the device put to sleep three times, and EmuShelf back in
        // front at 14:36:07. The wall-clock estimate credited 98.6 minutes. Android reports the screen going
        // off a second before the pause, and the paused and stopped events of each sleep arrive together;
        // the reader passes both as pauses.
        static DateTimeOffset T(string time) => DateTimeOffset.Parse($"2026-11-07T{time}+03:00");
        const string Activity = "org.citra.citra_emu.activities.EmulationActivity";
        var events = new[]
        {
            new AppForegroundEvent(T("12:57:31"), AppForegroundEventKind.Resumed, Activity),
            new AppForegroundEvent(T("13:19:00"), AppForegroundEventKind.ScreenOff),
            new AppForegroundEvent(T("13:19:01"), AppForegroundEventKind.Paused, Activity),
            new AppForegroundEvent(T("13:19:01"), AppForegroundEventKind.Paused, Activity),
            new AppForegroundEvent(T("13:25:56"), AppForegroundEventKind.Resumed, Activity),
            new AppForegroundEvent(T("13:27:36"), AppForegroundEventKind.ScreenOff),
            new AppForegroundEvent(T("13:27:37"), AppForegroundEventKind.Paused, Activity),
            new AppForegroundEvent(T("13:27:37"), AppForegroundEventKind.Paused, Activity),
            new AppForegroundEvent(T("13:33:34"), AppForegroundEventKind.Resumed, Activity),
            new AppForegroundEvent(T("13:39:23"), AppForegroundEventKind.ScreenOff),
            new AppForegroundEvent(T("13:39:23"), AppForegroundEventKind.Paused, Activity),
            new AppForegroundEvent(T("13:39:23"), AppForegroundEventKind.Paused, Activity),
        };

        var (duration, measurement) = DeferredPlayTiming.Resolve(T("12:57:30"), T("14:36:07"), events);

        Assert.Equal(new TimeSpan(0, 28, 58), duration); // 21:29 + 1:40 + 5:49
        Assert.Equal(PlaySessionMeasurement.UsageAccess, measurement);
    }

    [Fact]
    public void Resolve_UsesTheEvents_WhenTheyCoverTheSession()
    {
        var events = new[] { At(0, AppForegroundEventKind.Resumed), At(25, AppForegroundEventKind.Paused) };

        var (duration, measurement) = DeferredPlayTiming.Resolve(Launch, Launch.AddHours(9), events);

        Assert.Equal(TimeSpan.FromMinutes(25), duration);
        Assert.Equal(PlaySessionMeasurement.UsageAccess, measurement);
    }

    [Fact]
    public void Resolve_FallsBackToTheWallClockEstimate_WithoutEventsOrAccess()
    {
        var withoutAccess = DeferredPlayTiming.Resolve(Launch, Launch.AddMinutes(50), events: null);
        var agedOut = DeferredPlayTiming.Resolve(Launch, Launch.AddMinutes(50), []);
        var clockSkew = DeferredPlayTiming.Resolve(Launch, Launch.AddMinutes(-5), events: null);

        Assert.Equal((TimeSpan.FromMinutes(50), PlaySessionMeasurement.WallClockEstimate), withoutAccess);
        Assert.Equal((TimeSpan.FromMinutes(50), PlaySessionMeasurement.WallClockEstimate), agedOut);
        Assert.Equal((TimeSpan.Zero, PlaySessionMeasurement.WallClockEstimate), clockSkew);
    }
}
