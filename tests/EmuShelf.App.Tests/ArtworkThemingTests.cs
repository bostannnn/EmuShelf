using EmuShelf.App.Services;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Tests;

public class ArtworkThemingTests
{
    private const double TextReadabilityFloor = 4.5;

    [Fact]
    public void ContrastRatio_BlackOnWhite_IsMaximum()
    {
        var ratio = ArtworkColor.ContrastRatio(new Rgb(0, 0, 0), new Rgb(255, 255, 255));
        Assert.True(ratio > 20.9 && ratio <= 21.01, $"Expected ~21, got {ratio}.");
    }

    [Fact]
    public void HslRoundTrip_PreservesColourApproximately()
    {
        var original = new Rgb(214, 92, 158);
        var (h, s, l) = ArtworkColor.ToHsl(original);
        var restored = ArtworkColor.FromHsl(h, s, l);
        Assert.True(Math.Abs(restored.R - original.R) <= 2);
        Assert.True(Math.Abs(restored.G - original.G) <= 2);
        Assert.True(Math.Abs(restored.B - original.B) <= 2);
    }

    [Fact]
    public void Create_DarkArtwork_ProducesDarkPaletteWithReadableText()
    {
        var palette = ArtworkPaletteFactory.Create(new Rgb(60, 90, 220), averageLuminance: 0.12);

        Assert.True(palette.IsDark);
        AssertTextIsReadable(palette);
    }

    [Fact]
    public void Create_BrightArtwork_ProducesLightPaletteWithReadableText()
    {
        var palette = ArtworkPaletteFactory.Create(new Rgb(230, 90, 150), averageLuminance: 0.82);

        Assert.False(palette.IsDark);
        AssertTextIsReadable(palette);
    }

    [Fact]
    public void Create_AccentContrastsWithItsOwnText()
    {
        // Every extreme hue must still let the on-accent text (the Play glyph, selection label) read.
        for (var hue = 0; hue < 360; hue += 30)
        {
            var vibrant = ArtworkColor.FromHsl(hue, 0.9, 0.5);
            var palette = ArtworkPaletteFactory.Create(vibrant, averageLuminance: 0.3);
            var accent = Parse(palette.Accent);
            var accentText = Parse(palette.AccentText);
            var ratio = ArtworkColor.ContrastRatio(accent, accentText);
            Assert.True(ratio >= 3.0, $"Hue {hue}: accent/text contrast only {ratio:F2}.");
        }
    }

    [Fact]
    public void Create_MidBrightness_KeepsPreviousDarkLightDecision()
    {
        // Hysteresis: a mid-luminance cover must not flip the shell away from the last decision.
        var stayingDark = ArtworkPaletteFactory.Create(new Rgb(120, 120, 220), 0.5, previousIsDark: true);
        var stayingLight = ArtworkPaletteFactory.Create(new Rgb(120, 120, 220), 0.5, previousIsDark: false);

        Assert.True(stayingDark.IsDark);
        Assert.False(stayingLight.IsDark);
    }

    [Fact]
    public void FromBgraPixels_SolidRed_YieldsRedAccent()
    {
        var pixels = SolidBgra(0, 0, 255, count: 256);
        var palette = ArtworkPaletteExtractor.FromBgraPixels(pixels, previousIsDark: null);

        Assert.NotNull(palette);
        var accent = Parse(palette!.Accent);
        Assert.True(accent.R > accent.G && accent.R > accent.B, $"Accent {palette.Accent} is not red-dominant.");
    }

    [Fact]
    public void FromBgraPixels_Grayscale_ReturnsNullSoTheThemeIsKept()
    {
        var pixels = SolidBgra(128, 128, 128, count: 256);
        Assert.Null(ArtworkPaletteExtractor.FromBgraPixels(pixels, previousIsDark: null));
    }

    [Fact]
    public void FromBgraPixels_FullyTransparent_ReturnsNull()
    {
        var pixels = new byte[256 * 4]; // all zero, alpha 0
        Assert.Null(ArtworkPaletteExtractor.FromBgraPixels(pixels, previousIsDark: null));
    }

    private static void AssertTextIsReadable(ArtworkPalette palette)
    {
        var background = Parse(palette.Background);
        var text = Parse(palette.TextPrimary);
        var ratio = ArtworkColor.ContrastRatio(text, background);
        Assert.True(ratio >= TextReadabilityFloor, $"Body text contrast only {ratio:F2} on {palette.Background}.");
    }

    private static byte[] SolidBgra(byte b, byte g, byte r, int count)
    {
        var buffer = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            buffer[i * 4] = b;
            buffer[i * 4 + 1] = g;
            buffer[i * 4 + 2] = r;
            buffer[i * 4 + 3] = 255;
        }

        return buffer;
    }

    private static Rgb Parse(string hex)
    {
        var value = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new Rgb((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
    }
}
