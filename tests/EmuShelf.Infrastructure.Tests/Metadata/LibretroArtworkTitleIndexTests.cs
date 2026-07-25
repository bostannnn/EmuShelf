using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public sealed class LibretroArtworkTitleIndexTests
{
    [Fact]
    public void Parse_DecodesDirectoryNamesAndIgnoresNonPngLinks()
    {
        var entries = LibretroArtworkTitleIndex.Parse("""
            <a href="Dissidia%20-%20Final%20Fantasy%20%28Europe%29.png">cover</a>
            <a href="Readme.txt">readme</a>
            """);

        var entry = Assert.Single(entries);
        Assert.Equal("Dissidia - Final Fantasy (Europe)", entry.FilenameWithoutExtension);
    }

    [Fact]
    public void FindMatches_NormalizesPunctuationAndReleaseTags()
    {
        var entries = LibretroArtworkTitleIndex.Parse(
            "<a href=\"Dissidia%20-%20Final%20Fantasy%20%28Europe%29%20%28En%2CFr%2CDe%2CEs%2CIt%29.png\">cover</a>");

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From("Dissidia Final Fantasy (USA)"));

        Assert.Equal("Dissidia - Final Fantasy (Europe) (En,Fr,De,Es,It)", Assert.Single(matches).FilenameWithoutExtension);
    }

    [Fact]
    public void FindMatches_PrefersTheCataloguedRegionBeforeOtherRegionalScans()
    {
        var entries = LibretroArtworkTitleIndex.Parse("""
            <a href="Crazy%20Taxi%20%28Europe%29.png">cover</a>
            <a href="Crazy%20Taxi%20%28Japan%29.png">cover</a>
            <a href="Crazy%20Taxi%20%28USA%29.png">cover</a>
            """);

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From("Crazy Taxi (USA)"),
            "USA");

        Assert.Equal("Crazy Taxi (USA)", matches[0].FilenameWithoutExtension);
    }

    [Fact]
    public void FindMatches_UsesTheTitleRegionWhenTheCatalogRegionIsUnrecognised()
    {
        var entries = LibretroArtworkTitleIndex.Parse("""
            <a href="Crazy%20Taxi%20%28Europe%29.png">cover</a>
            <a href="Crazy%20Taxi%20%28USA%29.png">cover</a>
            """);

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From("Crazy Taxi (USA)"),
            "United States");

        Assert.Equal("Crazy Taxi (USA)", matches[0].FilenameWithoutExtension);
    }

    [Fact]
    public void FindMatches_DoesNotMatchAProductTitlePrefix()
    {
        var entries = LibretroArtworkTitleIndex.Parse(
            "<a href=\"Persona%202%20-%20Batsu%20%28Japan%29.png\">cover</a>");

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From("Persona 2 - Batsu - Eternal Punishment (Japan)"));

        Assert.Empty(matches);
    }

}
