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
