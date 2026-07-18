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

        public IReadOnlyList<int> GetLinkedRetroAchievementsGameIds() => LinkedIds;
        public RetroAchievementsProgressSnapshot? GetProgress(int retroAchievementsGameId) => null;
        public void SaveProgress(RetroAchievementsGameProgress progress, DateTimeOffset refreshedAt) =>
            Saved.Add(progress);
        public void ClearProgress() => Cleared = true;
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
