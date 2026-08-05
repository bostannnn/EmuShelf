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
}
