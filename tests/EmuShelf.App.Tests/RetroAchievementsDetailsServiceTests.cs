using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.Tests;

public class RetroAchievementsDetailsServiceTests
{
    private static readonly RetroAchievementsCredentials Credentials = new("Player", "KEY", "ULID-9");

    [Fact]
    public async Task Refresh_SavesDetailAndDerivedSummary()
    {
        var detailsStore = new MemoryDetailsStore();
        var progressStore = new MemoryProgressStore();
        var client = new FakeClient { Details = Details() };
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var service = new RetroAchievementsDetailsService(
            detailsStore,
            progressStore,
            client,
            new FixedTimeProvider(now));

        var response = await service.RefreshAsync(Credentials, 1234, TestContext.Current.CancellationToken);

        Assert.True(response.IsSuccess);
        Assert.Equal(now, response.Value!.LastRefreshedAt);
        Assert.Equal(1, response.Value.Details.UnlockedAchievements); // includes the softcore award
        Assert.Equal(1, progressStore.Saved!.NumAwarded);
        Assert.Equal(0, progressStore.Saved.NumAwardedHardcore);
        Assert.Equal(now, progressStore.RefreshedAt);
        Assert.NotNull(service.GetCached(1234));
    }

    [Fact]
    public async Task Clear_StopsAnOlderInFlightRequestFromRepopulatingAccountCache()
    {
        var detailsStore = new MemoryDetailsStore();
        var progressStore = new MemoryProgressStore();
        var client = new FakeClient { WaitForCompletion = true, Details = Details() };
        var service = new RetroAchievementsDetailsService(detailsStore, progressStore, client);

        var refresh = service.RefreshAsync(Credentials, 1234, TestContext.Current.CancellationToken);
        await client.Started.WaitAsync(TestContext.Current.CancellationToken);
        service.Clear();
        client.Complete();
        var response = await refresh;

        Assert.True(response.IsSuccess); // the still-open popup may use the returned result
        Assert.Null(detailsStore.Snapshot); // but disconnected account data is not persisted
        Assert.Null(progressStore.Saved);
        Assert.True(detailsStore.WasCleared);
    }

    [Fact]
    public async Task ConcurrentRefreshes_ShareOneClientRequest()
    {
        var client = new FakeClient { WaitForCompletion = true, Details = Details() };
        var service = new RetroAchievementsDetailsService(
            new MemoryDetailsStore(),
            new MemoryProgressStore(),
            client);

        var first = service.RefreshAsync(Credentials, 1234, TestContext.Current.CancellationToken);
        var second = service.RefreshAsync(Credentials, 1234, TestContext.Current.CancellationToken);
        await client.Started.WaitAsync(TestContext.Current.CancellationToken);
        client.Complete();
        await Task.WhenAll(first, second);

        Assert.Equal(1, client.Calls);
    }

    private static RetroAchievementsGameDetails Details() =>
        new(
            1234,
            "Spyro",
            2,
            1,
            0,
            [
                new RetroAchievementsAchievement(
                    7, "Softcore", "Earn it", 5, "000007", 1,
                    new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero), null),
                new RetroAchievementsAchievement(8, "Locked", "Later", 10, "000008", 2, null, null),
            ]);

    private sealed class MemoryDetailsStore : IRetroAchievementsDetailsStore
    {
        public RetroAchievementsDetailsSnapshot? Snapshot { get; private set; }
        public bool WasCleared { get; private set; }

        public RetroAchievementsDetailsSnapshot? GetDetails(int retroAchievementsGameId) => Snapshot;
        public void SaveDetails(RetroAchievementsGameDetails details, DateTimeOffset refreshedAt) =>
            Snapshot = new RetroAchievementsDetailsSnapshot(details, refreshedAt);
        public void ClearDetails()
        {
            WasCleared = true;
            Snapshot = null;
        }
    }

    private sealed class MemoryProgressStore : IRetroAchievementsProgressStore
    {
        public RetroAchievementsGameProgress? Saved { get; private set; }
        public DateTimeOffset? RefreshedAt { get; private set; }

        public IReadOnlyList<int> GetLinkedRetroAchievementsGameIds() => [];
        public RetroAchievementsProgressSnapshot? GetProgress(int retroAchievementsGameId) => null;
        public void SaveProgress(RetroAchievementsGameProgress progress, DateTimeOffset refreshedAt)
        {
            Saved = progress;
            RefreshedAt = refreshedAt;
        }
        public void ClearProgress() => Saved = null;
    }

    private sealed class FakeClient : IRetroAchievementsClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Started => _started.Task;
        public RetroAchievementsGameDetails Details { get; init; } = Details();
        public bool WaitForCompletion { get; init; }
        public int Calls { get; private set; }

        public async Task<RetroAchievementsResponse<RetroAchievementsGameDetails>> GetGameDetailsAsync(
            RetroAchievementsCredentials credentials,
            int gameId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            _started.TrySetResult();
            if (WaitForCompletion)
                await _complete.Task.WaitAsync(cancellationToken);
            return RetroAchievementsResponse<RetroAchievementsGameDetails>.Success(Details);
        }

        public void Complete() => _complete.TrySetResult();

        public Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
            RetroAchievementsCredentials credentials,
            int consoleId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
            RetroAchievementsCredentials credentials,
            IReadOnlyList<int> gameIds,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
