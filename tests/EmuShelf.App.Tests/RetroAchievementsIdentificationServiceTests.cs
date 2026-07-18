using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public class RetroAchievementsIdentificationServiceTests
{
    [Fact]
    public async Task IdentifyAsync_ReusesUnchangedTerminalIdentification()
    {
        var game = Game(1);
        var store = new MemoryStore(game);
        var hasher = new RecordingHasher("fingerprint-1");
        var service = new RetroAchievementsIdentificationService(store, hasher);

        var first = await service.IdentifyAsync(
            [game.Id],
            TestContext.Current.CancellationToken);
        var second = await service.IdentifyAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Hashed);
        Assert.Equal(1, second.Reused);
        Assert.Equal(1, hasher.IdentifyCount);
    }

    [Fact]
    public async Task IdentifyAsync_RehashesWhenDependencyFingerprintChanges()
    {
        var game = Game(1);
        var store = new MemoryStore(game);
        var hasher = new RecordingHasher("fingerprint-1");
        var service = new RetroAchievementsIdentificationService(store, hasher);
        await service.IdentifyAsync([game.Id], TestContext.Current.CancellationToken);

        hasher.Fingerprint = "fingerprint-2";
        var result = await service.IdentifyAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Hashed);
        Assert.Equal(2, hasher.IdentifyCount);
        Assert.Equal("fingerprint-2", store.Link!.SourceFingerprint);
    }

    [Fact]
    public async Task IdentifyAsync_RehashesWhenHashAlgorithmChanges()
    {
        var game = Game(1);
        var store = new MemoryStore(game);
        var hasher = new RecordingHasher("fingerprint-1");
        var service = new RetroAchievementsIdentificationService(store, hasher);
        await service.IdentifyAsync([game.Id], TestContext.Current.CancellationToken);

        hasher.AlgorithmVersion = "test-v2";
        var result = await service.IdentifyAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Hashed);
        Assert.Equal(2, hasher.IdentifyCount);
        Assert.Equal("test-v2", store.Link!.HashAlgorithmVersion);
    }

    [Fact]
    public async Task IdentifyAsync_ReusesACompatibleLegacyHashAlgorithm()
    {
        var game = Game(1);
        var store = new MemoryStore(game)
        {
            Link = new RetroAchievementsGameLink(
                game.Id,
                RetroAchievementsIdentificationStatus.Hashed,
                "0123456789abcdef0123456789abcdef",
                "legacy-v2",
                "fingerprint-1",
                null,
                null,
                DateTimeOffset.UtcNow,
                null),
        };
        var hasher = new RecordingHasher("fingerprint-1")
        {
            CompatibleVersions = new HashSet<string> { "test-v1", "legacy-v2" },
        };
        var service = new RetroAchievementsIdentificationService(store, hasher);

        var result = await service.IdentifyAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Reused);
        Assert.Equal(0, hasher.IdentifyCount);
    }

    [Fact]
    public async Task IdentifyAsync_RehashesACompatibleLegacyInvalidResult()
    {
        var game = Game(1);
        var store = new MemoryStore(game)
        {
            Link = new RetroAchievementsGameLink(
                game.Id,
                RetroAchievementsIdentificationStatus.InvalidMedia,
                null,
                "legacy-v2",
                "fingerprint-1",
                null,
                null,
                DateTimeOffset.UtcNow,
                "An older reader could not parse this image."),
        };
        var hasher = new RecordingHasher("fingerprint-1")
        {
            CompatibleVersions = new HashSet<string> { "test-v1", "legacy-v2" },
        };
        var service = new RetroAchievementsIdentificationService(store, hasher);

        var result = await service.IdentifyAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Hashed);
        Assert.Equal(1, hasher.IdentifyCount);
    }

    [Fact]
    public async Task IdentifyAsync_DoesNotPermanentlyReuseUnreadableAttempt()
    {
        var game = Game(1);
        var store = new MemoryStore(game)
        {
            Link = new RetroAchievementsGameLink(
                game.Id,
                RetroAchievementsIdentificationStatus.Unreadable,
                null,
                "test-v1",
                "fingerprint-1",
                null,
                null,
                DateTimeOffset.UtcNow,
                "temporarily unreadable"),
        };
        var hasher = new RecordingHasher("fingerprint-1");
        var service = new RetroAchievementsIdentificationService(store, hasher);

        var result = await service.IdentifyAsync(
            [game.Id],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Hashed);
        Assert.Equal(1, hasher.IdentifyCount);
    }

    private static Game Game(long id) => new()
    {
        Id = id,
        SystemId = "playstation",
        Path = "/games/test.cue",
        Title = "Test",
        DateAdded = DateTimeOffset.UtcNow,
    };

    private sealed class MemoryStore(Game game) : IRetroAchievementsStore
    {
        public RetroAchievementsGameLink? Link { get; set; }

        public Game? GetGame(long gameId) => gameId == game.Id ? game : null;

        public RetroAchievementsGameLink? GetGameLink(long gameId) =>
            gameId == game.Id ? Link : null;

        public void SaveIdentification(long gameId, RetroAchievementsHashResult result)
        {
            Link = new RetroAchievementsGameLink(
                gameId,
                result.Status,
                result.CanonicalHash,
                result.HashAlgorithmVersion,
                result.SourceFingerprint,
                null,
                null,
                result.AttemptedAt,
                result.Error);
        }

        public IReadOnlyList<RetroAchievementsHashedGame> GetHashedGames() => [];

        public void SaveCatalogueMatch(
            long gameId,
            int? retroAchievementsGameId,
            bool? hasAchievements)
        {
        }
    }

    private sealed class RecordingHasher(string fingerprint) : IRetroAchievementsGameHasher
    {
        public string Fingerprint { get; set; } = fingerprint;
        public int IdentifyCount { get; private set; }
        public string AlgorithmVersion { get; set; } = "test-v1";
        public IReadOnlySet<string>? CompatibleVersions { get; set; }

        public string GetAlgorithmVersion(Game game) => AlgorithmVersion;

        public bool IsAlgorithmVersionCompatible(Game game, string persistedVersion) =>
            CompatibleVersions?.Contains(persistedVersion) ?? persistedVersion == AlgorithmVersion;

        public RetroAchievementsSourceSnapshot Inspect(Game game) =>
            new(
                Fingerprint,
                true,
                RetroAchievementsIdentificationStatus.NotAttempted,
                null);

        public RetroAchievementsHashResult Identify(
            Game game,
            CancellationToken cancellationToken = default)
        {
            IdentifyCount++;
            return new RetroAchievementsHashResult(
                RetroAchievementsIdentificationStatus.Hashed,
                "0123456789abcdef0123456789abcdef",
                GetAlgorithmVersion(game),
                Fingerprint,
                DateTimeOffset.UtcNow,
                null);
        }
    }
}
