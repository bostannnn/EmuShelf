using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Achievements;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Achievements;

public class SqliteRetroAchievementsStoreTests : TempAppDirectoryTestBase
{
    private readonly LibraryDatabase _database;
    private readonly GameLibrary _library;
    private readonly SqliteRetroAchievementsStore _store;

    public SqliteRetroAchievementsStoreTests()
    {
        AppPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(AppPaths);
        _database.Initialize();
        var resolver = new RelativePathResolver(AppPaths);
        _library = new GameLibrary(_database, resolver);
        _store = new SqliteRetroAchievementsStore(_database, resolver);
    }

    [Fact]
    public void SaveIdentification_RoundTripsAndClearsStaleCatalogueLink()
    {
        var gameId = AddGame();
        var attemptedAt = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var first = new RetroAchievementsHashResult(
            RetroAchievementsIdentificationStatus.Hashed,
            "0123456789abcdef0123456789abcdef",
            "algorithm-v1",
            "fingerprint-1",
            attemptedAt,
            null);
        _store.SaveIdentification(gameId, first);

        using (var connection = _database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE RetroAchievementGameLinks SET " +
                "RetroAchievementsGameId = 123, HasAchievements = 1 WHERE GameId = $id;";
            command.Parameters.AddWithValue("$id", gameId);
            command.ExecuteNonQuery();
        }

        var second = first with
        {
            CanonicalHash = "fedcba9876543210fedcba9876543210",
            SourceFingerprint = "fingerprint-2",
            AttemptedAt = attemptedAt.AddMinutes(1),
        };
        _store.SaveIdentification(gameId, second);
        var stored = _store.GetGameLink(gameId);

        Assert.NotNull(stored);
        Assert.Equal(second.CanonicalHash, stored.CanonicalHash);
        Assert.Equal(second.SourceFingerprint, stored.SourceFingerprint);
        Assert.Equal(second.AttemptedAt, stored.LastAttemptedAt);
        Assert.Null(stored.RetroAchievementsGameId);
        Assert.Null(stored.HasAchievements);
    }

    [Fact]
    public void RemovingGame_CascadesIdentificationOnly()
    {
        var gameId = AddGame();
        _store.SaveIdentification(
            gameId,
            new RetroAchievementsHashResult(
                RetroAchievementsIdentificationStatus.UnsupportedFormat,
                null,
                "algorithm-v1",
                "fingerprint",
                DateTimeOffset.UtcNow,
                "unsupported"));

        _library.RemoveGame(gameId);

        Assert.Null(_store.GetGameLink(gameId));
    }

    [Fact]
    public void GetHashedGames_ReturnsHashedOnly_AndSaveCatalogueMatchRoundTrips()
    {
        var gameId = AddGame();
        _store.SaveIdentification(
            gameId,
            new RetroAchievementsHashResult(
                RetroAchievementsIdentificationStatus.Hashed,
                "abc123",
                "algorithm-v1",
                "fingerprint",
                DateTimeOffset.UtcNow,
                null));

        var entry = Assert.Single(_store.GetHashedGames());
        Assert.Equal(gameId, entry.GameId);
        Assert.Equal("playstation2", entry.SystemId);
        Assert.Equal("abc123", entry.CanonicalHash);

        _store.SaveCatalogueMatch(gameId, retroAchievementsGameId: 1234, hasAchievements: true);
        var link = _store.GetGameLink(gameId);

        Assert.Equal(1234, link!.RetroAchievementsGameId);
        Assert.True(link.HasAchievements);
    }

    [Fact]
    public void GetHashedGames_ExcludesUnhashedResults()
    {
        var gameId = AddGame();
        _store.SaveIdentification(
            gameId,
            new RetroAchievementsHashResult(
                RetroAchievementsIdentificationStatus.UnsupportedFormat,
                null,
                "algorithm-v1",
                "fingerprint",
                DateTimeOffset.UtcNow,
                "unsupported"));

        Assert.Empty(_store.GetHashedGames());
    }

    [Fact]
    public void Progress_LinkedIdsSaveGetAndClear_RoundTrip()
    {
        var gameId = AddGame();
        _store.SaveIdentification(
            gameId,
            new RetroAchievementsHashResult(
                RetroAchievementsIdentificationStatus.Hashed,
                "abc123",
                "algorithm-v1",
                "fingerprint",
                DateTimeOffset.UtcNow,
                null));
        _store.SaveCatalogueMatch(gameId, retroAchievementsGameId: 1234, hasAchievements: true);

        Assert.Equal([1234], _store.GetLinkedRetroAchievementsGameIds());

        var refreshedAt = new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero);
        _store.SaveProgress(new RetroAchievementsGameProgress(1234, 40, 12, 3), refreshedAt);

        var snapshot = _store.GetProgress(1234);
        Assert.NotNull(snapshot);
        Assert.Equal(40, snapshot!.Progress.AchievementCount);
        Assert.Equal(12, snapshot.Progress.NumAwarded);
        Assert.Equal(3, snapshot.Progress.NumAwardedHardcore);
        Assert.Equal(refreshedAt, snapshot.LastRefreshedAt);

        // Re-saving overwrites, and clearing drops everything (account-scoped).
        _store.SaveProgress(new RetroAchievementsGameProgress(1234, 40, 20, 5), refreshedAt);
        Assert.Equal(20, _store.GetProgress(1234)!.Progress.NumAwarded);

        _store.ClearProgress();
        Assert.Null(_store.GetProgress(1234));
    }

    [Fact]
    public void GetAllLinks_AndGetAllProgress_ReturnStoredRowsForDisplay()
    {
        var gameId = AddGame();
        _store.SaveIdentification(
            gameId,
            new RetroAchievementsHashResult(
                RetroAchievementsIdentificationStatus.Hashed,
                "abc123",
                "algorithm-v1",
                "fingerprint",
                DateTimeOffset.UtcNow,
                null));
        _store.SaveCatalogueMatch(gameId, retroAchievementsGameId: 1234, hasAchievements: true);
        _store.SaveProgress(
            new RetroAchievementsGameProgress(1234, 40, 12, 3),
            new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero));

        var links = _store.GetAllLinks();
        var progress = _store.GetAllProgress();

        Assert.Equal(1234, links[gameId].RetroAchievementsGameId);
        Assert.True(links[gameId].HasAchievements);
        Assert.Equal(12, progress[1234].Progress.NumAwarded);
        Assert.Equal(40, progress[1234].Progress.AchievementCount);
        Assert.Equal(3, progress[1234].Progress.NumAwardedHardcore);
    }

    private long AddGame()
    {
        var path = Path.Combine(BaseDirectory, "game.iso");
        _library.AddGames([
            new Game
            {
                SystemId = "playstation2",
                Path = path,
                Title = "Test",
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        return _library.GetGames().Single().Id;
    }
}
