using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.Tests;

public class RetroAchievementsRefreshServiceTests
{
    private static readonly RetroAchievementsCredentials Credentials = new("Player", "KEY", "ULID-9");
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartupRefresh_OnlyRunsWhenTheCompleteSummarySyncIsStale()
    {
        var store = new MemoryProgressStore
        {
            LastSummaryRefreshAt = Now - TimeSpan.FromMinutes(14),
        };
        store.LinkedIds.Add(1234);
        var progress = new RecordingProgressService();
        var service = CreateService(store, progress, new RecordingDetailsService());

        var fresh = await service.RefreshSummaryAtStartupIfStaleAsync(TestContext.Current.CancellationToken);
        store.LastSummaryRefreshAt = Now - TimeSpan.FromMinutes(15) - TimeSpan.FromTicks(1);
        var stale = await service.RefreshSummaryAtStartupIfStaleAsync(TestContext.Current.CancellationToken);

        Assert.Null(fresh);
        Assert.NotNull(stale);
        Assert.Equal(1, progress.Calls);
    }

    [Fact]
    public async Task PostExitRefresh_WaitsOnceAndRequestsOnlyTheLaunchedGamesFullDetail()
    {
        var delays = new List<TimeSpan>();
        var details = new RecordingDetailsService();
        var service = CreateService(
            new MemoryProgressStore(),
            new RecordingProgressService(),
            details,
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            });

        var response = await service.RefreshAfterTrackedExitAsync(1234, TestContext.Current.CancellationToken);

        Assert.True(response!.IsSuccess);
        Assert.Equal([RetroAchievementsRefreshService.PostExitSettleDelay], delays);
        Assert.Equal([1234], details.RequestedGameIds);
        Assert.False(details.ManualRefresh);
    }

    [Fact]
    public async Task PostExitRefresh_DropsWorkWhenTheAccountChangesDuringSettle()
    {
        var account = new MutableAccount(Credentials);
        var details = new RecordingDetailsService();
        var service = new RetroAchievementsRefreshService(
            account,
            new MemoryProgressStore(),
            new RecordingProgressService(),
            details,
            new FixedTimeProvider(Now),
            (_, _) =>
            {
                account.Credentials = null;
                return Task.CompletedTask;
            });

        var response = await service.RefreshAfterTrackedExitAsync(1234, TestContext.Current.CancellationToken);

        Assert.Null(response);
        Assert.Empty(details.RequestedGameIds);
    }

    private static RetroAchievementsRefreshService CreateService(
        MemoryProgressStore store,
        RecordingProgressService progress,
        RecordingDetailsService details,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(
            new MutableAccount(Credentials),
            store,
            progress,
            details,
            new FixedTimeProvider(Now),
            delay);

    private sealed class MutableAccount(RetroAchievementsCredentials? credentials)
        : IRetroAchievementsAccountService
    {
        public RetroAchievementsCredentials? Credentials { get; set; } = credentials;
        public RetroAchievementsAccount? Account => Credentials is { } value
            ? new RetroAchievementsAccount(value.Username, value.UserUlid ?? value.Username)
            : null;
        public bool IsConnected => Credentials is not null;
        public RetroAchievementsCredentials? CurrentCredentials => Credentials;
        public Task<RetroAchievementsConnectionResult> ConnectAsync(
            string username,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RetroAchievementsConnectionResult.Connected);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MemoryProgressStore : IRetroAchievementsProgressStore
    {
        public List<int> LinkedIds { get; } = [];
        public DateTimeOffset? LastSummaryRefreshAt { get; set; }
        public IReadOnlyList<int> GetLinkedRetroAchievementsGameIds() => LinkedIds;
        public RetroAchievementsProgressSnapshot? GetProgress(int retroAchievementsGameId) => null;
        public void SaveProgress(RetroAchievementsGameProgress progress, DateTimeOffset refreshedAt) { }
        public DateTimeOffset? GetLastSummaryRefreshAt() => LastSummaryRefreshAt;
        public void SaveLastSummaryRefreshAt(DateTimeOffset refreshedAt) => LastSummaryRefreshAt = refreshedAt;
        public void ClearProgress() => LastSummaryRefreshAt = null;
    }

    private sealed class RecordingProgressService : IRetroAchievementsProgressService
    {
        public int Calls { get; private set; }
        public Task<RetroAchievementsProgressRefreshSummary> RefreshAllAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default,
            IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
        {
            Calls++;
            return Task.FromResult(new RetroAchievementsProgressRefreshSummary(
                1, 1, RetroAchievementsRequestStatus.Success));
        }
        public void Clear() { }
    }

    private sealed class RecordingDetailsService : IRetroAchievementsDetailsService
    {
        public List<int> RequestedGameIds { get; } = [];
        public bool ManualRefresh { get; private set; }
        public event Action<RetroAchievementsDetailsSnapshot>? DetailsRefreshed
        {
            add { }
            remove { }
        }
        public RetroAchievementsDetailsSnapshot? GetCached(int retroAchievementsGameId) => null;
        public Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshAsync(
            RetroAchievementsCredentials credentials,
            int retroAchievementsGameId,
            CancellationToken cancellationToken = default,
            bool manual = false)
        {
            RequestedGameIds.Add(retroAchievementsGameId);
            ManualRefresh = manual;
            return Task.FromResult(
                RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Success(
                    new RetroAchievementsDetailsSnapshot(
                        new RetroAchievementsGameDetails(retroAchievementsGameId, "Game", 0, 0, 0, []),
                        Now)));
        }
        public void Clear() { }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
