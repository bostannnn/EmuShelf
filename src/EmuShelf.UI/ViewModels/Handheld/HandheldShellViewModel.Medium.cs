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
    private DispatcherTimer? _mediumTimer;
    private long _mediumTickTimestamp;

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
        if (_mediumTimer is null)
        {
            _mediumTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = interval };
            _mediumTimer.Tick += (_, _) => TickMedium();
        }
        else if (_mediumTimer.Interval != interval)
        {
            _mediumTimer.Interval = interval;
        }
        if (!_mediumTimer.IsEnabled)
        {
            _mediumTickTimestamp = _clock.GetTimestamp();
            _mediumTimer.Start();
        }
    }

    private void TickMedium()
    {
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_mediumTickTimestamp, now).TotalMilliseconds;
        _mediumTickTimestamp = now;
        if (_medium.Tick(elapsed))
            PushMediumPose();
        UpdateMediumTimer();
    }

    private void StopMediumTimer()
    {
        _mediumTimer?.Stop();
    }
}
