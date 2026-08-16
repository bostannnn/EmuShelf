using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class SqliteGameDetailsStoreTests : TempAppDirectoryTestBase
{
    private readonly LibraryDatabase _database;
    private readonly GameLibrary _library;
    private readonly SqliteGameDetailsStore _details;

    public SqliteGameDetailsStoreTests()
    {
        AppPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(AppPaths);
        _database.Initialize();
        var resolver = new RelativePathResolver(AppPaths);
        _library = new GameLibrary(_database, resolver);
        _details = new SqliteGameDetailsStore(_database, resolver);
    }

    [Fact]
    public void Metadata_FillsMissing_RefreshesSameProvider_AndProtectsUserEdits()
    {
        var game = AddGame("Provenance.iso");
        var original = ProviderValue(game.Id, "ScreenScraper description", "screenscraper");

        Assert.True(_details.TryApplyMetadata(original, GameMetadataApplyMode.FillMissing));
        Assert.False(_details.TryApplyMetadata(
            ProviderValue(game.Id, "Other description", "other"),
            GameMetadataApplyMode.FillMissing));
        Assert.False(_details.TryApplyMetadata(
            ProviderValue(game.Id, "Other refresh", "other"),
            GameMetadataApplyMode.RefreshProviderOwned));
        Assert.True(_details.TryApplyMetadata(
            ProviderValue(game.Id, "Updated description", "screenscraper"),
            GameMetadataApplyMode.RefreshProviderOwned));

        Assert.True(_details.TryApplyMetadata(
            new GameMetadataValue(
                game.Id,
                GameMetadataField.Description,
                "My description",
                "en",
                GameMetadataValueOrigin.User,
                null,
                null,
                null,
                DateTimeOffset.UtcNow),
            GameMetadataApplyMode.UserEdit));
        Assert.False(_details.TryApplyMetadata(
            ProviderValue(game.Id, "Provider overwrite", "screenscraper"),
            GameMetadataApplyMode.RefreshProviderOwned));

        var saved = Assert.Single(_details.GetDetails(game.Id).Metadata);
        Assert.Equal("My description", saved.Value);
        Assert.Equal(GameMetadataValueOrigin.User, saved.Origin);
        Assert.Null(saved.ProviderId);
    }

    [Fact]
    public void Metadata_StoresLocalizedDescriptionsIndependently()
    {
        var game = AddGame("Localized.iso");
        Assert.True(_details.TryApplyMetadata(
            ProviderValue(game.Id, "English", "screenscraper", "EN"),
            GameMetadataApplyMode.FillMissing));
        Assert.True(_details.TryApplyMetadata(
            ProviderValue(game.Id, "Français", "screenscraper", "fr"),
            GameMetadataApplyMode.FillMissing));

        var values = _details.GetDetails(game.Id).Metadata;
        Assert.Equal(2, values.Count);
        Assert.Contains(values, value => value.Locale == "en" && value.Value == "English");
        Assert.Contains(values, value => value.Locale == "fr" && value.Value == "Français");
    }

    [Fact]
    public void Media_IsPortable_AndOnlyOneAssetPerKindIsSelected()
    {
        var game = AddGame("Media.iso");
        var firstPath = Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "first.png");
        var secondPath = Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "second.png");

        var first = _details.SaveMedia(ProviderMedia(game.Id, firstPath, isSelected: true));
        var second = _details.SaveMedia(ProviderMedia(game.Id, secondPath, isSelected: true));

        var media = _details.GetDetails(game.Id).Media;
        Assert.Equal(2, media.Count);
        Assert.False(media.Single(asset => asset.Id == first.Id).IsSelected);
        Assert.True(media.Single(asset => asset.Id == second.Id).IsSelected);
        Assert.Equal(
            GameMediaSelectionOrigin.Provider,
            media.Single(asset => asset.Id == second.Id).SelectionOrigin);
        Assert.All(media, asset => Assert.True(Path.IsPathRooted(asset.LocalPath)));

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT LocalPath FROM GameMediaAssets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", second.Id);
        Assert.Equal(
            $"Data/Media/{game.Id}/second.png",
            ((string)command.ExecuteScalar()!).Replace('\\', '/'));
    }

    [Fact]
    public void GetSelectedMediaPaths_ReturnsOnlySelectedAssetsOfTheRequestedKind()
    {
        var selectedGame = AddGame("SelectedTexture.sfc");
        var unselectedGame = AddGame("UnselectedTexture.sfc");
        var selectedPath = Path.Combine(
            AppPaths.DataDirectory,
            "Media",
            selectedGame.Id.ToString(),
            "support-texture.png");

        _details.SaveMedia(ProviderMedia(selectedGame.Id, selectedPath, isSelected: true) with
        {
            Kind = GameMediaKind.PhysicalMediaTexture,
        });
        _details.SaveMedia(ProviderMedia(
            selectedGame.Id,
            Path.Combine(AppPaths.DataDirectory, "Media", selectedGame.Id.ToString(), "support-2D.png"),
            isSelected: true) with
        {
            Kind = GameMediaKind.PhysicalMedia,
        });
        _details.SaveMedia(ProviderMedia(
            unselectedGame.Id,
            Path.Combine(AppPaths.DataDirectory, "Media", unselectedGame.Id.ToString(), "support-texture.png"),
            isSelected: false) with
        {
            Kind = GameMediaKind.PhysicalMediaTexture,
        });

        var paths = _details.GetSelectedMediaPaths(GameMediaKind.PhysicalMediaTexture);

        var actual = Assert.Single(paths);
        Assert.Equal(selectedGame.Id, actual.Key);
        Assert.Equal(Path.GetFullPath(selectedPath), actual.Value);
    }

    [Fact]
    public void SelectMedia_RejectsAnAssetFromAnotherKind()
    {
        var game = AddGame("Selection.iso");
        var screenshot = _details.SaveMedia(ProviderMedia(
            game.Id,
            Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "shot.png"),
            isSelected: false));

        Assert.False(_details.SelectMedia(game.Id, GameMediaKind.Fanart, screenshot.Id));
        Assert.False(Assert.Single(_details.GetDetails(game.Id).Media).IsSelected);
    }

    [Fact]
    public void ProviderRefresh_CannotChangeAUserMediaSelection()
    {
        var game = AddGame("ProtectedSelection.iso");
        var firstPath = Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "first.png");
        var secondPath = Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "second.png");
        var first = _details.SaveMedia(ProviderMedia(game.Id, firstPath, isSelected: false));
        var second = _details.SaveMedia(ProviderMedia(game.Id, secondPath, isSelected: false));
        Assert.True(_details.SelectMedia(game.Id, GameMediaKind.Screenshot, first.Id));

        var providerAttempt = _details.SaveMedia(
            ProviderMedia(game.Id, secondPath, isSelected: true) with { Id = second.Id });
        _details.SaveMedia(ProviderMedia(game.Id, firstPath, isSelected: false) with { Id = first.Id });

        Assert.False(providerAttempt.IsSelected);
        var media = _details.GetDetails(game.Id).Media;
        var selected = Assert.Single(media, asset => asset.IsSelected);
        Assert.Equal(first.Id, selected.Id);
        Assert.Equal(GameMediaSelectionOrigin.User, selected.SelectionOrigin);
    }

    [Fact]
    public void ProviderMedia_TakesOverAUserSelection_WhenOverrideRequested()
    {
        // The single-game scraper's explicit tick overrides even a user-selected asset.
        var game = AddGame("OverrideSelection.iso");
        var firstPath = Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "first.png");
        var secondPath = Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "second.png");
        var first = _details.SaveMedia(ProviderMedia(game.Id, firstPath, isSelected: false));
        var second = _details.SaveMedia(ProviderMedia(game.Id, secondPath, isSelected: false));
        Assert.True(_details.SelectMedia(game.Id, GameMediaKind.Screenshot, first.Id));

        var overridden = _details.SaveMedia(
            ProviderMedia(game.Id, secondPath, isSelected: true) with { Id = second.Id },
            overrideUserSelection: true);

        Assert.True(overridden.IsSelected);
        var selected = Assert.Single(_details.GetDetails(game.Id).Media, asset => asset.IsSelected);
        Assert.Equal(second.Id, selected.Id);
    }

    [Fact]
    public void ProviderMedia_CannotOverwriteUserOwnedMediaAtTheSamePath()
    {
        var game = AddGame("ProtectedMedia.iso");
        var path = Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "user.png");
        _details.SaveMedia(new GameMediaAsset(
            0,
            game.Id,
            GameMediaKind.Screenshot,
            path,
            false,
            null,
            GameMediaOrigin.User,
            null,
            null,
            null,
            null,
            null,
            ".png",
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow));

        Assert.Throws<InvalidOperationException>(() =>
            _details.SaveMedia(ProviderMedia(game.Id, path, isSelected: false)));
    }

    [Fact]
    public void ProviderMatch_UpsertsLatestEvidenceAndStatus()
    {
        var game = AddGame("Match.iso");
        _details.UpsertProviderMatch(new GameProviderMatch(
            game.Id,
            "screenscraper",
            "58",
            1,
            "100",
            "200",
            GameProviderMatchMethod.Sha1,
            "ABC123",
            GameMetadataStatus.Matched,
            DateTimeOffset.UtcNow,
            null));
        _details.UpsertProviderMatch(new GameProviderMatch(
            game.Id,
            "ScreenScraper",
            "58",
            1,
            null,
            null,
            GameProviderMatchMethod.Sha1,
            "ABC123",
            GameMetadataStatus.Failed,
            DateTimeOffset.UtcNow,
            "Quota exhausted"));

        var match = Assert.Single(_details.GetDetails(game.Id).ProviderMatches);
        Assert.Equal(GameMetadataStatus.Failed, match.Status);
        Assert.Equal(1, match.SystemMappingVersion);
        Assert.Equal("Quota exhausted", match.LastError);
    }

    [Fact]
    public void ProviderMatch_RoundTripsCoverageComplete_AndDefaultsToFalse()
    {
        var game = AddGame("Coverage.iso");

        // Default (omitted) is incomplete, so an upgraded database never skips existing matches.
        _details.UpsertProviderMatch(MatchWithCoverage(game.Id, null));
        Assert.False(Assert.Single(_details.GetDetails(game.Id).ProviderMatches).CoverageComplete);

        // A coverage-complete scrape persists and reads back true.
        _details.UpsertProviderMatch(MatchWithCoverage(game.Id, true));
        Assert.True(Assert.Single(_details.GetDetails(game.Id).ProviderMatches).CoverageComplete);

        // A later partial scrape can drop it back to false (the game gained a newly-offered gap).
        _details.UpsertProviderMatch(MatchWithCoverage(game.Id, false));
        Assert.False(Assert.Single(_details.GetDetails(game.Id).ProviderMatches).CoverageComplete);
    }

    private static GameProviderMatch MatchWithCoverage(long gameId, bool? coverageComplete) =>
        coverageComplete is null
            ? new GameProviderMatch(
                gameId, "screenscraper", "58", 1, "100", "200",
                GameProviderMatchMethod.Sha1, "ABC123", GameMetadataStatus.Matched, DateTimeOffset.UtcNow, null)
            : new GameProviderMatch(
                gameId, "screenscraper", "58", 1, "100", "200",
                GameProviderMatchMethod.Sha1, "ABC123", GameMetadataStatus.Matched, DateTimeOffset.UtcNow, null,
                coverageComplete.Value);

    [Fact]
    public void RichDetails_CascadeWhenLibraryRowIsRemoved()
    {
        var game = AddGame("Cascade.iso");
        _details.TryApplyMetadata(
            ProviderValue(game.Id, "Description", "screenscraper"),
            GameMetadataApplyMode.FillMissing);
        _details.SaveMedia(ProviderMedia(
            game.Id,
            Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "shot.png"),
            isSelected: true));
        _details.UpsertProviderMatch(new GameProviderMatch(
            game.Id,
            "screenscraper",
            "58",
            1,
            "100",
            "200",
            GameProviderMatchMethod.Sha1,
            "ABC123",
            GameMetadataStatus.Matched,
            DateTimeOffset.UtcNow,
            null));

        _library.RemoveGame(game.Id);

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT COUNT(*) FROM GameMetadataValues) + " +
            "(SELECT COUNT(*) FROM GameMediaAssets) + " +
            "(SELECT COUNT(*) FROM GameProviderMatches);";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void GetAllDetailsProjections_MatchesPerGameGetDetails_AndOmitsGamesWithNoDetails()
    {
        // Rich game: every scalar field (one with two locales), all four media kinds, a provider match.
        var rich = AddGame("Rich.iso");
        ApplyProviderField(rich.Id, GameMetadataField.Description, "A long description");
        ApplyProviderField(rich.Id, GameMetadataField.Rating, "14");
        ApplyProviderField(rich.Id, GameMetadataField.Genre, "Action", "en");
        ApplyProviderField(rich.Id, GameMetadataField.Genre, "Aktion", "de");
        ApplyProviderField(rich.Id, GameMetadataField.ReleaseDate, "2001-11-15");
        ApplyProviderField(rich.Id, GameMetadataField.Players, "2");
        ApplyProviderField(rich.Id, GameMetadataField.Developer, "Studio");
        ApplyProviderField(rich.Id, GameMetadataField.Publisher, "Publisher");
        SaveMediaKind(rich.Id, GameMediaKind.BoxFront, "box.png");
        SaveMediaKind(rich.Id, GameMediaKind.Screenshot, "shot.png");
        SaveMediaKind(rich.Id, GameMediaKind.Wheel, "wheel.png");
        SaveMediaKind(rich.Id, GameMediaKind.Fanart, "fanart.png");
        AddProviderMatch(rich.Id);

        // Partial game: a couple of scalars, one media kind, no provider match.
        var partial = AddGame("Partial.iso");
        ApplyProviderField(partial.Id, GameMetadataField.Genre, "Puzzle");
        ApplyProviderField(partial.Id, GameMetadataField.ReleaseDate, "1998");
        SaveMediaKind(partial.Id, GameMediaKind.Screenshot, "shot.png");

        // Provider-match-only game: nothing else stored.
        var matchOnly = AddGame("MatchOnly.iso");
        AddProviderMatch(matchOnly.Id);

        // Bare game: added to the library but has no stored details at all.
        var bare = AddGame("Bare.iso");

        var projections = _details.GetAllDetailsProjections();

        foreach (var id in new[] { rich.Id, partial.Id, matchOnly.Id })
        {
            Assert.True(projections.ContainsKey(id));
            Assert.Equal(ExpectedFromDetails(_details.GetDetails(id)), projections[id]);
        }

        Assert.False(projections.ContainsKey(bare.Id));
        Assert.Equal(3, projections.Count);
    }

    [Fact]
    public void GetAllDetailsProjections_OnAnEmptyStore_ReturnsAnEmptyMap()
    {
        Assert.Empty(_details.GetAllDetailsProjections());
    }

    private static GameDetailsProjection ExpectedFromDetails(GameDetails details)
    {
        bool HasMedia(GameMediaKind kind) => details.Media.Any(asset => asset.Kind == kind);
        string? First(GameMetadataField field) =>
            details.Metadata.FirstOrDefault(value => value.Field == field)?.Value;

        return new GameDetailsProjection(
            HasMedia(GameMediaKind.BoxFront),
            HasMedia(GameMediaKind.Screenshot),
            HasMedia(GameMediaKind.Wheel),
            HasMedia(GameMediaKind.Fanart),
            details.Metadata.Any(value => value.Field == GameMetadataField.Description),
            details.ProviderMatches.Count > 0,
            First(GameMetadataField.Rating),
            First(GameMetadataField.Genre),
            First(GameMetadataField.ReleaseDate),
            First(GameMetadataField.Players),
            First(GameMetadataField.Developer),
            First(GameMetadataField.Publisher),
            HasMedia(GameMediaKind.TitleScreen),
            HasMedia(GameMediaKind.BoxBack),
            HasMedia(GameMediaKind.BoxSpine),
            HasMedia(GameMediaKind.PhysicalMedia),
            HasMedia(GameMediaKind.PhysicalMediaTexture));
    }

    private void ApplyProviderField(long gameId, GameMetadataField field, string value, string locale = "en") =>
        Assert.True(_details.TryApplyMetadata(
            new GameMetadataValue(
                gameId,
                field,
                value,
                locale,
                GameMetadataValueOrigin.Provider,
                "screenscraper",
                "provider-game-id",
                "https://example.test/game",
                DateTimeOffset.UtcNow),
            GameMetadataApplyMode.FillMissing));

    private void SaveMediaKind(long gameId, GameMediaKind kind, string fileName) =>
        _details.SaveMedia(
            ProviderMedia(
                gameId,
                Path.Combine(AppPaths.DataDirectory, "Media", gameId.ToString(), fileName),
                isSelected: false) with
            {
                Kind = kind,
            });

    private void AddProviderMatch(long gameId) =>
        _details.UpsertProviderMatch(new GameProviderMatch(
            gameId,
            "screenscraper",
            "58",
            1,
            "100",
            "200",
            GameProviderMatchMethod.Sha1,
            "ABC123",
            GameMetadataStatus.Matched,
            DateTimeOffset.UtcNow,
            null));

    private Game AddGame(string filename)
    {
        var path = Path.Combine(BaseDirectory, "Games", filename);
        _library.AddGames([
            new Game
            {
                SystemId = "playstation2",
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                TitleOrigin = GameTitleOrigin.Filename,
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        return _library.GetGames().Single(game => game.Path == path);
    }

    private static GameMetadataValue ProviderValue(
        long gameId,
        string value,
        string providerId,
        string locale = "en") =>
        new(
            gameId,
            GameMetadataField.Description,
            value,
            locale,
            GameMetadataValueOrigin.Provider,
            providerId,
            "provider-game-id",
            "https://example.test/game",
            DateTimeOffset.UtcNow);

    private static GameMediaAsset ProviderMedia(long gameId, string path, bool isSelected) =>
        new(
            0,
            gameId,
            GameMediaKind.Screenshot,
            path,
            isSelected,
            isSelected ? GameMediaSelectionOrigin.Provider : null,
            GameMediaOrigin.Provider,
            "screenscraper",
            "provider-game-id",
            "https://example.test/screenshot.png",
            "us",
            "en",
            ".png",
            1920,
            1080,
            "CRC32",
            "MD5",
            "SHA1",
            DateTimeOffset.UtcNow);
}
