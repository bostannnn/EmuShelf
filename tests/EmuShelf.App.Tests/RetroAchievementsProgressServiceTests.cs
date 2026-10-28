using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.Tests;

public class RetroAchievementsProgressServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly RetroAchievementsCredentials Credentials = new("Player", "KEY");

    [Fact]
    public async Task RefreshAll_FetchesAndSavesProgress()
    {
        var store = new FakeProgressStore { LinkedIds = { 1234 } };
        var client = new FakeClient();
        var service = new RetroAchievementsProgressService(store, client);

        var summary = await service.RefreshAllAsync(Credentials, Token);

        Assert.Equal(RetroAchievementsRequestStatus.Success, summary.Status);
        Assert.Equal(1, summary.RequestedGames);
        Assert.Equal(1, summary.UpdatedGames);
        Assert.Equal(1234, Assert.Single(store.Saved).GameId);
        Assert.NotNull(store.LastSummaryRefreshAt);
    }

    [Fact]
    public async Task RefreshAll_NoLinkedGames_MakesNoRequest()
    {
        var store = new FakeProgressStore();
        var client = new FakeClient();
        var service = new RetroAchievementsProgressService(store, client);

        var summary = await service.RefreshAllAsync(Credentials, Token);

        Assert.Equal(0, summary.RequestedGames);
        Assert.Empty(client.Requests);
    }

    [Fact]
    public async Task RefreshAll_SplitsIntoBatchesOfAtMostTheCap()
    {
        var store = new FakeProgressStore();
        store.LinkedIds.AddRange(Enumerable.Range(1, RetroAchievementsApi.MaxUserProgressBatchSize + 50));
        var client = new FakeClient();
        var service = new RetroAchievementsProgressService(store, client);

        await service.RefreshAllAsync(Credentials, Token);

        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(RetroAchievementsApi.MaxUserProgressBatchSize, client.Requests[0].Count);
        Assert.Equal(50, client.Requests[1].Count);
    }

    [Fact]
    public async Task RefreshAll_SavesEachResponseBatchAsOneWrite()
    {
        // A per-game write loop here was the Thor's ~16 s post-launch stall: every separate commit is
        // a ~20 ms MediaProvider round trip on Android shared storage.
        var store = new FakeProgressStore();
        store.LinkedIds.AddRange(Enumerable.Range(1, (RetroAchievementsApi.MaxUserProgressBatchSize * 2) + 50));
        var service = new RetroAchievementsProgressService(store, new FakeClient());

        var summary = await service.RefreshAllAsync(Credentials, Token);

        Assert.Equal(
            [RetroAchievementsApi.MaxUserProgressBatchSize, RetroAchievementsApi.MaxUserProgressBatchSize, 50],
            store.BatchSizes);
        Assert.Equal(0, store.SingleSaves);
        Assert.Equal(store.LinkedIds.Count, summary.UpdatedGames);
        Assert.Equal(store.LinkedIds, store.Saved.Select(progress => progress.GameId));
    }

    [Fact]
    public async Task RefreshAll_WritesOnlyGamesWhoseProgressChanged()
    {
        var store = new FakeProgressStore { LinkedIds = { 1, 2, 3, 4 } };
        var earlier = new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero);
        // FakeClient answers (id, 40, 1, 0) for every id: 1 and 3 are already cached with exactly that,
        // 2 has moved on the server since, and 4 has never been cached.
        store.Cached[1] = new(new RetroAchievementsGameProgress(1, 40, 1, 0), earlier);
        store.Cached[2] = new(new RetroAchievementsGameProgress(2, 40, 0, 0), earlier);
        store.Cached[3] = new(new RetroAchievementsGameProgress(3, 40, 1, 0), earlier);
        var service = new RetroAchievementsProgressService(store, new FakeClient());

        var summary = await service.RefreshAllAsync(Credentials, Token);

        Assert.Equal([2, 4], store.Saved.Select(progress => progress.GameId));
        Assert.Equal([2], store.BatchSizes);
        Assert.Equal(4, summary.UpdatedGames);
        Assert.Equal(2, summary.ChangedGames);
    }

    [Fact]
    public async Task RefreshAll_NothingChanged_WritesNoProgressButRecordsTheRefresh()
    {
        // The common cold launch: nothing was earned since the last refresh. No progress rows are
        // rewritten, yet the refresh still counts as done so the next launch does not ask again.
        var store = new FakeProgressStore { LinkedIds = { 1, 2 } };
        var earlier = new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero);
        store.Cached[1] = new(new RetroAchievementsGameProgress(1, 40, 1, 0), earlier);
        store.Cached[2] = new(new RetroAchievementsGameProgress(2, 40, 1, 0), earlier);
        var service = new RetroAchievementsProgressService(store, new FakeClient());

        var summary = await service.RefreshAllAsync(Credentials, Token);

        Assert.Empty(store.Saved);
        Assert.Empty(store.BatchSizes);
        Assert.Equal(2, summary.UpdatedGames);
        Assert.Equal(0, summary.ChangedGames);
        Assert.NotNull(store.LastSummaryRefreshAt);
    }

    [Fact]
    public async Task RefreshAll_RequestFailure_StopsAndReportsStatusKeepingCache()
    {
        var store = new FakeProgressStore();
        store.LinkedIds.AddRange(Enumerable.Range(1, RetroAchievementsApi.MaxUserProgressBatchSize + 50));
        var client = new FakeClient
        {
            Respond = _ => RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>.Failure(
                RetroAchievementsRequestStatus.Offline),
        };
        var service = new RetroAchievementsProgressService(store, client);

        var summary = await service.RefreshAllAsync(Credentials, Token);

        Assert.Equal(RetroAchievementsRequestStatus.Offline, summary.Status);
        Assert.Single(client.Requests); // stopped after the first failing batch
        Assert.Empty(store.Saved); // nothing overwritten
    }

    [Fact]
    public void Clear_DelegatesToStore()
    {
        var store = new FakeProgressStore();
        var service = new RetroAchievementsProgressService(store, new FakeClient());

        service.Clear();

        Assert.True(store.Cleared);
    }

    private sealed class FakeProgressStore : IRetroAchievementsProgressStore
    {
        public List<int> LinkedIds { get; } = [];
        public List<RetroAchievementsGameProgress> Saved { get; } = [];
        public bool Cleared { get; private set; }
        public DateTimeOffset? LastSummaryRefreshAt { get; private set; }

        public IReadOnlyList<int> GetLinkedRetroAchievementsGameIds() => LinkedIds;
        public Dictionary<int, RetroAchievementsProgressSnapshot> Cached { get; } = [];
        public RetroAchievementsProgressSnapshot? GetProgress(int retroAchievementsGameId) => null;
        public IReadOnlyDictionary<int, RetroAchievementsProgressSnapshot> GetAllProgress() => Cached;
        public List<int> BatchSizes { get; } = [];
        public int SingleSaves { get; private set; }
        public void SaveProgress(RetroAchievementsGameProgress progress, DateTimeOffset refreshedAt)
        {
            SingleSaves++;
            Saved.Add(progress);
        }
        public void SaveProgress(
            IReadOnlyCollection<RetroAchievementsGameProgress> progress, DateTimeOffset refreshedAt)
        {
            BatchSizes.Add(progress.Count);
            Saved.AddRange(progress);
        }
        public DateTimeOffset? GetLastSummaryRefreshAt() => LastSummaryRefreshAt;
        public void SaveLastSummaryRefreshAt(DateTimeOffset refreshedAt) => LastSummaryRefreshAt = refreshedAt;
        public void ClearProgress()
        {
            Cleared = true;
            LastSummaryRefreshAt = null;
        }
    }

    private sealed class FakeClient : IRetroAchievementsClient
    {
        public List<IReadOnlyList<int>> Requests { get; } = [];

        public Func<IReadOnlyList<int>, RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>>
            Respond { get; set; } = batch => RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>
                .Success(batch.Select(id => new RetroAchievementsGameProgress(id, 40, 1, 0)).ToArray());

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
            RetroAchievementsCredentials credentials,
            IReadOnlyList<int> gameIds,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(gameIds);
            return Task.FromResult(Respond(gameIds));
        }

        public Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
            RetroAchievementsCredentials credentials,
            int consoleId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RetroAchievementsResponse<RetroAchievementsGameDetails>> GetGameDetailsAsync(
            RetroAchievementsCredentials credentials,
            int gameId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
