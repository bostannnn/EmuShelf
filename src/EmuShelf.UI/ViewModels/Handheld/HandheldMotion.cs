using System;
using System.Collections.Generic;
using Avalonia.Animation.Easings;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Handheld mode's motion numbers (HANDHELD-MODE.md "Motion spec"), in one place so the views, the shell and
/// the spec agree. Start values came from the mockup (docs/prototypes/handheld-mode, version 5); the device
/// tuning is written back into the spec.
/// </summary>
public static class HandheldMotion
{
    /// <summary>The selector: stiffness 560, damping 43 (damping ratio ≈ 0.91, barely any overshoot).</summary>
    public static readonly HandheldSpringParameters Selector = new(560, 43);

    /// <summary>Tracks (home pages, folder columns): stiffness 250, damping 31.5 (≈ 1.0, no overshoot).</summary>
    public static readonly HandheldSpringParameters Track = new(250, 31.5);

    /// <summary>The top screen's centre pop: stiffness 190, damping 17, from 0.72× (a couple of percent over).</summary>
    public static readonly HandheldSpringParameters Pop = new(190, 17);

    public const double PopFromScale = 0.72;
    public const double PlateFromScale = 0.94;
    public const double PlatePopMilliseconds = 240;
    public const double OutgoingScale = 0.86;
    public const double OutgoingMilliseconds = 150;

    /// <summary>The focused tile's lift: 1.13× in 220 ms with a small overshoot.</summary>
    public const double LiftScale = 1.13;
    public const double LiftMilliseconds = 220;

    /// <summary>The selector's glow breathes on a 1.2 s cycle (600 ms each way).</summary>
    public const double BreathHalfCycleMilliseconds = 600;

    /// <summary>After each focus change the glow breathes 4 times (4.8 s), then rests at full opacity.</summary>
    public const int BreathHalfCycles = 8;

    /// <summary>The edge bump: the selector nudges 7% of its size toward the wall and eases back in 300 ms.</summary>
    public const double BumpFraction = 0.07;
    public const double BumpMilliseconds = 300;

    /// <summary>Presses closer together than this are a held direction: the top screen waits for it to settle.</summary>
    public static readonly TimeSpan HeldDirectionWindow = TimeSpan.FromMilliseconds(150);

    /// <summary>How long the top screen waits after the last press of a held direction.</summary>
    public static readonly TimeSpan HeldDirectionSettle = TimeSpan.FromMilliseconds(140);

    /// <summary>After a folder opens the top screen shows the open folder this long, then the first game.</summary>
    public static readonly TimeSpan FolderOpenStageHold = TimeSpan.FromMilliseconds(300);

    /// <summary>The big folder's flap tilts this long after the folder takes the focus.</summary>
    public const double FlapPeekDelayMilliseconds = 200;

    // Folder open, "Zoom through" (M45 H4, picked in the folder lab): two objects, one direction. The folder
    // tile grows toward the viewer and fades; home fades under it; the folder page (header and grid, one
    // surface) grows out of the tile's place on the springy curve, fading in over its first 45%.
    public const double ZoomTileScale = 2.6;
    public const double ZoomTileOutMilliseconds = 300;
    public const double ZoomHomeOutDelayMilliseconds = 40;
    public const double ZoomHomeOutMilliseconds = 240;
    public const double ZoomSheetFromScale = 0.55;

    /// <summary>
    /// The sheet's springy arrival, baked into a curve: stiffness 600, damping 32 (ratio ≈ 0.65), settling in
    /// ≈ 0.38 s with ≈ 3% over full size. The lab's cubic-bezier(.22, 1.1, .36, 1) overshoots by only ≈ 0.1%
    /// of the scale, so the spring carries the "springy" part of the pick.
    /// </summary>
    public static readonly HandheldSpringParameters ZoomSheet = new(600, 32);

    /// <summary>The tile's return from 2.6× on close: stiffness 576, damping 38.8 (≈ 0.81), ≈ 2% under 1× before it rests.</summary>
    public static readonly HandheldSpringParameters ZoomTile = new(576, 38.8);
    public const double ZoomSheetInDelayMilliseconds = 60;
    public const double ZoomSheetInMilliseconds = 380;
    public const float ZoomSheetFadeInPortion = 0.45f;

    // Folder close: the reverse. The page shrinks back into the tile's place, fading over its last 30%; home
    // fades in; the tile comes back from 2.6× on the springy curve.
    public const double ZoomSheetOutMilliseconds = 260;
    public const float ZoomSheetFadeOutFrom = 0.7f;
    public const double ZoomHomeInDelayMilliseconds = 60;
    public const double ZoomHomeInMilliseconds = 240;
    public const double ZoomTileInDelayMilliseconds = 40;
    public const double ZoomTileInMilliseconds = 320;

    /// <summary>The selector hides while the page zooms and fades back in once it has landed (140 ms).</summary>
    public const double ZoomSelectorHiddenMilliseconds = 260;
    public const double ZoomSelectorFadeMilliseconds = 140;

    /// <summary>On close the big folder arrives open on the Stage and its flap closes this much later.</summary>
    public const double StageFlapCloseDelayMilliseconds = 220;

    // The Stage's era motif (M45 H4): a cartridge system's pixel grid with drifting blocks, a disc system's
    // scanlines with a rolling bar, in the focused system's colour; translate-only on pre-rendered tiles.
    public const double BackdropCrossfadeMilliseconds = 450;

    /// <summary>A backdrop change waits for this much quiet, so a run of presses crossfades once, at the end.</summary>
    public const double BackdropSettleMilliseconds = 250;
    public const double CartridgeGridPitch = 22;
    public const double CartridgeGridLine = 2;
    public const double CartridgeBlockSize = 22;
    public const double CartridgeBlockTileWidth = 176;
    public const double CartridgeBlockTileHeight = 132;
    public const double CartridgeDriftSeconds = 22;

    /// <summary>The blocks step 4 times a second (2 px across, 1–2 px down), never at the panel rate.</summary>
    public const double CartridgeDriftStepMilliseconds = 250;
    public const double DiscScanlinePitch = 5;
    public const double DiscScanlineHeight = 2;
    public const double DiscBarHeight = 140;
    /// <summary>
    /// The bar's pass, top to bottom. Slowed from the lab's 9 s so that, stepped 6 times a second, each step is
    /// about 8 px of a soft 140 px gradient instead of 22 px.
    /// </summary>
    public const double DiscBarSeconds = 24;
    public const double DiscBarStepMilliseconds = 1000d / 6;

    // Launch: the tile/cover presses in (0.94×) and pops (1.1×); the cover lifts off the Launcher, lands on
    // the Stage and zooms into the emulator. The emulator starts once the cover has landed.
    public const double PressScale = 0.94;
    public const double PressPopScale = 1.1;
    public const double PressMilliseconds = 260;
    public const double LaunchLiftMilliseconds = 420;
    public const double LaunchLandDelayMilliseconds = 140;
    public const double LaunchLandMilliseconds = 520;
    public const double LaunchZoomMilliseconds = 460;
    public const double LaunchZoomScale = 1.7;

    /// <summary>The launch waits for the cover to land on the Stage before the emulator starts.</summary>
    public static readonly TimeSpan LaunchLead = TimeSpan.FromMilliseconds(LaunchLandDelayMilliseconds + LaunchLandMilliseconds);

    /// <summary>The mockup's POP curve, cubic-bezier(.22, 1.1, .36, 1): fast out with a hair of overshoot (the lab's SPRINGY).</summary>
    public static Easing PopCurve() => new SplineEasing(0.22, 1.1, 0.36, 1);

    /// <summary>The lab's EASE, cubic-bezier(.4, 0, .2, 1): the standard ease-in-out for things that leave.</summary>
    public static Easing ZoomEase() => new SplineEasing(0.4, 0, 0.2, 1);

    /// <summary>The mockup's tile curve, cubic-bezier(.34, 1.42, .64, 1): the lift's ~8% overshoot.</summary>
    public static Easing LiftCurve() => new SplineEasing(0.34, 1.42, 0.64, 1);
}

/// <summary>A spring's stiffness and damping (unit mass).</summary>
public readonly record struct HandheldSpringParameters(double Stiffness, double Damping)
{
    public double DampingRatio => Damping / (2 * Math.Sqrt(Stiffness));
}

/// <summary>
/// A one-dimensional spring that keeps its speed when its target changes, so fast repeated presses glide
/// instead of restarting from rest the way a timed curve does. Integrated in fixed 240 Hz substeps, so a
/// long frame never destabilises it. Pure and clock-free: the caller passes the frame's elapsed time.
/// </summary>
public sealed class HandheldSpring(HandheldSpringParameters parameters, double restDistance = 0.05, double restSpeed = 0.5)
{
    private const double Substep = 1d / 240d;
    private const double MaxFrameSeconds = 1d / 20d;

    public HandheldSpringParameters Parameters { get; } = parameters;
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }

    public bool IsSettled => Value == Target && Velocity == 0;

    /// <summary>Moves the target; the value carries on from where it is, at the speed it has.</summary>
    public void Retarget(double target) => Target = target;

    /// <summary>Jumps straight to <paramref name="value"/> at rest (first layout, a re-host).</summary>
    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    /// <summary>Shifts value and target together, keeping the speed (a track moved under a rider).</summary>
    public void Shift(double delta)
    {
        Value += delta;
        Target += delta;
    }

    /// <summary>
    /// Moves the value by <paramref name="delta"/> without moving the target, keeping the speed: what was on
    /// screen stays put while the thing under it jumped (a folder's window scrolled by a column), then glides.
    /// </summary>
    public void Displace(double delta) => Value += delta;

    /// <summary>
    /// Keeps the value within <paramref name="distance"/> of the target, keeping the speed: a track that may
    /// only lag by what is realized behind it (a folder's one spare column) never shows the empty space past it.
    /// </summary>
    public void Limit(double distance)
    {
        if (Value - Target > distance)
            Value = Target + distance;
        else if (Target - Value > distance)
            Value = Target - distance;
    }

    /// <summary>Advances the spring. Returns whether it moved.</summary>
    public bool Step(double seconds)
    {
        if (IsSettled || seconds <= 0)
            return false;
        var remaining = Math.Min(seconds, MaxFrameSeconds);
        while (remaining > 0)
        {
            var h = Math.Min(Substep, remaining);
            var acceleration = (-Parameters.Stiffness * (Value - Target)) - (Parameters.Damping * Velocity);
            Velocity += acceleration * h;
            Value += Velocity * h;
            remaining -= h;
        }
        if (Math.Abs(Target - Value) < restDistance && Math.Abs(Velocity) < restSpeed)
            Snap(Target);
        return true;
    }
}

/// <summary>
/// A spring's step response baked into an easing curve, for one-shot composition animations that should feel
/// like the spring (the top screen's centre pop). Normalised: 0 at the start, settling at exactly 1, with the
/// spring's own overshoot in between. <see cref="Duration"/> is how long the spring takes to settle.
/// </summary>
public sealed class HandheldSpringEasing : Easing
{
    private const double SampleRate = 240;
    private readonly double[] _samples;

    public HandheldSpringEasing(HandheldSpringParameters parameters, double from = 0, double to = 1)
    {
        // Simulate the spring from `from` toward `to` and keep the normalised trajectory until it settles.
        var samples = new List<double>();
        var value = from;
        var velocity = 0d;
        var span = to - from;
        var dt = 1 / SampleRate;
        for (var i = 0; i < SampleRate * 3; i++)
        {
            samples.Add((value - from) / span);
            var acceleration = (-parameters.Stiffness * (value - to)) - (parameters.Damping * velocity);
            velocity += acceleration * dt;
            value += velocity * dt;
            if (i > 20 && Math.Abs(value - to) < Math.Abs(span) * 0.002 && Math.Abs(velocity) < Math.Abs(span) * 0.05)
                break;
        }
        samples.Add(1);
        _samples = samples.ToArray();
        Duration = TimeSpan.FromSeconds((_samples.Length - 1) / SampleRate);
    }

    public TimeSpan Duration { get; }

    /// <summary>The largest value on the curve (1 + the overshoot).</summary>
    public double Peak
    {
        get
        {
            var peak = 0d;
            foreach (var sample in _samples)
                peak = Math.Max(peak, sample);
            return peak;
        }
    }

    public override double Ease(double progress)
    {
        if (progress <= 0)
            return 0;
        if (progress >= 1)
            return 1;
        var position = progress * (_samples.Length - 1);
        var index = (int)position;
        var fraction = position - index;
        return _samples[index] + ((_samples[index + 1] - _samples[index]) * fraction);
    }
}

/// <summary>
/// The top screen's "once per move" rule. A single press updates the Stage at once; a held direction
/// (presses closer than <see cref="HandheldMotion.HeldDirectionWindow"/>) waits until the movement settles.
/// </summary>
public sealed class HandheldHeldDirection(TimeProvider clock)
{
    private long? _lastPress;

    /// <summary>Records a navigation press and returns whether it is part of a held direction.</summary>
    public bool Press()
    {
        var now = clock.GetTimestamp();
        var held = _lastPress is { } last && clock.GetElapsedTime(last, now) < HandheldMotion.HeldDirectionWindow;
        _lastPress = now;
        return held;
    }

    /// <summary>Forgets the last press (anything that is not a move breaks the run).</summary>
    public void Reset() => _lastPress = null;
}
