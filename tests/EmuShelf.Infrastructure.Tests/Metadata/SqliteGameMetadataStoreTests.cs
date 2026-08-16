using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Importing;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class SqliteGameMetadataStoreTests : TempAppDirectoryTestBase
{
    private readonly LibraryDatabase _database;
    private readonly GameLibrary _library;
    private readonly SqliteGameMetadataStore _metadata;

    public SqliteGameMetadataStoreTests()
    {
        AppPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(AppPaths);
        _database.Initialize();
        var resolver = new RelativePathResolver(AppPaths);
        _library = new GameLibrary(_database, resolver);
        _metadata = new SqliteGameMetadataStore(_database, resolver);
    }

    [Fact]
    public void CatalogTitle_UpdatesFilenameTitle_ButNeverUserTitle()
    {
        var game = AddGame("Original.iso", GameTitleOrigin.Filename);

        Assert.True(_metadata.TryApplyCatalogTitle(game.Id, "Catalog Title", "Original"));
        Assert.Equal(GameTitleOrigin.Catalog, _metadata.GetGame(game.Id)!.TitleOrigin);

        _library.UpdateTitle(game.Id, "My Custom Title");
        Assert.False(_metadata.TryApplyCatalogTitle(game.Id, "New Catalog Title", "Original"));
        Assert.Equal("My Custom Title", _metadata.GetGame(game.Id)!.Title);
        Assert.Equal(GameTitleOrigin.User, _metadata.GetGame(game.Id)!.TitleOrigin);
    }

    [Fact]
    public void CatalogTitle_UpdatesEmbeddedTitle()
    {
        var game = AddGame("Original.iso", GameTitleOrigin.Embedded);

        Assert.True(_metadata.TryApplyCatalogTitle(game.Id, "Catalog Title", "Original"));
        var updated = _metadata.GetGame(game.Id)!;
        Assert.Equal("Catalog Title", updated.Title);
        Assert.Equal(GameTitleOrigin.Catalog, updated.TitleOrigin);
    }

    [Fact]
    public void DownloadedCover_IsPortable_AndCannotReplaceManualCover()
    {
        var game = AddGame("Cover.iso", GameTitleOrigin.Filename);
        var downloaded = Path.Combine(AppPaths.CoversDirectory, "downloaded.jpg");
        var manual = Path.Combine(AppPaths.CoversDirectory, "manual.png");

        Assert.True(_metadata.TryApplyDownloadedCover(
            game.Id,
            downloaded,
            "provider",
            "https://example.test/cover.jpg"));
        Assert.Equal(GameCoverOrigin.Downloaded, _metadata.GetGame(game.Id)!.CoverOrigin);

        _library.UpdateCoverPath(game.Id, manual);
        Assert.False(_metadata.TryApplyDownloadedCover(
            game.Id,
            downloaded,
            "provider",
            "https://example.test/new.jpg"));
        Assert.Equal(manual, _metadata.GetGame(game.Id)!.CoverPath);
        Assert.Equal(GameCoverOrigin.User, _metadata.GetGame(game.Id)!.CoverOrigin);
    }

    [Fact]
    public void DownloadedCover_ReplacesScannedCover_WhichIsNotUserOwned()
    {
        // Art discovered during scanning keeps origin None; a scrape must be able to replace it.
        var game = AddGameWithCover("Scanned.iso", "scanned-boxart.png");
        Assert.Equal(GameCoverOrigin.None, _metadata.GetGame(game.Id)!.CoverOrigin);

        var downloaded = Path.Combine(AppPaths.CoversDirectory, "downloaded.jpg");
        Assert.True(_metadata.TryApplyDownloadedCover(
            game.Id,
            downloaded,
            "provider",
            "https://example.test/cover.jpg"));

        Assert.Equal(downloaded, _metadata.GetGame(game.Id)!.CoverPath);
        Assert.Equal(GameCoverOrigin.Downloaded, _metadata.GetGame(game.Id)!.CoverOrigin);
    }

    [Fact]
    public void DownloadedCover_OverwritesManualCover_WhenExplicitlyRequested()
    {
        // The single-game scraper's ticked row asks to override even a hand-picked cover.
        var game = AddGame("Override.iso", GameTitleOrigin.Filename);
        var manual = Path.Combine(AppPaths.CoversDirectory, "manual.png");
        var downloaded = Path.Combine(AppPaths.CoversDirectory, "downloaded.jpg");
        _library.UpdateCoverPath(game.Id, manual);

        Assert.True(_metadata.TryApplyDownloadedCover(
            game.Id,
            downloaded,
            "provider",
            "https://example.test/cover.jpg",
            overwriteUserCover: true));

        Assert.Equal(downloaded, _metadata.GetGame(game.Id)!.CoverPath);
        Assert.Equal(GameCoverOrigin.Downloaded, _metadata.GetGame(game.Id)!.CoverOrigin);
    }

    [Fact]
    public void GetIdentifiers_RoundTripsStoredEvidence_PrimaryFirst()
    {
        var game = AddGame("Serialized.iso", GameTitleOrigin.Filename);
        _metadata.ReplaceIdentifiers(game.Id,
        [
            new GameIdentifier(GameIdentifierKind.Serial, "SLUS-20265", "DiscContent", false),
            new GameIdentifier(GameIdentifierKind.Serial, "SLUS-20264", "DiscContent", true),
        ]);

        var identifiers = _metadata.GetIdentifiers(game.Id);

        Assert.Equal(2, identifiers.Count);
        Assert.True(identifiers[0].IsPrimary);
        Assert.Equal("SLUS-20264", identifiers[0].Value);
        Assert.Equal("DiscContent", identifiers[0].Source);
        Assert.Equal(GameIdentifierKind.Serial, identifiers[0].Kind);
        Assert.Empty(_metadata.GetIdentifiers(game.Id + 1));
    }

    [Fact]
    public void IdentifiersAndMetadata_CascadeWhenLibraryRowIsRemoved()
    {
        var game = AddGame("Cascade.iso", GameTitleOrigin.Filename);
        _metadata.ReplaceIdentifiers(game.Id,
        [
            new GameIdentifier(GameIdentifierKind.Serial, "SLUS-20265", "DiscContent", true),
        ]);
        _metadata.RecordAttempt(new GameMetadataAttempt(
            game.Id,
            GameMetadataStatus.Matched,
            new GameCatalogMatch("catalog", "SLUS-20265", "Title", "USA"),
            null,
            null,
            null,
            DateTimeOffset.UtcNow));

        _library.RemoveGame(game.Id);

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT COUNT(*) FROM GameIdentifiers) + " +
            "(SELECT COUNT(*) FROM GameMetadata);";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void MissingMetadataQuery_ExcludesFullyEnrichedGame()
    {
        var game = AddGame("Done.iso", GameTitleOrigin.Filename);
        Assert.Single(_metadata.GetGamesMissingMetadata());

        _metadata.TryApplyCatalogTitle(game.Id, "Done (USA)", "Done");
        _metadata.TryApplyDownloadedCover(
            game.Id,
            Path.Combine(AppPaths.CoversDirectory, "done.png"),
            "provider",
            "https://example.test/done.png");

        Assert.Empty(_metadata.GetGamesMissingMetadata());
    }

    [Fact]
    public void MissingMetadataQuery_IncludesEmbeddedTitleWithKnownCatalogTitleUntilRepaired()
    {
        var game = AddGame("InternalId.iso", GameTitleOrigin.Embedded);
        Assert.True(_metadata.TryApplyDownloadedCover(
            game.Id,
            Path.Combine(AppPaths.CoversDirectory, "internal-id.png"),
            "provider",
            "https://example.test/internal-id.png"));
        _metadata.RecordAttempt(new GameMetadataAttempt(
            game.Id,
            GameMetadataStatus.Matched,
            new GameCatalogMatch("catalog", "entry", "Catalog Title", null),
            "provider",
            "https://example.test/internal-id.png",
            null,
            DateTimeOffset.UtcNow));

        Assert.Single(_metadata.GetGamesMissingMetadata());

        Assert.True(_metadata.TryApplyCatalogTitle(game.Id, "Catalog Title", "InternalId"));
        Assert.Empty(_metadata.GetGamesMissingMetadata());
    }

    [Fact]
    public void MetadataReads_PreserveExternalLibraryEvidence()
    {
        var path = Path.Combine(BaseDirectory, "Games", "Demon's Souls");
        var source = new ExternalLibrarySource(
            "rpcs3-library",
            "playstation3",
            "RPCS3 library",
            Path.Combine(BaseDirectory, "RPCS3"));
        _library.ReconcileExternalLibrary(
            source,
        [
            new ExternalLibraryGameEntry(
                "BLUS30443",
                path,
                "Demon's Souls",
                IsAvailable: true,
                GameTitleOrigin.Embedded),
        ]);
        var game = _library.GetGames("playstation3").Single();

        var metadataGame = _metadata.GetGame(game.Id);

        Assert.NotNull(metadataGame);
        Assert.Equal("rpcs3-library", metadataGame.ExternalSourceId);
        Assert.Equal("BLUS30443", metadataGame.ExternalSourceEntryId);
        Assert.True(metadataGame.IsPresentInExternalSource);
    }

    [Fact]
    public void GetProviderTitles_ReturnsScrapedTitles_PreferringTheNeutralLocale()
    {
        var scraped = AddGame("Pokemon - FireRed Version (USA, Europe) (Rev 1).gba", GameTitleOrigin.Filename);
        var unscraped = AddGame("Some Other Game.iso", GameTitleOrigin.Filename);

        var details = new SqliteGameDetailsStore(_database, new RelativePathResolver(AppPaths));
        // A localized name and the neutral (empty-locale) canonical name; the neutral one wins.
        Assert.True(details.TryApplyMetadata(
            TitleValue(scraped.Id, "Pokemon FireRed (Europe)", locale: "en"),
            GameMetadataApplyMode.FillMissing));
        Assert.True(details.TryApplyMetadata(
            TitleValue(scraped.Id, "Pokémon FireRed", locale: null),
            GameMetadataApplyMode.FillMissing));

        var titles = _metadata.GetProviderTitles();

        Assert.Equal("Pokémon FireRed", titles[scraped.Id]);
        Assert.False(titles.ContainsKey(unscraped.Id));
    }

    // Rows named after disc 1 because their whole set shares one product number look complete, so a
    // fetch has to ask for them by name once the catalogue can tell the discs apart.
    [Fact]
    public void MismatchedDiscTitles_AreOnlyTheRowsNamingAnotherDisc()
    {
        var wrong = AddGame("Shenmue (Europe) (Disc 2).chd", GameTitleOrigin.Filename);
        var right = AddGame("Shenmue (Europe) (Disc 3).chd", GameTitleOrigin.Filename);
        var wholeSet = AddGame("Twin Snakes (USA) (Disc 2).rvz", GameTitleOrigin.Filename);
        var renamed = AddGame("Xenogears (USA) (Disc 2).chd", GameTitleOrigin.Filename);

        Assert.True(_metadata.TryApplyCatalogTitle(wrong.Id, "Shenmue (Europe) (Disc 1)", wrong.Title));
        Assert.True(_metadata.TryApplyCatalogTitle(right.Id, "Shenmue (Europe) (Disc 3)", right.Title));
        // A DAT that holds one entry for the whole set is not evidence of a wrong disc.
        Assert.True(_metadata.TryApplyCatalogTitle(wholeSet.Id, "Twin Snakes (USA)", wholeSet.Title));
        _library.UpdateTitle(renamed.Id, "Xenogears (USA) (Disc 1)");

        var mismatched = _metadata.GetGamesWithMismatchedDiscTitles();

        // The user's own rename is left alone; a catalogue fetch could not overwrite it anyway.
        Assert.Equal([wrong.Id], mismatched.Select(game => game.Id));
        Assert.Empty(_metadata.GetGamesWithMismatchedDiscTitles("dreamcast"));
    }

    private static GameMetadataValue TitleValue(long gameId, string value, string? locale) =>
        new(
            gameId,
            GameMetadataField.Title,
            value,
            locale,
            GameMetadataValueOrigin.Provider,
            "screenscraper",
            "provider-game-id",
            null,
            DateTimeOffset.UtcNow);

    private Game AddGame(string filename, GameTitleOrigin origin)
    {
        var path = Path.Combine(BaseDirectory, "Games", filename);
        _library.AddGames([
            new Game
            {
                SystemId = "playstation2",
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                TitleOrigin = origin,
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        return _library.GetGames().Single(game => game.Path == path);
    }

    private Game AddGameWithCover(string filename, string coverFilename)
    {
        var path = Path.Combine(BaseDirectory, "Games", filename);
        var coverPath = Path.Combine(AppPaths.CoversDirectory, coverFilename);
        _library.AddGames([
            new Game
            {
                SystemId = "playstation2",
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                TitleOrigin = GameTitleOrigin.Filename,
                CoverPath = coverPath,
                CoverOrigin = GameCoverOrigin.None,
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        return _library.GetGames().Single(game => game.Path == path);
    }
}
