using System.Numerics;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>The native Stage's motion matches the Avalonia Stage's numbers (M45 H4, option 5).</summary>
public class StageCompositorMathTests
{
    private static readonly StagePixels Pixel = new(1, 1, [0, 0, 0, 0]);

    [Fact]
    public void CentrePop_StartsAt72Percent_OvershootsALittle_AndRests()
    {
        Assert.Equal(StageCurves.PopFromScale, StageCurves.PopScale(0), 3);
        var peak = 0f;
        for (var t = 0f; t <= StageCurves.PopMilliseconds; t += 4)
            peak = Math.Max(peak, StageCurves.PopScale(t));
        Assert.InRange(peak, 1.01f, 1.05f);
        Assert.Equal(1f, StageCurves.PopScale(StageCurves.PopMilliseconds + 1));
        // The same spring as the Avalonia Stage's pop.
        var avalonia = new HandheldSpringEasing(HandheldMotion.Pop, HandheldMotion.PopFromScale, 1);
        Assert.InRange(StageCurves.PopMilliseconds, avalonia.Duration.TotalMilliseconds - 10, avalonia.Duration.TotalMilliseconds + 10);
    }

    [Fact]
    public void PlatePop_Runs240Milliseconds_FromNinetyFourPercent()
    {
        Assert.Equal(0.94f, StageCurves.PlateScale(0), 3);
        Assert.True(StageCurves.PlateScale(120) > 0.98f);
        Assert.Equal(1f, StageCurves.PlateScale(240));
    }

    [Fact]
    public void BackdropFade_EasesOutOver450Milliseconds()
    {
        Assert.Equal(0f, StageCurves.FadeIn(0));
        Assert.True(StageCurves.FadeIn(225) > 0.8f);
        Assert.Equal(1f, StageCurves.FadeIn(450));
    }

    [Fact]
    public void Drift_MovesInWholePixels_OnlyOnSteps_AndLoops()
    {
        var image = new StageImage(Pixel, 0, 0, 1920, 1080);
        var backdrop = new StageBackdrop(image, null, Pixel, null, new Vector2(176, 132), 22000, 250);

        Assert.Equal(Vector2.Zero, StageCompositor.DriftOffset(backdrop, 0));
        // Held between steps.
        Assert.Equal(StageCompositor.DriftOffset(backdrop, 250), StageCompositor.DriftOffset(backdrop, 499));
        var mid = StageCompositor.DriftOffset(backdrop, 11000);
        Assert.Equal(new Vector2(88, 66), mid);
        Assert.Equal(MathF.Round(mid.X), mid.X);
        // One loop later it is back where it started.
        Assert.Equal(Vector2.Zero, StageCompositor.DriftOffset(backdrop, 22000));
    }
}

/// <summary>The native Launcher moves by the same numbers as the Avalonia Launcher.</summary>
public class LauncherMotionNumbersTests
{
    [Fact]
    public void NativeNumbers_MatchTheAvaloniaLaunchers()
    {
        Assert.Equal(LauncherMotion.SelectorStiffness, HandheldMotion.Selector.Stiffness);
        Assert.Equal(LauncherMotion.SelectorDamping, HandheldMotion.Selector.Damping);
        Assert.Equal(LauncherMotion.TrackStiffness, HandheldMotion.Track.Stiffness);
        Assert.Equal(LauncherMotion.TrackDamping, HandheldMotion.Track.Damping);
        Assert.Equal(LauncherMotion.LiftScale, HandheldMotion.LiftScale, 5);
        Assert.Equal(LauncherMotion.LiftMilliseconds, HandheldMotion.LiftMilliseconds);
        Assert.Equal(LauncherMotion.BreathHalfCycleMilliseconds, HandheldMotion.BreathHalfCycleMilliseconds);
        Assert.Equal(LauncherMotion.BreathHalfCycles, HandheldMotion.BreathHalfCycles);
        Assert.Equal(LauncherMotion.BumpFraction, HandheldMotion.BumpFraction, 5);
        Assert.Equal(LauncherMotion.ZoomTileScale, HandheldMotion.ZoomTileScale, 5);
        Assert.Equal(LauncherMotion.ZoomSheetFromScale, HandheldMotion.ZoomSheetFromScale, 5);
        Assert.Equal(LauncherMotion.ZoomSheetInMilliseconds, HandheldMotion.ZoomSheetInMilliseconds);
        Assert.Equal(LauncherMotion.ZoomSheetOutMilliseconds, HandheldMotion.ZoomSheetOutMilliseconds);
        Assert.Equal(LauncherMotion.ZoomTileInMilliseconds, HandheldMotion.ZoomTileInMilliseconds);
        Assert.Equal(LauncherMotion.ZoomSelectorHiddenMilliseconds, HandheldMotion.ZoomSelectorHiddenMilliseconds);
    }

    [Fact]
    public void Breathing_StartsAndEndsAtFullStrength()
    {
        Assert.Equal(1f, LauncherMotion.Breath(0), 3);
        Assert.Equal(LauncherMotion.BreathLow, LauncherMotion.Breath(600), 3);
        Assert.Equal(1f, LauncherMotion.Breath(4800), 3);
        Assert.Equal(1f, LauncherMotion.Breath(10000), 3);
    }

    [Fact]
    public void Breathing_StepsAt30Hz_OnAGridThatHitsEveryTurn()
    {
        // A half cycle is a whole number of 30 Hz steps, so the stepped breath reaches its exact low and high.
        var steps = LauncherMotion.BreathHalfCycleMilliseconds / LauncherMotion.BreathStepMilliseconds;
        Assert.Equal(MathF.Round(steps), steps, 3);
        Assert.Equal(1000f / 30f, LauncherMotion.BreathStepMilliseconds, 3);
    }

    [Fact]
    public void TrackSpring_CanBeHeldToOneColumnOfLag()
    {
        var spring = new SceneSpring(LauncherMotion.TrackStiffness, LauncherMotion.TrackDamping);
        spring.Snap(0);
        spring.Retarget(-264 * 3);
        spring.Limit(264);
        Assert.Equal(-264 * 2, spring.Value, 3);
    }
}
