using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperBatchServiceTests : TempAppDirectoryTestBase
{
    private readonly LibraryDatabase _database;
    private readonly GameLibrary _library;
    private readonly SqliteGameMetadataStore _games;
    private readonly FakePreview _preview = new();
    private readonly FakeApply _apply = new();
    private readonly ScreenScraperBatchService _batch;

    public ScreenScraperBatchServiceTests()
    {
        AppPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(AppPaths);
        _database.Initialize();
        var resolver = new RelativePathResolver(AppPaths);
        _library = new GameLibrary(_database, resolver);
        _games = new SqliteGameMetadataStore(_database, resolver);
        _batch = new ScreenScraperBatchService(_preview, _apply, _games);
    }

    [Fact]
    public async Task Run_AppliesEveryMatch_AndNeverTitleSearches()
    {
        var ids = AddGames("A.iso", "B.iso", "C.iso");
        foreach (var id in ids)
            _preview.Results[id] = Success(id);

        var summary = await _batch.RunAsync(
            ids, Enabled(), GameMetadataApplyMode.FillMissing, null, null, null);

        Assert.Equal(GameScrapeBatchStopReason.Completed, summary.StopReason);
        Assert.Equal(3, summary.Applied);
        Assert.Equal(0, summary.NotProcessed);
        Assert.Equal(3, _apply.Calls);
        Assert.Equal(0, _preview.SearchCalls);
    }

    [Fact]
    public async Task Run_RecordsMixedOutcomes_AndKeepsGoing()
    {
        var ids = AddGames("Hit.iso", "Miss.iso", "Bad.iso");
        _preview.Results[ids[0]] = Success(ids[0]);
        _preview.Results[ids[1]] = Failure(ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound);
        _preview.Results[ids[2]] = Failure(ScreenScraperPreviewStatus.UnsupportedSystem);

        var summary = await _batch.RunAsync(
            ids, Enabled(), GameMetadataApplyMode.FillMissing, null, null, null);

        Assert.Equal(GameScrapeBatchStopReason.Completed, summary.StopReason);
        Assert.Equal(3, summary.Results.Count);
        Assert.Equal(1, summary.Applied);
        Assert.Equal(1, summary.NoMatch);
        Assert.Equal(1, summary.Unsupported);
    }

    [Fact]
    public async Task Run_StopsEarly_OnQuotaExhaustion_LeavingLaterGamesUnprocessed()
    {
        var ids = AddGames("First.iso", "Quota.iso", "Never.iso");
        _preview.Results[ids[0]] = Success(ids[0]);
        _preview.Results[ids[1]] = Failure(
            ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.DailyQuotaExceeded);

        var summary = await _batch.RunAsync(
            ids, Enabled(), GameMetadataApplyMode.FillMissing, null, null, null);

        Assert.Equal(GameScrapeBatchStopReason.QuotaExhausted, summary.StopReason);
        Assert.Single(summary.Results);
        Assert.Equal(GameScrapeBatchOutcome.Applied, summary.Results[0].Outcome);
        Assert.Equal(2, summary.NotProcessed);
    }

    [Fact]
    public async Task Run_MatchedButNothingWritten_IsReportedAsNothingToApply()
    {
        var ids = AddGames("Filled.iso");
        _preview.Results[ids[0]] = Success(ids[0]);
        _apply.Results[ids[0]] = new GameScrapeApplyResult(ids[0], 0, 3, [], false);

        var summary = await _batch.RunAsync(
            ids, Enabled(), GameMetadataApplyMode.FillMissing, null, null, null);

        Assert.Equal(GameScrapeBatchOutcome.NothingToApply, Assert.Single(summary.Results).Outcome);
    }

    [Fact]
    public async Task Run_HonorsCancellation()
    {
        var ids = AddGames("One.iso", "Two.iso");
        foreach (var id in ids)
            _preview.Results[id] = Success(id);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var summary = await _batch.RunAsync(
            ids, Enabled(), GameMetadataApplyMode.FillMissing, null, null, null, cts.Token);

        Assert.Equal(GameScrapeBatchStopReason.Cancelled, summary.StopReason);
        Assert.Empty(summary.Results);
    }

    private long[] AddGames(params string[] filenames)
    {
        _library.AddGames(filenames.Select(name => new Game
        {
            SystemId = "playstation2",
            Path = Path.Combine(BaseDirectory, "Games", name),
            Title = Path.GetFileNameWithoutExtension(name),
            TitleOrigin = GameTitleOrigin.Filename,
            DateAdded = DateTimeOffset.UtcNow,
        }).ToArray());
        return filenames
            .Select(name => _library.GetGames().Single(game =>
                game.Path == Path.Combine(BaseDirectory, "Games", name)).Id)
            .ToArray();
    }

    private static ScreenScraperSettings Enabled() => new() { Enabled = true };

    private static ScreenScraperPreviewResult Success(long gameId)
    {
        var match = new GameProviderMatch(
            gameId, ScreenScraperProvider.Id, "58", 1, "100", null,
            GameProviderMatchMethod.Serial, "SLUS-1", GameMetadataStatus.Matched, DateTimeOffset.UtcNow, null);
        var preview = new ScreenScraperGamePreview(
            gameId,
            match,
            [new GameMetadataValue(
                gameId, GameMetadataField.Title, "A Title", null, GameMetadataValueOrigin.Provider,
                ScreenScraperProvider.Id, "100", "https://x/", DateTimeOffset.UtcNow)],
            new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>(),
            new GameDetails(gameId, [], [], []),
            null,
            null);
        return new ScreenScraperPreviewResult(
            ScreenScraperPreviewStatus.Success, preview, ScreenScraperRequestStatus.Success, null);
    }

    private static ScreenScraperPreviewResult Failure(
        ScreenScraperPreviewStatus status,
        ScreenScraperRequestStatus? requestStatus = null) => new(status, null, requestStatus, null);

    private sealed class FakePreview : IScreenScraperPreviewService
    {
        public Dictionary<long, ScreenScraperPreviewResult> Results { get; } = new();

        public int SearchCalls { get; private set; }

        public Task<ScreenScraperPreviewResult> PreviewAsync(
            long gameId,
            ScreenScraperSettings settings,
            bool allowFingerprinting,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.TryGetValue(gameId, out var result)
                ? result
                : Failure(ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound));

        public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchAsync(
            long gameId,
            string query,
            ScreenScraperSettings settings,
            CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            return Task.FromResult(new ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>(
                ScreenScraperRequestStatus.Success, [], null, null));
        }

        public Task<ScreenScraperPreviewResult> PreviewByProviderGameIdAsync(
            long gameId,
            string providerGameId,
            ScreenScraperSettings settings,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeApply : IGameScrapeApplicationService
    {
        public Dictionary<long, GameScrapeApplyResult> Results { get; } = new();

        public int Calls { get; private set; }

        public Task<GameScrapeApplyResult> ApplyAsync(
            GameScrapeApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Results.TryGetValue(request.GameId, out var result)
                ? result
                : new GameScrapeApplyResult(request.GameId, 1, 0, [], false));
        }
    }
}
