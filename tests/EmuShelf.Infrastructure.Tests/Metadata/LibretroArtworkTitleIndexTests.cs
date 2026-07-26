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

    [Theory]
    [InlineData("Rhythm Heaven (U)(Undub)(RH2Y)")]
    [InlineData("Zoo_Keeper_(U)_(UNDUB)")]
    [InlineData("Rhythm Heaven (USA) (En,Fr,Es) (patched)")]
    [InlineData("Rhythm Heaven (Japan) [T-En by Some Team v1.01]")]
    public void FindMatches_ResolvesAModifiedDumpWhoseFilenameKeepsTheRetailTitle(string filename)
    {
        var entries = LibretroArtworkTitleIndex.Parse("""
            <a href="Rhythm%20Heaven%20%28USA%29.png">cover</a>
            <a href="Zoo%20Keeper%20%28USA%29.png">cover</a>
            """);

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From(filename));

        Assert.NotEmpty(matches);
        Assert.StartsWith(
            filename.StartsWith("Zoo", StringComparison.Ordinal) ? "Zoo Keeper" : "Rhythm Heaven",
            matches[0].FilenameWithoutExtension,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindMatches_IgnoresAVersionSuffixCarriedAheadOfTheReleaseTags()
    {
        var entries = LibretroArtworkTitleIndex.Parse(
            "<a href=\"Crazy%20Taxi%20%28USA%29.png\">cover</a>");

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From(
                "Crazy Taxi v1.004 (1999)(Sega)(NTSC)(US)[!][10S 51035]"));

        Assert.Equal("Crazy Taxi (USA)", Assert.Single(matches).FilenameWithoutExtension);
    }

    [Fact]
    public void FindMatches_IgnoresAPublisherPossessiveCarriedByOnlyOneSource()
    {
        var entries = LibretroArtworkTitleIndex.Parse(
            "<a href=\"Disney%27s%20Donald%20Duck%20-%20Goin%27%20Quackers%20%28USA%29.png\">cover</a>");

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From(
                "Donald Duck - Goin' Quackers v1.001 (2000)(Ubi Soft)(NTSC)(US)(M5)[!]"));

        Assert.Equal(
            "Disney's Donald Duck - Goin' Quackers (USA)",
            Assert.Single(matches).FilenameWithoutExtension);
    }

    [Fact]
    public void FindMatches_PossessiveRelaxationDoesNotOutrankAnExactTitle()
    {
        var entries = LibretroArtworkTitleIndex.Parse("""
            <a href="Disney%27s%20Tarzan%20%28Europe%29.png">cover</a>
            <a href="Tarzan%20%28USA%29.png">cover</a>
            """);

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From("Tarzan (USA)"));

        Assert.Equal("Tarzan (USA)", Assert.Single(matches).FilenameWithoutExtension);
    }

    [Fact]
    public void FindMatches_DoesNotMatchAnUnrelatedTitleThroughAPossessive()
    {
        var entries = LibretroArtworkTitleIndex.Parse(
            "<a href=\"Yoshi%27s%20Island%20DS%20%28USA%29.png\">cover</a>");

        var matches = LibretroArtworkTitleIndex.FindMatches(
            entries,
            LibretroArtworkTitleIndex.NormalizedTitle.From("Yoshi Touch & Go (USA)"));

        Assert.Empty(matches);
    }
}
