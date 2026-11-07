using System;
using Avalonia.Threading;

namespace EmuShelf.App.ViewModels;

// The focused game's physical medium on the Stage: its pose (HandheldMediumMotion), the frame clock that
// drives it, and the right stick / R3 that turn it. The clock runs only while something moves: fast while
// the spring carries a change, slow for the idle sway, stopped for anything that is not a medium.
public sealed partial class HandheldShellViewModel
{
    // About 60 frames a second while the spring travels; the sway is slow enough for 20.
    private static readonly TimeSpan SettleInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SwayInterval = TimeSpan.FromMilliseconds(50);

    private readonly HandheldMediumMotion _medium = new();
    private long _mediumTickTimestamp;

    // A thread-pool timer that posts each tick, not a DispatcherTimer: on Android an Avalonia DispatcherTimer
    // kept the UI thread about 19% busy for as long as it ran (measured with the tick doing nothing at all),
    // and the idle sway runs for as long as a game rests on the Stage.
    private System.Threading.Timer? _mediumTimer;
    private TimeSpan _mediumInterval;
    private bool _mediumRunning;
    private int _mediumTickQueued;

    /// <summary>The medium's pose, exposed for tests.</summary>
    public HandheldMediumMotion MediumMotion => _medium;

    /// <summary>
    /// One tick of the right stick, from the shared input poll. Turns the medium while a browsed game's
    /// medium is on the Stage and nothing is open over it.
    /// </summary>
    public void ApplyRightStick(float x, float y, double deltaMilliseconds)
    {
        if (!CanTurnMedium || !_medium.ApplyStick(x, y, deltaMilliseconds))
            return;
        PushMediumPose();
        UpdateMediumTimer();
    }

    private bool CanTurnMedium =>
        Stage.HasMedium && !IsMenuOpen && TitlePage is null &&
        (_companion is null || _companion.Overlay == SecondScreenOverlayKind.None);

    /// <summary>R3: the medium swings back to its resting three-quarter view.</summary>
    private void RecentreMedium()
    {
        if (!Stage.HasMedium || !_medium.Recentre())
            return;
        PushMediumPose();
        UpdateMediumTimer();
    }

    private void PushMediumPose()
    {
        Stage.MediumYaw = _medium.Yaw;
        Stage.MediumPitch = _medium.Pitch;
    }

    private void UpdateMediumTimer()
    {
        if (_disposed || !Stage.HasMedium || (!_medium.IsSettling && !_medium.IsSwaying))
        {
            StopMediumTimer();
            return;
        }
        var interval = _medium.IsSettling ? SettleInterval : SwayInterval;
        _mediumTimer ??= new System.Threading.Timer(_ =>
        {
            // One tick queued at a time: a busy UI thread skips ticks rather than piling them up.
            if (System.Threading.Interlocked.Exchange(ref _mediumTickQueued, 1) == 0)
                Dispatcher.UIThread.Post(TickMedium, DispatcherPriority.Render);
        });
        if (!_mediumRunning)
            _mediumTickTimestamp = _clock.GetTimestamp();
        if (!_mediumRunning || interval != _mediumInterval)
        {
            _mediumInterval = interval;
            _mediumRunning = true;
            _mediumTimer.Change(interval, interval);
        }
    }

    private void TickMedium()
    {
        System.Threading.Interlocked.Exchange(ref _mediumTickQueued, 0);
        if (!_mediumRunning)
            return;
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_mediumTickTimestamp, now).TotalMilliseconds;
        _mediumTickTimestamp = now;
        if (_medium.Tick(elapsed))
            PushMediumPose();
        UpdateMediumTimer();
    }

    private void StopMediumTimer()
    {
        _mediumRunning = false;
        _mediumTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }
}
