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
