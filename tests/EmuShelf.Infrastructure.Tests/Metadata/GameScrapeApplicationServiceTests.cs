using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class GameScrapeApplicationServiceTests : TempAppDirectoryTestBase
{
    private readonly LibraryDatabase _database;
    private readonly RelativePathResolver _resolver;
    private readonly GameLibrary _library;
    private readonly SqliteGameDetailsStore _details;
    private readonly SqliteGameMetadataStore _metadata;
    private readonly FakeDownloader _downloader;
    private readonly GameScrapeApplicationService _service;

    public GameScrapeApplicationServiceTests()
    {
        AppPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(AppPaths);
        _database.Initialize();
        _resolver = new RelativePathResolver(AppPaths);
        _library = new GameLibrary(_database, _resolver);
        _details = new SqliteGameDetailsStore(_database, _resolver);
        _metadata = new SqliteGameMetadataStore(_database, _resolver);
        _downloader = new FakeDownloader(Path.Combine(AppPaths.CacheDirectory, "test-downloads"));
        _service = new GameScrapeApplicationService(_details, _metadata, _downloader, AppPaths);
    }

    [Fact]
    public async Task Apply_WritesMetadata_ImportsMedia_AndProjectsBoxFrontToCover()
    {
        var game = AddGame("Apply.iso");

        var result = await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [TitleValue(game.Id, "Canonical Title")],
            [MediaImport(GameMediaKind.BoxFront, "box"), MediaImport(GameMediaKind.Screenshot, "shot")],
            GameMetadataApplyMode.FillMissing));

        Assert.Null(result.Error);
        Assert.Equal(1, result.MetadataApplied);
        Assert.Equal(2, result.MediaImported);
        Assert.True(result.CoverProjected);
        Assert.All(result.Media, media => Assert.Equal(GameMediaApplyOutcome.Imported, media.Outcome));

        var details = _details.GetDetails(game.Id);
        Assert.Contains(details.Metadata, value =>
            value.Field == GameMetadataField.Title &&
            value.Value == "Canonical Title" &&
            value.Origin == GameMetadataValueOrigin.Provider);
        Assert.Single(details.ProviderMatches);

        var box = details.Media.Single(media => media.Kind == GameMediaKind.BoxFront);
        Assert.True(box.IsSelected);
        Assert.Equal(GameMediaOrigin.Provider, box.Origin);
        Assert.True(File.Exists(box.LocalPath));
        Assert.True(File.Exists(details.Media.Single(m => m.Kind == GameMediaKind.Screenshot).LocalPath));

        // The selected box-front is projected into the fast Games.CoverPath grid column.
        Assert.True(PathsEqual(box.LocalPath, CoverPath(game.Id)!));
    }

    [Fact]
    public async Task Apply_ProjectsTitleScreenToCover_WhenCoverKindIsTitleScreen()
    {
        var game = AddGame("Arcade.zip");

        var result = await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [],
            [MediaImport(GameMediaKind.BoxFront, "box"), MediaImport(GameMediaKind.TitleScreen, "title")],
            GameMetadataApplyMode.FillMissing,
            GameMediaKind.TitleScreen));

        Assert.True(result.CoverProjected);
        var details = _details.GetDetails(game.Id);

        // The title screen — not the box front — becomes the cover for a title-screen system (arcade).
        var title = details.Media.Single(media => media.Kind == GameMediaKind.TitleScreen);
        Assert.True(PathsEqual(title.LocalPath, CoverPath(game.Id)!));
        var box = details.Media.Single(media => media.Kind == GameMediaKind.BoxFront);
        Assert.False(PathsEqual(box.LocalPath, CoverPath(game.Id)!));
    }

    [Fact]
    public async Task Apply_DoesNotProjectTitleScreen_WhenCoverKindIsBoxFront()
    {
        var game = AddGame("Console.iso");

        var result = await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [],
            [MediaImport(GameMediaKind.TitleScreen, "title")],
            GameMetadataApplyMode.FillMissing)); // default cover kind is the box front

        Assert.False(result.CoverProjected);
        Assert.Null(CoverPath(game.Id));
    }

    [Fact]
    public async Task FillMissing_SkipsAKindThatAlreadyHasAnActiveAsset_WithoutDownloading()
    {
        var game = AddGame("Skip.iso");
        _details.SaveMedia(ProviderBox(
            game.Id,
            Path.Combine(AppPaths.DataDirectory, "Media", game.Id.ToString(), "existing-BoxFront.png"),
            selected: true,
            provider: "other"));

        var result = await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [],
            [MediaImport(GameMediaKind.BoxFront, "box")],
            GameMetadataApplyMode.FillMissing));

        Assert.Equal(GameMediaApplyOutcome.SkippedExisting, result.Media.Single().Outcome);
        Assert.Equal(0, _downloader.Calls);
        Assert.False(result.CoverProjected);
    }

    [Fact]
    public async Task Apply_NeverOverwritesAUserOwnedFileAtTheProviderPath()
    {
        var game = AddGame("Protected.iso");
        var providerPath = Path.Combine(
            AppPaths.DataDirectory, "Media", game.Id.ToString(), "screenscraper-BoxFront.png");
        Directory.CreateDirectory(Path.GetDirectoryName(providerPath)!);
        File.WriteAllText(providerPath, "USER-FILE");
        _details.SaveMedia(UserBox(game.Id, providerPath));

        var result = await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [],
            [MediaImport(GameMediaKind.BoxFront, "box")],
            GameMetadataApplyMode.RefreshProviderOwned));

        Assert.Equal(GameMediaApplyOutcome.SkippedProtected, result.Media.Single().Outcome);
        Assert.Equal(0, _downloader.Calls);
        Assert.False(result.CoverProjected);
        Assert.Equal("USER-FILE", File.ReadAllText(providerPath));
    }

    [Fact]
    public async Task Refresh_ReplacesSameProviderMedia_ButUserMetadataWins()
    {
        var game = AddGame("Refresh.iso");
        await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [TitleValue(game.Id, "First")],
            [MediaImport(GameMediaKind.BoxFront, "box")],
            GameMetadataApplyMode.FillMissing));

        Assert.True(_details.TryApplyMetadata(
            new GameMetadataValue(
                game.Id, GameMetadataField.Title, "My Title", null,
                GameMetadataValueOrigin.User, null, null, null, DateTimeOffset.UtcNow),
            GameMetadataApplyMode.UserEdit));

        var result = await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [TitleValue(game.Id, "Second")],
            [MediaImport(GameMediaKind.BoxFront, "box")],
            GameMetadataApplyMode.RefreshProviderOwned));

        Assert.Equal(0, result.MetadataApplied);
        Assert.Equal(1, result.MetadataSkipped);
        Assert.Equal(1, result.MediaImported);

        var details = _details.GetDetails(game.Id);
        Assert.Equal("My Title", details.Metadata.Single(v => v.Field == GameMetadataField.Title).Value);
        // Refresh replaced the provider's box-front in place — it did not create a duplicate.
        Assert.Single(details.Media, media => media.Kind == GameMediaKind.BoxFront);
    }

    [Fact]
    public async Task DownloadFailure_ReportsFailure_LeavesNoMedia_ButStillRecordsTheMatch()
    {
        var game = AddGame("Fail.iso");
        _downloader.ReturnNull = true;

        var result = await _service.ApplyAsync(new GameScrapeApplyRequest(
            game.Id,
            Match(game.Id),
            [],
            [MediaImport(GameMediaKind.BoxFront, "box")],
            GameMetadataApplyMode.FillMissing));

        Assert.Equal(GameMediaApplyOutcome.DownloadFailed, result.Media.Single().Outcome);
        Assert.False(result.CoverProjected);
        var details = _details.GetDetails(game.Id);
        Assert.Empty(details.Media);
        Assert.Single(details.ProviderMatches);
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

    private string? CoverPath(long gameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CoverPath FROM Games WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : _resolver.ToAbsolutePath((string)value);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static GameProviderMatch Match(long gameId) => new(
        gameId, ScreenScraperProvider.Id, "58", 1, "100", "200",
        GameProviderMatchMethod.Sha1, "ABC123", GameMetadataStatus.Matched, DateTimeOffset.UtcNow, null);

    private static GameMetadataValue TitleValue(long gameId, string value) => new(
        gameId, GameMetadataField.Title, value, null, GameMetadataValueOrigin.Provider,
        ScreenScraperProvider.Id, "100", "https://example.test/game", DateTimeOffset.UtcNow);

    private static GameMediaImport MediaImport(
        GameMediaKind kind,
        string id,
        string provider = ScreenScraperProvider.Id) => new(
        kind, new Uri($"https://example.test/{id}.png"), ".png", provider,
        id, "us", "en", 512, 512, null, null, null);

    private static GameMediaAsset UserBox(long gameId, string path) => new(
        0, gameId, GameMediaKind.BoxFront, path, false, null, GameMediaOrigin.User,
        null, null, null, null, null, ".png", null, null, null, null, null, DateTimeOffset.UtcNow);

    private static GameMediaAsset ProviderBox(long gameId, string path, bool selected, string provider) => new(
        0, gameId, GameMediaKind.BoxFront, path, selected,
        selected ? GameMediaSelectionOrigin.Provider : null, GameMediaOrigin.Provider,
        provider, "pid", "https://example.test/x.png", "us", "en", ".png",
        null, null, null, null, null, DateTimeOffset.UtcNow);

    private sealed class FakeDownloader : IRemoteArtworkDownloader
    {
        private readonly string _directory;

        public FakeDownloader(string directory)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
        }

        public int Calls { get; private set; }

        public bool ReturnNull { get; set; }

        public Task<DownloadedArtwork?> DownloadFirstAsync(
            IReadOnlyList<ArtworkCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ReturnNull)
                return Task.FromResult<DownloadedArtwork?>(null);

            var candidate = candidates[0];
            var temporaryPath = Path.Combine(_directory, $"{Guid.NewGuid():N}{candidate.FileExtension}");
            File.WriteAllBytes(temporaryPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            return Task.FromResult<DownloadedArtwork?>(new DownloadedArtwork(candidate, temporaryPath));
        }
    }
}
