using EmuShelf.Core.Metadata;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackLibraryMapTests
{
    [Fact]
    public void UsablePack_MatchingAnImportedSerial_IsMatchedAndMarksThatGame()
    {
        var map = Build(
            Snapshot(Usable("SLUS-20946", TexturePackMatchRule.ExactSerial, "SLUS-20946")),
            Library((7, GameIdentifierKind.Serial, "SLUS-20946")));

        var classification = Assert.Single(map.Classifications);
        Assert.Equal(TexturePackEntryStatus.Matched, classification.Status);
        Assert.Equal([7L], classification.MatchedGameIds);

        var match = Assert.Single(map.GetMatches(7));
        Assert.Equal("SLUS-20946", match.PackKey);
        Assert.Equal(1, map.MatchedCount);
    }

    [Fact]
    public void UsablePack_ForAGameThatIsNotImported_IsNoLibraryMatchNotAFailure()
    {
        var map = Build(
            Snapshot(Usable("SLUS-11111", TexturePackMatchRule.ExactSerial, "SLUS-11111")),
            Library((7, GameIdentifierKind.Serial, "SLUS-20946")));

        Assert.Equal(TexturePackEntryStatus.NoLibraryMatch, Assert.Single(map.Classifications).Status);
        Assert.Equal(0, map.MatchedCount);
        Assert.Equal(1, map.NoMatchCount);
        // "No library match" is a normal state, so it must not inflate the attention count.
        Assert.Equal(0, map.AttentionCount);
    }

    [Fact]
    public void UsablePack_WhenTheLibraryHasNoComparableIdentifiers_IsPendingRatherThanUnmatched()
    {
        // Identification has not run yet, so calling this pack unmatched would be a guess.
        var map = Build(
            Snapshot(Usable("SLUS-20946", TexturePackMatchRule.ExactSerial, "SLUS-20946")),
            Library());

        Assert.Equal(TexturePackEntryStatus.IdentifierPending, Assert.Single(map.Classifications).Status);
        Assert.Equal(0, map.NoMatchCount);
    }

    [Fact]
    public void DumpsOnlyAndUnrecognizedFolders_AreAttentionStatesAndNeverMarkAGame()
    {
        var map = Build(
            Snapshot(
                new TexturePackInventoryEntry(
                    "SLUS-20946",
                    "/t/SLUS-20946",
                    TexturePackContentStatus.EmptyOrDumpsOnly,
                    [new TexturePackMatchKey(TexturePackMatchRule.ExactSerial, "SLUS-20946")]),
                new TexturePackInventoryEntry(
                    "notes",
                    "/t/notes",
                    TexturePackContentStatus.UnrecognizedLayout,
                    [])),
            Library((7, GameIdentifierKind.Serial, "SLUS-20946")));

        Assert.Equal(
            [TexturePackEntryStatus.EmptyOrDumpsOnly, TexturePackEntryStatus.UnrecognizedLayout],
            map.Classifications.Select(c => c.Status));
        Assert.Empty(map.GetMatches(7));
        Assert.Equal(2, map.AttentionCount);
    }

    [Fact]
    public void UnavailableRoot_MakesEveryEntryFolderUnavailableWithoutClearingTheCachedList()
    {
        var snapshot = Snapshot(Usable("SLUS-20946", TexturePackMatchRule.ExactSerial, "SLUS-20946"))
            with { RootStatus = TexturePackRootStatus.Missing };

        var map = Build(snapshot, Library((7, GameIdentifierKind.Serial, "SLUS-20946")));

        Assert.Equal(TexturePackEntryStatus.FolderUnavailable, Assert.Single(map.Classifications).Status);
        Assert.Empty(map.GetMatches(7));
    }

    [Fact]
    public void DolphinSharedPack_IsReportedSeparatelyAndNeverAsNoLibraryMatch()
    {
        var map = Build(
            Snapshot(Usable("all.txt", TexturePackMatchRule.DolphinShared, "*")),
            Library((7, GameIdentifierKind.DiscId, "GALE01")));

        Assert.Equal(TexturePackEntryStatus.SharedPack, Assert.Single(map.Classifications).Status);
        Assert.Equal(0, map.NoMatchCount);
        // A shared pack is not keyed on a title, so it must not put a mark on every game.
        Assert.Empty(map.GetMatches(7));
    }

    [Fact]
    public void DolphinThreeCharacterPack_MatchesByPrefixUnlessAnExactFolderClaimsThatDiscId()
    {
        var map = Build(
            Snapshot(
                Usable("GAL", TexturePackMatchRule.DolphinDirectoryPrefix, "GAL"),
                Usable("GALE01", TexturePackMatchRule.DolphinDirectoryExact, "GALE01")),
            Library(
                (7, GameIdentifierKind.DiscId, "GALE01"),
                (8, GameIdentifierKind.DiscId, "GALP01")));

        var prefix = map.Classifications.Single(c => c.PackKey == "GAL");
        var exact = map.Classifications.Single(c => c.PackKey == "GALE01");

        // Dolphin consults the exact directory first, so GALE01 belongs to the exact pack only.
        Assert.Equal([8L], prefix.MatchedGameIds);
        Assert.Equal([7L], exact.MatchedGameIds);
    }

    [Fact]
    public void PspPackKey_MatchesTheHyphenlessFormOfAnImportedDiscId()
    {
        var map = Build(
            Snapshot(Usable("ULUS10041", TexturePackMatchRule.PspGameId, "ULUS10041")),
            Library((7, GameIdentifierKind.Serial, "ULUS-10041")));

        Assert.Equal([7L], Assert.Single(map.Classifications).MatchedGameIds);
    }

    [Fact]
    public void UsablePack_MatchingAnImported3dsTitleId_IsMatchedAndMarksThatGame()
    {
        var map = Build(
            Snapshot(Usable("0004000000033500", TexturePackMatchRule.Nintendo3dsTitleId, "0004000000033500")),
            Library((7, GameIdentifierKind.TitleId, "0004000000033500")));

        var classification = Assert.Single(map.Classifications);
        Assert.Equal(TexturePackEntryStatus.Matched, classification.Status);
        Assert.Equal([7L], classification.MatchedGameIds);
        Assert.Equal("0004000000033500", Assert.Single(map.GetMatches(7)).PackKey);
    }

    [Fact]
    public void Azahar3dsPack_ForATitleNoImportedGameDeclares_IsNoLibraryMatch()
    {
        var map = Build(
            Snapshot(Usable("0004000000033500", TexturePackMatchRule.Nintendo3dsTitleId, "0004000000033500")),
            Library((7, GameIdentifierKind.TitleId, "00040000000ABCDE")));

        Assert.Equal(TexturePackEntryStatus.NoLibraryMatch, Assert.Single(map.Classifications).Status);
    }

    [Fact]
    public void MultiDiscTitle_IsMatchedWhenAnySingleDiscIs()
    {
        // These emulators key a multi-disc pack on one disc's serial, so a title set backed by
        // several library rows is matched when any of its discs is.
        var map = Build(
            Snapshot(Usable("SLUS-00594", TexturePackMatchRule.ExactSerial, "SLUS-00594")),
            Library(
                (7, GameIdentifierKind.Serial, "SLUS-00594"),
                (8, GameIdentifierKind.Serial, "SLUS-00779")));

        Assert.Empty(map.GetMatches(8));
        var match = Assert.Single(map.GetMatches([8L, 7L]));
        Assert.Equal("SLUS-00594", match.PackKey);
    }

    [Fact]
    public void MatchesForASet_AreDeduplicatedWhenSeveralDiscsShareOnePack()
    {
        var map = Build(
            Snapshot(Usable("SLUS-00594", TexturePackMatchRule.ExactSerial, "SLUS-00594")),
            Library(
                (7, GameIdentifierKind.Serial, "SLUS-00594"),
                (8, GameIdentifierKind.Serial, "SLUS-00594")));

        Assert.Single(map.GetMatches([7L, 8L]));
    }

    [Fact]
    public void LastScannedAt_IsTheOldestContributingSnapshotSoStalenessIsNotUnderstated()
    {
        var older = DateTimeOffset.UtcNow.AddHours(-3);
        var map = TexturePackLibraryMap.Build(
            [
                Snapshot() with { ScannedAt = DateTimeOffset.UtcNow },
                Snapshot() with { InstallationId = "other", ScannedAt = older },
            ],
            Library());

        Assert.Equal(older, map.LastScannedAt);
    }

    private static TexturePackLibraryMap Build(
        TexturePackInventorySnapshot snapshot,
        IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> identifiers) =>
        TexturePackLibraryMap.Build([snapshot], identifiers);

    private static TexturePackInventorySnapshot Snapshot(params TexturePackInventoryEntry[] entries) =>
        new(
            "duckstation",
            "duckstation:/textures",
            "/textures",
            DateTimeOffset.UtcNow,
            TexturePackRootStatus.Ready,
            entries);

    private static TexturePackInventoryEntry Usable(
        string packKey,
        TexturePackMatchRule rule,
        string value) =>
        new(
            packKey,
            $"/textures/{packKey}",
            TexturePackContentStatus.Usable,
            [new TexturePackMatchKey(rule, value)]);

    private static IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> Library(
        params (long GameId, GameIdentifierKind Kind, string Value)[] identifiers) =>
        identifiers
            .GroupBy(identifier => identifier.GameId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GameIdentifier>)group
                    .Select(identifier => new GameIdentifier(identifier.Kind, identifier.Value, "test"))
                    .ToArray());
}
