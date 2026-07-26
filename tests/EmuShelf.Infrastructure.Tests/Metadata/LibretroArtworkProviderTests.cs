using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public sealed class LibretroArtworkProviderTests
{
    [Fact]
    public void GetCandidates_UsesTheVerifiedCatalogTitle()
    {
        var candidates = Provider().GetCandidates([], Match(
            "Lumines II (Europe) (En,Fr,De,Es,It)"));

        var candidate = Assert.Single(candidates);
        Assert.Equal(
            "Lumines II (Europe) (En,Fr,De,Es,It).png",
            Filename(candidate));
    }

    [Fact]
    public void GetCandidates_FilenameFallbackKeepsOnlyTheLiteralFilename()
    {
        var candidates = Provider().GetCandidates(
            [],
            new GameCatalogMatch(
                "filename-fallback",
                "Lumines - Puzzle Fusion",
                "Lumines - Puzzle Fusion (Europe) (En,Fr,De,Es,It)",
                null));

        Assert.Single(candidates);
        Assert.Equal(
            "Lumines - Puzzle Fusion (Europe) (En,Fr,De,Es,It).png",
            Filename(candidates[0]));
    }

    [Fact]
    public void GetIndexedTitleQueries_UsesReviewedAliasesOnlyForPspCatalogMatches()
    {
        var aliases = Provider().GetIndexedTitleQueries(Match("Metal Gear Acid"));
        var personaAlias = Provider().GetIndexedTitleQueries(Match("Persona 2 - Batsu - Eternal Punishment"));
        var otherPlatform = new LibretroArtworkProvider("Nintendo - Nintendo DS")
            .GetIndexedTitleQueries(Match("Metal Gear Acid"));
        var filenameFallback = Provider().GetIndexedTitleQueries(new GameCatalogMatch(
            "filename-fallback", "Metal Gear Acid", "Metal Gear Acid", null));

        Assert.Equal(["Metal Gear Acid", "Metal Gear Ac!d"], aliases);
        Assert.Equal(["Persona 2 - Batsu - Eternal Punishment", "Persona 2 - Batsu"], personaAlias);
        Assert.Equal(["Metal Gear Acid"], otherPlatform);
        Assert.Equal(["Metal Gear Acid"], filenameFallback);
    }

    // A catalogue title always carries its region and language tags, so an alias table keyed by
    // product title has to drop them before the lookup. Testing only the bare product title hid
    // that the aliases never fired against a real match.
    [Theory]
    [InlineData("Persona 2 - Batsu - Eternal Punishment (Japan)", "Persona 2 - Batsu")]
    [InlineData("Metal Gear Acid (Europe) (En,Fr,De,Es,It)", "Metal Gear Ac!d")]
    [InlineData("Lumines - Puzzle Fusion (Europe) (En,Fr,De,Es,It)", "Lumines")]
    public void GetIndexedTitleQueries_AppliesAnAliasToATaggedCatalogTitle(
        string canonicalTitle,
        string expectedAlias)
    {
        var queries = Provider().GetIndexedTitleQueries(Match(canonicalTitle));

        Assert.Equal([canonicalTitle, expectedAlias], queries);
    }

    private static LibretroArtworkProvider Provider() =>
        new("Sony - PlayStation Portable");

    private static GameCatalogMatch Match(string title) =>
        new("libretro-database", "exact-key", title, "Europe");

    private static string Filename(ArtworkCandidate candidate) =>
        Uri.UnescapeDataString(candidate.SourceUri.Segments[^1]);
}
