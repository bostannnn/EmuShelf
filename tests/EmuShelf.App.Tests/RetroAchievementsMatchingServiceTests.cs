using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public class RetroAchievementsMatchingServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly RetroAchievementsCredentials Credentials = new("Player", "KEY");

    [Fact]
    public async Task Match_RecordsRaGameIdAndHasAchievements()
    {
        var store = new FakeStore(Hashed(1, "playstation", "abc"));
        var catalogue = new FakeCatalogue(Fresh(("abc", 1234, 40)));
        var service = new RetroAchievementsMatchingService(store, catalogue);

        var summary = await service.MatchAsync(Credentials, forceRefreshCatalogues: false, Token);

        Assert.Equal(1, summary.Matched);
        var match = Assert.Single(store.Matches);
        Assert.Equal((1L, (int?)1234, (bool?)true), match);
    }

    [Fact]
    public async Task FreshMiss_RecordsNoAchievements()
    {
        var store = new FakeStore(Hashed(1, "playstation", "zzz"));
        var catalogue = new FakeCatalogue(Fresh(("abc", 1234, 40)));
        var service = new RetroAchievementsMatchingService(store, catalogue);

        var summary = await service.MatchAsync(Credentials, forceRefreshCatalogues: false, Token);

        Assert.Equal(1, summary.NoAchievements);
        Assert.Equal((1L, (int?)null, (bool?)false), Assert.Single(store.Matches));
    }

    [Fact]
    public async Task StaleMiss_LeavesUnresolved()
    {
        var store = new FakeStore(Hashed(1, "playstation", "zzz"));
        var catalogue = new FakeCatalogue(Stale(("abc", 1234, 40)));
        var service = new RetroAchievementsMatchingService(store, catalogue);

        var summary = await service.MatchAsync(Credentials, forceRefreshCatalogues: false, Token);

        Assert.Equal(1, summary.Unresolved);
        Assert.Empty(store.Matches); // a stale miss must never become a false "no"
    }

    [Fact]
    public async Task UnsupportedSystem_IsSkippedWithoutConsultingCatalogue()
    {
        var store = new FakeStore(Hashed(1, "playstation3", "abc"));
        var catalogue = new FakeCatalogue(Fresh(("abc", 1234, 40)));
        var service = new RetroAchievementsMatchingService(store, catalogue);

        var summary = await service.MatchAsync(Credentials, forceRefreshCatalogues: false, Token);

        Assert.Equal(1, summary.Unsupported);
        Assert.Equal(0, summary.Processed);
        Assert.Empty(store.Matches);
        Assert.Equal(0, catalogue.Calls);
    }

    [Theory]
    [InlineData("psp", 41)]
    [InlineData("megadrive", 1)]
    [InlineData("nds", 18)]
    [InlineData("gba", 5)]
    [InlineData("dreamcast", 40)]
    public async Task ExpansionSystem_UsesItsVerifiedConsoleCatalogue(
        string systemId,
        int expectedConsoleId)
    {
        var store = new FakeStore(Hashed(1, systemId, "abc"));
        var catalogue = new FakeCatalogue(Fresh(("abc", 1234, 40)));
        var service = new RetroAchievementsMatchingService(store, catalogue);

        var summary = await service.MatchAsync(Credentials, forceRefreshCatalogues: false, Token);

        Assert.Equal(1, summary.Matched);
        Assert.Equal([expectedConsoleId], catalogue.ConsoleIds);
        Assert.Equal((1L, (int?)1234, (bool?)true), Assert.Single(store.Matches));
    }

    [Fact]
    public async Task Match_SavesEachConsoleAsOneWrite()
    {
        // One commit per game stalled the Thor's shared storage for most of a minute on a ~900-game
        // library, so a console's outcomes are written together.
        var store = new FakeStore(
        [
            new RetroAchievementsHashedGame(1, "playstation", "abc"),
            new RetroAchievementsHashedGame(2, "playstation", "zzz"),
            new RetroAchievementsHashedGame(3, "playstation", "def"),
            new RetroAchievementsHashedGame(4, "psp", "abc"),
            new RetroAchievementsHashedGame(5, "psp", "yyy"),
        ]);
        var catalogue = new FakeCatalogue(Fresh(("abc", 1234, 40), ("def", 5678, 12)));
        var service = new RetroAchievementsMatchingService(store, catalogue);

        var summary = await service.MatchAsync(Credentials, forceRefreshCatalogues: false, Token);

        Assert.Equal(3, summary.Matched);
        Assert.Equal(2, summary.NoAchievements);
        Assert.Equal([3, 2], store.WriteSizes);
        Assert.Equal(
            [(1L, (int?)1234, (bool?)true), (2L, null, false), (3L, 5678, true), (4L, 1234, true), (5L, null, false)],
            store.Matches.OrderBy(match => match.GameId));
    }

    [Fact]
    public async Task NoCatalogueAvailable_LeavesUnresolved()
    {
        var store = new FakeStore(Hashed(1, "playstation", "abc"));
        var catalogue = new FakeCatalogue(null);
        var service = new RetroAchievementsMatchingService(store, catalogue);

        var summary = await service.MatchAsync(Credentials, forceRefreshCatalogues: false, Token);

        Assert.Equal(1, summary.Unresolved);
        Assert.Empty(store.Matches);
    }

    private static RetroAchievementsCatalogueLookup Fresh(params (string Hash, int Id, int Count)[] games) =>
        Build(isFresh: true, games);

    private static RetroAchievementsCatalogueLookup Stale(params (string Hash, int Id, int Count)[] games) =>
        Build(isFresh: false, games);

    private static RetroAchievementsCatalogueLookup Build(
        bool isFresh, (string Hash, int Id, int Count)[] games)
    {
        var byHash = games.ToDictionary(
            g => g.Hash,
            g => new RetroAchievementsCatalogueMatch(g.Id, "Game", g.Count),
            StringComparer.OrdinalIgnoreCase);
        return new RetroAchievementsCatalogueLookup(isFresh, byHash);
    }

    private static RetroAchievementsHashedGame[] Hashed(long id, string systemId, string hash) =>
        [new RetroAchievementsHashedGame(id, systemId, hash)];

    private sealed class FakeStore(IReadOnlyList<RetroAchievementsHashedGame> hashed)
        : IRetroAchievementsStore
    {
        public List<(long GameId, int? RaId, bool? Has)> Matches { get; } = [];

        public IReadOnlyList<RetroAchievementsHashedGame> GetHashedGames() => hashed;

        public List<int> WriteSizes { get; } = [];

        public void SaveCatalogueMatches(IReadOnlyCollection<RetroAchievementsCatalogueResolution> resolutions)
        {
            WriteSizes.Add(resolutions.Count);
            Matches.AddRange(resolutions.Select(r => (r.GameId, r.RetroAchievementsGameId, r.HasAchievements)));
        }

        public Game? GetGame(long gameId) => null;
        public RetroAchievementsGameLink? GetGameLink(long gameId) => null;
        public void SaveIdentification(long gameId, RetroAchievementsHashResult result) { }
    }

    private sealed class FakeCatalogue(RetroAchievementsCatalogueLookup? lookup)
        : IRetroAchievementsCatalogueCache
    {
        public int Calls { get; private set; }
        public List<int> ConsoleIds { get; } = [];

        public Task<RetroAchievementsCatalogueLookup?> GetLookupAsync(
            int consoleId,
            RetroAchievementsCredentials? credentials,
            bool forceRefresh,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            ConsoleIds.Add(consoleId);
            return Task.FromResult(lookup);
        }
    }
}
