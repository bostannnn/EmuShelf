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
        var first = CartridgeLabelPlaceholder.Warm("snes", "Super Nintendo", Colors.BlueViolet, null);
        var second = CartridgeLabelPlaceholder.Warm("snes", "Super Nintendo", Colors.BlueViolet, null);

        // The label depends on the platform, never the game, so a 500-game shelf draws it once.
        Assert.Same(first, second);
        Assert.Same(first, CartridgeLabelPlaceholder.TryGet("snes"));
    }

    /// <summary>
    /// The label panel is landscape and ArtFit.Cover crops whatever does not match it, so a
    /// placeholder authored at the wrong shape would arrive with its own text cut off.
    /// </summary>
    [AvaloniaFact]
    public void Warm_MatchesTheCartridgeLabelsAspect()
    {
        var label = CartridgeLabelPlaceholder.Warm("gba", "Game Boy Advance", Colors.SlateGray, null);

        var aspect = label.PixelSize.Width / (double)label.PixelSize.Height;
        Assert.InRange(aspect, 2.8, 3.05);
    }
}
