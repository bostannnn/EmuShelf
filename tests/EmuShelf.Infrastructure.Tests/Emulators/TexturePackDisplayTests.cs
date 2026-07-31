using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackDisplayTests
{
    [Fact]
    public void NoMatches_ShowsNoMarkAnEmDashAndAZeroSortKey()
    {
        var display = TexturePackDisplay.For([]);

        Assert.False(display.ShowMark);
        Assert.Equal(TexturePackDisplay.Dash, display.ColumnText);
        Assert.Equal(0, display.SortKey);
    }

    [Fact]
    public void NotScannedAndUnsupported_AreDistinctFromHavingNoPack()
    {
        // Both show an em dash, but their sort key and tooltip must keep "we don't know yet"
        // separate from "we looked and there is nothing".
        Assert.Equal(-1, TexturePackDisplay.NotScanned.SortKey);
        Assert.Equal(-1, TexturePackDisplay.Unsupported.SortKey);
        Assert.NotEqual(TexturePackDisplay.NotScanned.Tooltip, TexturePackDisplay.Unsupported.Tooltip);
        Assert.Equal(0, TexturePackDisplay.For([]).SortKey);
    }

    [Fact]
    public void OneMatch_ShowsTheMarkAndReadsInstalled()
    {
        var display = TexturePackDisplay.For([Match("SLUS-20946")]);

        Assert.True(display.ShowMark);
        Assert.Equal("Installed", display.ColumnText);
        Assert.Equal(1, display.SortKey);
    }

    [Fact]
    public void SeveralMatches_ShowTheCountAndSortAboveASingleMatch()
    {
        var display = TexturePackDisplay.For([Match("SLUS-20946"), Match("SLUS-20946-hd")]);

        Assert.Equal("2 packs", display.ColumnText);
        Assert.True(display.SortKey > TexturePackDisplay.For([Match("SLUS-20946")]).SortKey);
    }

    [Fact]
    public void Tooltip_NamesTheEmulatorTheMatchedIdentifierAndThePackPath()
    {
        var display = TexturePackDisplay.For(
            [Match("SLUS-20946")],
            TexturePackLoadingStatus.Enabled,
            _ => "PCSX2");

        Assert.Contains("PCSX2", display.Tooltip, StringComparison.Ordinal);
        Assert.Contains("SLUS-20946", display.Tooltip, StringComparison.Ordinal);
        Assert.Contains("/textures/SLUS-20946", display.Tooltip, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TexturePackLoadingStatus.Enabled, "enabled")]
    [InlineData(TexturePackLoadingStatus.Disabled, "turned off")]
    [InlineData(TexturePackLoadingStatus.Unknown, "unknown")]
    public void Tooltip_QualifiesTheMarkWithTheLoadingState(
        TexturePackLoadingStatus loading,
        string expected)
    {
        var display = TexturePackDisplay.For([Match("SLUS-20946")], loading);

        Assert.Contains(expected, display.Tooltip, StringComparison.OrdinalIgnoreCase);
        // The mark means installed and matched, so a disabled emulator setting must not hide it.
        Assert.True(display.ShowMark);
    }

    private static TexturePackMatch Match(string packKey) =>
        new("pcsx2", "pcsx2:/textures", packKey, $"/textures/{packKey}", "SLUS-20946");
}
