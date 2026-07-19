using EmuShelf.Core.Achievements;
using EmuShelf.Infrastructure.Achievements;

namespace EmuShelf.Infrastructure.Tests.Achievements;

public class RetroAchievementsCatalogueCacheTests : TempAppDirectoryTestBase
{
    private static readonly RetroAchievementsCredentials Credentials = new("Player", "KEY");
    private const int Console = 12;

    private string CachePath =>
        Path.Combine(AppPaths.CacheDirectory, "RetroAchievements", $"console-{Console}.json");

    [Fact]
    public async Task FreshFetch_WritesCacheAndReturnsMatch()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);

        var lookup = await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);

        Assert.NotNull(lookup);
        Assert.True(lookup!.IsFresh);
        Assert.Equal(1234, lookup.Find("abc")!.GameId);
        Assert.Equal(1, client.GameListCalls);
        Assert.True(File.Exists(CachePath));
    }

    [Fact]
    public async Task CorruptCache_WithinTtl_RefetchesInsteadOfServingGarbage()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);
        await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);
        await File.WriteAllTextAsync(CachePath, "{ not valid json", Token);

        var lookup = await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);

        Assert.NotNull(lookup);
        Assert.Equal(1234, lookup!.Find("abc")!.GameId);
        Assert.Equal(2, client.GameListCalls); // a corrupt within-TTL cache falls through to a refetch
    }

    [Fact]
    public async Task CorruptCache_NoCredentials_ReturnsNullInsteadOfThrowing()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);
        await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);
        await File.WriteAllTextAsync(CachePath, "{ not valid json", Token);

        var lookup = await cache.GetLookupAsync(Console, credentials: null, forceRefresh: false, Token);

        Assert.Null(lookup); // corrupt cache + no way to refetch degrades to "no catalogue", not a crash
    }

    [Fact]
    public async Task WithinTtl_SecondCall_DoesNotRefetch()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);

        await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);
        var second = await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);

        Assert.Equal(1, client.GameListCalls); // served from the within-TTL cache
        Assert.True(second!.IsFresh);
        Assert.Equal(1234, second.Find("abc")!.GameId);
    }

    [Fact]
    public async Task ForceRefresh_FetchesEvenWithFreshCache()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);
        await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);

        await cache.GetLookupAsync(Console, Credentials, forceRefresh: true, Token);

        Assert.Equal(2, client.GameListCalls);
    }

    [Fact]
    public async Task StaleCache_FetchFails_ServesStaleAsNotFresh()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);
        await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);
        File.SetLastWriteTimeUtc(CachePath, DateTime.UtcNow - TimeSpan.FromDays(8));
        client.Response = RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>
            .Failure(RetroAchievementsRequestStatus.Offline);

        var lookup = await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);

        Assert.NotNull(lookup);
        Assert.False(lookup!.IsFresh); // stale-while-offline
        Assert.Equal(1234, lookup.Find("abc")!.GameId);
        Assert.Equal(2, client.GameListCalls); // it did try to refresh
    }

    [Fact]
    public async Task NoCredentials_NoCache_ReturnsNull()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);

        var lookup = await cache.GetLookupAsync(Console, credentials: null, forceRefresh: false, Token);

        Assert.Null(lookup);
        Assert.Equal(0, client.GameListCalls);
    }

    [Fact]
    public async Task NoCredentials_WithCache_ServesCacheOffline()
    {
        var client = new FakeClient(Catalogue(("abc", 1234, "Spyro", 40)));
        var cache = new RetroAchievementsCatalogueCache(AppPaths, client);
        await cache.GetLookupAsync(Console, Credentials, forceRefresh: false, Token);

        var offline = await cache.GetLookupAsync(Console, credentials: null, forceRefresh: false, Token);

        Assert.NotNull(offline);
        Assert.Equal(1234, offline!.Find("abc")!.GameId);
        Assert.Equal(1, client.GameListCalls); // no extra call
    }

    private static CancellationToken Token => CancellationToken.None;

    private static IReadOnlyList<RetroAchievementsCatalogueGame> Catalogue(
        params (string Hash, int Id, string Title, int Count)[] games) =>
        games
            .Select(g => new RetroAchievementsCatalogueGame(g.Id, g.Title, g.Count, [g.Hash]))
            .ToArray();

    private sealed class FakeClient(IReadOnlyList<RetroAchievementsCatalogueGame> games)
        : IRetroAchievementsClient
    {
        public RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>> Response { get; set; }
            = RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>.Success(games);

        public int GameListCalls { get; private set; }

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
            RetroAchievementsCredentials credentials,
            int consoleId,
            CancellationToken cancellationToken = default)
        {
            GameListCalls++;
            return Task.FromResult(Response);
        }

        public Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
            RetroAchievementsCredentials credentials,
            IReadOnlyList<int> gameIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RetroAchievementsResponse<RetroAchievementsGameDetails>> GetGameDetailsAsync(
            RetroAchievementsCredentials credentials,
            int gameId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
