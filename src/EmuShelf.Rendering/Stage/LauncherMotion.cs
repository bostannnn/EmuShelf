namespace EmuShelf.Rendering.Stage;

/// <summary>
/// The Launcher's motion numbers and curves for the native scene: the same values as the Avalonia Launcher
/// (<c>HandheldMotion</c> in the UI project, HANDHELD-MODE.md "Numbers as built"); a test keeps them equal.
/// </summary>
public static class LauncherMotion
{
    public const double SelectorStiffness = 560;
    public const double SelectorDamping = 43;
    public const double TrackStiffness = 250;
    public const double TrackDamping = 31.5;

    public const float LiftScale = 1.13f;
    public const float LiftMilliseconds = 220f;
    public const float BreathLow = 0.55f;
    public const float BreathHalfCycleMilliseconds = 600f;
    public const int BreathHalfCycles = 8;

    /// <summary>The breath alone steps at 30 Hz; with anything else moving the Launcher draws at the panel's rate.</summary>
    public const float BreathStepMilliseconds = 1000f / 30f;
    public const float BumpFraction = 0.07f;
    public const float BumpMilliseconds = 300f;

    public const float ZoomTileScale = 2.6f;
    public const float ZoomTileOutMilliseconds = 300f;
    public const float ZoomHomeOutDelay = 40f;
    public const float ZoomHomeOutMilliseconds = 240f;
    public const float ZoomSheetFromScale = 0.55f;
    public const float ZoomSheetInDelay = 60f;
    public const float ZoomSheetInMilliseconds = 380f;
    public const float ZoomSheetFadeInPortion = 0.45f;
    public const float ZoomSheetOutMilliseconds = 260f;
    public const float ZoomSheetFadeOutBy = 0.7f;
    public const float ZoomHomeInDelay = 60f;
    public const float ZoomHomeInMilliseconds = 240f;
    public const float ZoomTileInDelay = 40f;
    public const float ZoomTileInMilliseconds = 320f;
    public const float ZoomSelectorHiddenMilliseconds = 260f;
    public const float ZoomSelectorFadeMilliseconds = 140f;

    private static readonly float[] SheetSpring = StageCurves.SampleSpring(600, 32);
    private static readonly float[] TileSpring = StageCurves.SampleSpring(576, 38.8);

    /// <summary>The focus lift's curve, cubic-bezier(.34, 1.42, .64, 1).</summary>
    public static float Lift(float t) => StageCurves.CubicBezier(0.34f, 1.42f, 0.64f, 1f, Math.Clamp(t, 0f, 1f));

    /// <summary>The zoom's ease, cubic-bezier(.4, 0, .2, 1).</summary>
    public static float ZoomEase(float t) => StageCurves.CubicBezier(0.4f, 0f, 0.2f, 1f, Math.Clamp(t, 0f, 1f));

    public static float CubicOut(float t)
    {
        var u = 1f - Math.Clamp(t, 0f, 1f);
        return 1f - (u * u * u);
    }

    /// <summary>The folder page's springy arrival (600/32), played over its duration: 0 → 1 with ≈ 6% over.</summary>
    public static float Sheet(float t) => Sample(SheetSpring, t);

    /// <summary>The home tile's return (576/38.8), played over its duration.</summary>
    public static float TileReturn(float t) => Sample(TileSpring, t);

    /// <summary>The glow's breathing after a focus change: 1 → 0.55 → 1 … for 8 half cycles, then 1.</summary>
    public static float Breath(float sinceFocus)
    {
        if (sinceFocus < 0 || sinceFocus >= BreathHalfCycleMilliseconds * BreathHalfCycles)
            return 1f;
        var half = (int)(sinceFocus / BreathHalfCycleMilliseconds);
        var f = (sinceFocus % BreathHalfCycleMilliseconds) / BreathHalfCycleMilliseconds;
        var eased = 0.5f - (0.5f * MathF.Cos(MathF.PI * f));
        return half % 2 == 0 ? 1f - ((1f - BreathLow) * eased) : BreathLow + ((1f - BreathLow) * eased);
    }

    /// <summary>The bump: out to the wall fast (the first 35%), then eased back.</summary>
    public static float Bump(float progress) => progress < 0.35f
        ? MathF.Sin(progress / 0.35f * MathF.PI / 2)
        : MathF.Pow(MathF.Cos((progress - 0.35f) / 0.65f * MathF.PI / 2), 2);

    private static float Sample(float[] samples, float t)
    {
        if (t <= 0)
            return 0;
        if (t >= 1)
            return 1;
        var position = t * (samples.Length - 1);
        var index = Math.Min((int)position, samples.Length - 2);
        var fraction = position - index;
        return samples[index] + ((samples[index + 1] - samples[index]) * fraction);
    }
}

/// <summary>A one-dimensional spring that keeps its speed when retargeted (fixed 240 Hz substeps).</summary>
public sealed class SceneSpring(double stiffness, double damping, double restDistance = 0.05, double restSpeed = 0.5)
{
    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; private set; }
    public bool IsSettled => Value == Target && Velocity == 0;

    public void Retarget(double target) => Target = target;

    public void Snap(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }

    /// <summary>Keeps the value within <paramref name="distance"/> of the target, keeping the speed.</summary>
    public void Limit(double distance)
    {
        if (Value - Target > distance)
            Value = Target + distance;
        else if (Target - Value > distance)
            Value = Target - distance;
    }

    public bool Step(double seconds)
    {
        if (IsSettled || seconds <= 0)
            return false;
        var remaining = Math.Min(seconds, 0.05);
        while (remaining > 0)
        {
            var h = Math.Min(1d / 240d, remaining);
            var acceleration = (-stiffness * (Value - Target)) - (damping * Velocity);
            Velocity += acceleration * h;
            Value += Velocity * h;
            remaining -= h;
        }
        if (Math.Abs(Target - Value) < restDistance && Math.Abs(Velocity) < restSpeed)
            Snap(Target);
        return true;
    }
}
