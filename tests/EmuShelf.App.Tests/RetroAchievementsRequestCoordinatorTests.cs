using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.Tests;

public class RetroAchievementsRequestCoordinatorTests
{
    private static readonly RetroAchievementsCredentials Credentials = new("Player", "KEY", "ULID-9");
    private static readonly RetroAchievementsProfile Profile = new("Player", "ULID-9", 0, 0);

    [Fact]
    public async Task Requests_AreGloballySingleFlight_AndDuplicateWorkIsCoalesced()
    {
        var client = new BlockingClient();
        var coordinator = new RetroAchievementsRequestCoordinator(client);
        var automatic = coordinator.CreateClient(RetroAchievementsRequestMode.Automatic);
        var manual = coordinator.CreateClient(RetroAchievementsRequestMode.Manual);

        var first = automatic.GetGameDetailsAsync(Credentials, 1234, TestContext.Current.CancellationToken);
        await client.Started.WaitAsync(TestContext.Current.CancellationToken);
        var duplicate = manual.GetGameDetailsAsync(Credentials, 1234, TestContext.Current.CancellationToken);
        var different = manual.GetGameListAsync(Credentials, 12, TestContext.Current.CancellationToken);

        Assert.Equal(1, client.DetailsCalls);
        Assert.Equal(0, client.GameListCalls);

        client.Release();
        await Task.WhenAll(first, duplicate, different);

        Assert.Equal(1, client.DetailsCalls);
        Assert.Equal(1, client.GameListCalls);
    }

    [Fact]
    public async Task AutomaticRequests_AreSpacedByAtLeastOneSecond()
    {
        var delays = new List<TimeSpan>();
        var client = new BlockingClient();
        var coordinator = new RetroAchievementsRequestCoordinator(
            client,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            },
            jitter: () => 0);
        var automatic = coordinator.CreateClient(RetroAchievementsRequestMode.Automatic);

        await automatic.GetUserProfileAsync(Credentials, TestContext.Current.CancellationToken);
        await automatic.GetGameListAsync(Credentials, 12, TestContext.Current.CancellationToken);

        Assert.Equal([RetroAchievementsRequestCoordinator.MinimumAutomaticInterval], delays);
    }

    [Fact]
    public async Task RateLimit_UsesRetryAfterAsAGlobalCooldownWithoutRetrying()
    {
        var delays = new List<TimeSpan>();
        var client = new BlockingClient
        {
            ProfileResponse = RetroAchievementsResponse<RetroAchievementsProfile>.Failure(
                RetroAchievementsRequestStatus.RateLimited,
                TimeSpan.FromSeconds(9)),
        };
        var coordinator = new RetroAchievementsRequestCoordinator(
            client,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            },
            jitter: () => 0);
        var automatic = coordinator.CreateClient(RetroAchievementsRequestMode.Automatic);
        var manual = coordinator.CreateClient(RetroAchievementsRequestMode.Manual);

        var limited = await automatic.GetUserProfileAsync(Credentials, TestContext.Current.CancellationToken);
        await manual.GetGameListAsync(Credentials, 12, TestContext.Current.CancellationToken);

        Assert.Equal(RetroAchievementsRequestStatus.RateLimited, limited.Status);
        Assert.Equal(1, client.ProfileCalls); // the coordinator schedules no automatic retry
        Assert.Equal([TimeSpan.FromSeconds(9)], delays); // manual still honors Retry-After
    }

    [Fact]
    public async Task ServerError_UsesPositiveJitteredBackoffForLaterWork()
    {
        var delays = new List<TimeSpan>();
        var client = new BlockingClient
        {
            ProfileResponse = RetroAchievementsResponse<RetroAchievementsProfile>.Failure(
                RetroAchievementsRequestStatus.ServerError),
        };
        var coordinator = new RetroAchievementsRequestCoordinator(
            client,
            new FixedTimeProvider(DateTimeOffset.UnixEpoch),
            (duration, _) =>
            {
                delays.Add(duration);
                return Task.CompletedTask;
            },
            jitter: () => 0.5d);
        var automatic = coordinator.CreateClient(RetroAchievementsRequestMode.Automatic);

        await automatic.GetUserProfileAsync(Credentials, TestContext.Current.CancellationToken);
        await automatic.GetGameListAsync(Credentials, 12, TestContext.Current.CancellationToken);

        Assert.Equal([TimeSpan.FromSeconds(2.25)], delays);
    }

    private sealed class BlockingClient : IRetroAchievementsClient
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public int ProfileCalls { get; private set; }
        public int GameListCalls { get; private set; }
        public int DetailsCalls { get; private set; }
        public bool BlockDetails { get; set; } = true;
        public RetroAchievementsResponse<RetroAchievementsProfile> ProfileResponse { get; set; } =
            RetroAchievementsResponse<RetroAchievementsProfile>.Success(Profile);

        public Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            ProfileCalls++;
            return Task.FromResult(ProfileResponse);
        }

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
            RetroAchievementsCredentials credentials,
            int consoleId,
            CancellationToken cancellationToken = default)
        {
            GameListCalls++;
            return Task.FromResult(
                RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>.Success([]));
        }

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
            RetroAchievementsCredentials credentials,
            IReadOnlyList<int> gameIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>.Success([]));

        public async Task<RetroAchievementsResponse<RetroAchievementsGameDetails>> GetGameDetailsAsync(
            RetroAchievementsCredentials credentials,
            int gameId,
            CancellationToken cancellationToken = default)
        {
            DetailsCalls++;
            _started.TrySetResult();
            if (BlockDetails)
                await _release.Task.WaitAsync(cancellationToken);
            return RetroAchievementsResponse<RetroAchievementsGameDetails>.Success(
                new RetroAchievementsGameDetails(gameId, "Game", 0, 0, 0, []));
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
