using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EmuShelf.App.Rendering;

namespace EmuShelf.App.Tests;

public class CartridgeLabelPlaceholderTests
{
    [AvaloniaFact]
    public void TryGet_ReturnsNothingUntilWarmed() =>
        Assert.Null(CartridgeLabelPlaceholder.TryGet("a-system-nobody-warmed"));

    [AvaloniaFact]
    public void Warm_DrawsOneLabelPerSystemAndReusesIt()
    {
        var first = CartridgeLabelPlaceholder.Warm(
            "snes", "Super Nintendo", Colors.BlueViolet, null, 2.93f);
        var second = CartridgeLabelPlaceholder.Warm(
            "snes", "Super Nintendo", Colors.BlueViolet, null, 2.93f);

        // The label depends on the platform, never the game, so a 500-game shelf draws it once.
        Assert.Same(first, second);
        Assert.Same(first, CartridgeLabelPlaceholder.TryGet("snes"));
    }

    /// <summary>
    /// The label is drawn at whatever shape the shell's panel is.
    /// </summary>
    /// <remarks>
    /// Regression test. This was fixed at the SNES panel's 2.93:1, and ArtFit.Cover crops anything
    /// that does not match — so a portrait NES cartridge showed "TWORK MI" over half a medallion,
    /// and every shell that was not SNES lost part of its own placeholder.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("wide", 2.93f)]
    [InlineData("square", 1.02f)]
    [InlineData("portrait", 0.63f)]
    public void Warm_MatchesThePanelsAspect(string systemId, float aspect)
    {
        var label = CartridgeLabelPlaceholder.Warm(
            systemId, "A System", Colors.SlateGray, null, aspect);

        var drawn = label.PixelSize.Width / (float)label.PixelSize.Height;
        Assert.Equal(aspect, drawn, 1);
    }

    /// <summary>An absurd panel cannot produce an absurd bitmap.</summary>
    [AvaloniaFact]
    public void Warm_ClampsRidiculousAspects()
    {
        var sliver = CartridgeLabelPlaceholder.Warm(
            "sliver", "A System", Colors.SlateGray, null, 40f);

        Assert.InRange(sliver.PixelSize.Width / (float)sliver.PixelSize.Height, 1f, 4.01f);
    }
}
