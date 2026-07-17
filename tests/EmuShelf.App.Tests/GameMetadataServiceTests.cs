using EmuShelf.App.Services;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;

namespace EmuShelf.App.Tests;

public class GameMetadataServiceTests
{
    [Fact]
    public async Task Enrich_AppliesExactTitleAndCoverAndRecordsProvenance()
    {
        var game = new Game
        {
            Id = 7,
            SystemId = "test-system",
            Path = "/games/filename.iso",
            Title = "filename",
            TitleOrigin = GameTitleOrigin.Filename,
            DateAdded = DateTimeOffset.UtcNow,
        };
        var store = new RecordingMetadataStore(game);
        var candidate = new ArtworkCandidate(
            "test-art",
            new Uri("https://example.test/cover.jpg"),
            ".jpg");
        var temporaryPath = Path.GetTempFileName();
        var service = new GameMetadataService(
            store,
            [
                new MetadataSystemProfile(
                    "test-system",
                    GameIdentifierKind.Serial,
                    new Uri("https://example.test/catalog.dat"),
                    new FixedExtractor(),
                    [new FixedArtworkProvider(candidate)]),
            ],
            new FixedCatalog(),
            new FixedDownloader(new DownloadedArtwork(candidate, temporaryPath)),
            new RecordingCoverService());

        var summary = await service.EnrichAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.TitlesApplied);
        Assert.Equal(1, summary.CoversApplied);
        Assert.Equal("Catalog Game (USA)", store.Game.Title);
        Assert.Equal(GameTitleOrigin.Catalog, store.Game.TitleOrigin);
        Assert.Equal(GameCoverOrigin.Downloaded, store.Game.CoverOrigin);
        Assert.Equal(GameMetadataStatus.Matched, store.LastAttempt?.Status);
        Assert.Equal("test-art", store.LastAttempt?.CoverProviderId);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task Enrich_CatalogUnavailable_StillUsesIdentifierBasedCoverProvider()
    {
        var game = new Game
        {
            Id = 8,
            SystemId = "test-system",
            Path = "/games/filename.iso",
            Title = "filename",
            TitleOrigin = GameTitleOrigin.Filename,
            DateAdded = DateTimeOffset.UtcNow,
        };
        var store = new RecordingMetadataStore(game);
        var candidate = new ArtworkCandidate(
            "serial-art",
            new Uri("https://example.test/SLUS-12345.jpg"),
            ".jpg");
        var temporaryPath = Path.GetTempFileName();
        var service = new GameMetadataService(
            store,
            [
                new MetadataSystemProfile(
                    "test-system",
                    GameIdentifierKind.Serial,
                    new Uri("https://example.test/catalog.dat"),
                    new FixedExtractor(),
                    [new FixedArtworkProvider(candidate)]),
            ],
            new ThrowingCatalog(),
            new FixedDownloader(new DownloadedArtwork(candidate, temporaryPath)),
            new RecordingCoverService());

        var summary = await service.EnrichAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.TitlesApplied);
        Assert.Equal(1, summary.CoversApplied);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(GameMetadataStatus.Partial, store.LastAttempt?.Status);
        Assert.Equal("Catalog unavailable", store.LastAttempt?.Error);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task Enrich_EqualCatalogTitle_MarksOriginWithoutCountingVisibleChange()
    {
        var game = new Game
        {
            Id = 9,
            SystemId = "test-system",
            Path = "/games/Catalog Game (USA).iso",
            Title = "Catalog Game (USA)",
            TitleOrigin = GameTitleOrigin.Filename,
            DateAdded = DateTimeOffset.UtcNow,
        };
        var store = new RecordingMetadataStore(game);
        var candidate = new ArtworkCandidate(
            "test-art",
            new Uri("https://example.test/cover.jpg"),
            ".jpg");
        var temporaryPath = Path.GetTempFileName();
        var service = new GameMetadataService(
            store,
            [
                new MetadataSystemProfile(
                    "test-system",
                    GameIdentifierKind.Serial,
                    new Uri("https://example.test/catalog.dat"),
                    new FixedExtractor(),
                    [new FixedArtworkProvider(candidate)]),
            ],
            new FixedCatalog(),
            new FixedDownloader(new DownloadedArtwork(candidate, temporaryPath)),
            new RecordingCoverService());

        var summary = await service.EnrichAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(0, summary.TitlesApplied);
        Assert.Equal("Catalog Game (USA)", store.Game.Title);
        Assert.Equal(GameTitleOrigin.Catalog, store.Game.TitleOrigin);
        Assert.False(File.Exists(temporaryPath));
    }

    [Fact]
    public async Task Enrich_ReusesStoredIdentifiers_WithoutReextractingOnRerun()
    {
        var game = new Game
        {
            Id = 10,
            SystemId = "test-system",
            Path = "/games/filename.iso",
            Title = "filename",
            TitleOrigin = GameTitleOrigin.Filename,
            DateAdded = DateTimeOffset.UtcNow,
        };
        var store = new RecordingMetadataStore(game);
        var extractor = new CountingExtractor();
        var candidate = new ArtworkCandidate(
            "test-art",
            new Uri("https://example.test/cover.jpg"),
            ".jpg");
        var service = new GameMetadataService(
            store,
            [
                new MetadataSystemProfile(
                    "test-system",
                    GameIdentifierKind.Serial,
                    new Uri("https://example.test/catalog.dat"),
                    extractor,
                    [new FixedArtworkProvider(candidate)]),
            ],
            new FixedCatalog(),
            new FixedDownloader(new DownloadedArtwork(candidate, Path.GetTempFileName())),
            new RecordingCoverService());

        await service.EnrichAsync([game.Id], TestContext.Current.CancellationToken);
        await service.EnrichAsync([game.Id], TestContext.Current.CancellationToken);

        Assert.Equal(1, extractor.Calls);
    }

    private sealed class FixedExtractor : IGameIdentifierExtractor
    {
        public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        [
            new GameIdentifier(GameIdentifierKind.Serial, "SLUS-12345", "Test", true),
        ];
    }

    private sealed class CountingExtractor : IGameIdentifierExtractor
    {
        public int Calls { get; private set; }

        public IReadOnlyList<GameIdentifier> Extract(Game game)
        {
            Calls++;
            return [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-12345", "Test", true)];
        }
    }

    private sealed class FixedCatalog : IGameMetadataCatalog
    {
        public Task<GameCatalogMatch?> FindMatchAsync(
            MetadataSystemProfile profile,
            IReadOnlyList<GameIdentifier> identifiers,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GameCatalogMatch?>(new(
                "test-catalog",
                "SLUS-12345",
                "Catalog Game (USA)",
                "USA"));
    }

    private sealed class ThrowingCatalog : IGameMetadataCatalog
    {
        public Task<GameCatalogMatch?> FindMatchAsync(
            MetadataSystemProfile profile,
            IReadOnlyList<GameIdentifier> identifiers,
            CancellationToken cancellationToken = default) =>
            Task.FromException<GameCatalogMatch?>(new HttpRequestException("Catalog unavailable"));
    }

    private sealed class FixedArtworkProvider(ArtworkCandidate candidate) : IGameArtworkProvider
    {
        public string Id => candidate.ProviderId;

        public IReadOnlyList<ArtworkCandidate> GetCandidates(
            IReadOnlyList<GameIdentifier> identifiers,
            GameCatalogMatch? match) => [candidate];
    }

    private sealed class FixedDownloader(DownloadedArtwork artwork) : IRemoteArtworkDownloader
    {
        public Task<DownloadedArtwork?> DownloadFirstAsync(
            IReadOnlyList<ArtworkCandidate> candidates,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DownloadedArtwork?>(artwork);
    }

    private sealed class RecordingCoverService : IGameCoverService
    {
        public Task<ImportedGameCover> ImportAsync(
            long gameId,
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportedGameCover(
                $"/covers/{gameId}.jpg",
                $"/cache/{gameId}.png"));

        public Task<string?> GetThumbnailAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task DeleteOwnedCoverAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingMetadataStore(Game game) : IGameMetadataStore
    {
        private IReadOnlyList<GameIdentifier> _identifiers = [];

        public Game Game { get; private set; } = game;
        public GameMetadataAttempt? LastAttempt { get; private set; }

        public Game? GetGame(long gameId) => gameId == Game.Id ? Game : null;
        public IReadOnlyList<Game> GetGamesMissingMetadata(string? systemId = null) => [Game];
        public IReadOnlyList<GameIdentifier> GetIdentifiers(long gameId) => _identifiers;

        public void ReplaceIdentifiers(long gameId, IReadOnlyList<GameIdentifier> identifiers) =>
            _identifiers = identifiers;

        public bool TryApplyCatalogTitle(long gameId, string canonicalTitle, string filenameTitle)
        {
            Game = Game with
            {
                Title = canonicalTitle,
                TitleOrigin = GameTitleOrigin.Catalog,
            };
            return true;
        }

        public bool TryApplyDownloadedCover(
            long gameId,
            string coverPath,
            string providerId,
            string sourceUri)
        {
            Game = Game with
            {
                CoverPath = coverPath,
                CoverOrigin = GameCoverOrigin.Downloaded,
            };
            return true;
        }

        public void RecordAttempt(GameMetadataAttempt attempt) => LastAttempt = attempt;
    }
}
