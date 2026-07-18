using System.Globalization;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Achievements;

public sealed class SqliteRetroAchievementsStore
    : IRetroAchievementsStore,
      IRetroAchievementsProgressStore,
      IRetroAchievementsReadStore,
      IRetroAchievementsDetailsStore
{
    private const string GameColumns =
        "Id, SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin, IsAvailable, DateAdded";

    private const string LinkColumns =
        "GameId, Status, CanonicalHash, HashAlgorithmVersion, SourceFingerprint, " +
        "RetroAchievementsGameId, HasAchievements, LastAttemptUnixMilliseconds, LastError";

    private readonly LibraryDatabase _database;
    private readonly IRelativePathResolver _pathResolver;

    public SqliteRetroAchievementsStore(
        LibraryDatabase database,
        IRelativePathResolver pathResolver)
    {
        _database = database;
        _pathResolver = pathResolver;
    }

    public Game? GetGame(long gameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {GameColumns} FROM Games WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadGame(reader) : null;
    }

    public RetroAchievementsGameLink? GetGameLink(long gameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {LinkColumns} FROM RetroAchievementGameLinks WHERE GameId = $gameId;";
        command.Parameters.AddWithValue("$gameId", gameId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadLink(reader) : null;
    }

    public void SaveIdentification(long gameId, RetroAchievementsHashResult result)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO RetroAchievementGameLinks (
                GameId, Status, CanonicalHash, HashAlgorithmVersion,
                SourceFingerprint, RetroAchievementsGameId, HasAchievements,
                LastAttemptUnixMilliseconds, LastError)
            VALUES (
                $gameId, $status, $canonicalHash, $algorithmVersion,
                $fingerprint, NULL, NULL, $attemptedAt, $error)
            ON CONFLICT(GameId) DO UPDATE SET
                Status = excluded.Status,
                CanonicalHash = excluded.CanonicalHash,
                HashAlgorithmVersion = excluded.HashAlgorithmVersion,
                SourceFingerprint = excluded.SourceFingerprint,
                RetroAchievementsGameId = NULL,
                HasAchievements = NULL,
                LastAttemptUnixMilliseconds = excluded.LastAttemptUnixMilliseconds,
                LastError = excluded.LastError;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$status", (int)result.Status);
        command.Parameters.AddWithValue(
            "$canonicalHash",
            (object?)result.CanonicalHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$algorithmVersion", result.HashAlgorithmVersion);
        command.Parameters.AddWithValue("$fingerprint", result.SourceFingerprint);
        command.Parameters.AddWithValue("$attemptedAt", result.AttemptedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$error", (object?)result.Error ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<RetroAchievementsHashedGame> GetHashedGames()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT l.GameId, g.SystemId, l.CanonicalHash, g.Title
            FROM RetroAchievementGameLinks l
            JOIN Games g ON g.Id = l.GameId
            WHERE l.Status = $hashed AND l.CanonicalHash IS NOT NULL;
            """;
        command.Parameters.AddWithValue(
            "$hashed", (int)RetroAchievementsIdentificationStatus.Hashed);
        using var reader = command.ExecuteReader();
        var results = new List<RetroAchievementsHashedGame>();
        while (reader.Read())
        {
            results.Add(new RetroAchievementsHashedGame(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }
        return results;
    }

    public void SaveCatalogueMatch(
        long gameId,
        int? retroAchievementsGameId,
        bool? hasAchievements)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE RetroAchievementGameLinks
            SET RetroAchievementsGameId = $raId, HasAchievements = $has
            WHERE GameId = $gameId;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue(
            "$raId", (object?)retroAchievementsGameId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$has",
            hasAchievements is null ? DBNull.Value : hasAchievements.Value ? 1 : 0);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<int> GetLinkedRetroAchievementsGameIds()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT DISTINCT RetroAchievementsGameId
            FROM RetroAchievementGameLinks
            WHERE RetroAchievementsGameId IS NOT NULL;
            """;
        using var reader = command.ExecuteReader();
        var ids = new List<int>();
        while (reader.Read())
            ids.Add(reader.GetInt32(0));
        return ids;
    }

    public RetroAchievementsProgressSnapshot? GetProgress(int retroAchievementsGameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT AchievementCount, NumAwarded, NumAwardedHardcore, LastRefreshUnixMilliseconds
            FROM RetroAchievementProgress
            WHERE RetroAchievementsGameId = $id;
            """;
        command.Parameters.AddWithValue("$id", retroAchievementsGameId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new RetroAchievementsProgressSnapshot(
            new RetroAchievementsGameProgress(
                retroAchievementsGameId,
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)));
    }

    public void SaveProgress(RetroAchievementsGameProgress progress, DateTimeOffset refreshedAt)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO RetroAchievementProgress (
                RetroAchievementsGameId, AchievementCount, NumAwarded,
                NumAwardedHardcore, LastRefreshUnixMilliseconds)
            VALUES ($id, $count, $awarded, $hardcore, $refreshed)
            ON CONFLICT(RetroAchievementsGameId) DO UPDATE SET
                AchievementCount = excluded.AchievementCount,
                NumAwarded = excluded.NumAwarded,
                NumAwardedHardcore = excluded.NumAwardedHardcore,
                LastRefreshUnixMilliseconds = excluded.LastRefreshUnixMilliseconds;
            """;
        command.Parameters.AddWithValue("$id", progress.GameId);
        command.Parameters.AddWithValue("$count", progress.AchievementCount);
        command.Parameters.AddWithValue("$awarded", progress.NumAwarded);
        command.Parameters.AddWithValue("$hardcore", progress.NumAwardedHardcore);
        command.Parameters.AddWithValue("$refreshed", refreshedAt.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public void ClearProgress()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RetroAchievementProgress;";
        command.ExecuteNonQuery();
    }

    public RetroAchievementsDetailsSnapshot? GetDetails(int retroAchievementsGameId)
    {
        using var connection = _database.CreateConnection();
        using var header = connection.CreateCommand();
        header.CommandText =
            """
            SELECT Title, AchievementCount, NumAwarded, NumAwardedHardcore,
                   LastRefreshUnixMilliseconds
            FROM RetroAchievementGameDetails
            WHERE RetroAchievementsGameId = $id;
            """;
        header.Parameters.AddWithValue("$id", retroAchievementsGameId);
        using var headerReader = header.ExecuteReader();
        if (!headerReader.Read())
            return null;

        var title = headerReader.GetString(0);
        var achievementCount = headerReader.GetInt32(1);
        var numAwarded = headerReader.GetInt32(2);
        var numAwardedHardcore = headerReader.GetInt32(3);
        var lastRefreshedAt = DateTimeOffset.FromUnixTimeMilliseconds(headerReader.GetInt64(4));

        using var achievementsCommand = connection.CreateCommand();
        achievementsCommand.CommandText =
            """
            SELECT AchievementId, Title, Description, Points, BadgeName, DisplayOrder,
                   DateEarnedUnixMilliseconds, DateEarnedHardcoreUnixMilliseconds
            FROM RetroAchievementDetails
            WHERE RetroAchievementsGameId = $id
            ORDER BY DisplayOrder, AchievementId;
            """;
        achievementsCommand.Parameters.AddWithValue("$id", retroAchievementsGameId);
        using var achievementReader = achievementsCommand.ExecuteReader();
        var achievements = new List<RetroAchievementsAchievement>();
        while (achievementReader.Read())
        {
            achievements.Add(new RetroAchievementsAchievement(
                achievementReader.GetInt32(0),
                achievementReader.GetString(1),
                achievementReader.GetString(2),
                achievementReader.GetInt32(3),
                achievementReader.GetString(4),
                achievementReader.GetInt32(5),
                achievementReader.IsDBNull(6)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(achievementReader.GetInt64(6)),
                achievementReader.IsDBNull(7)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(achievementReader.GetInt64(7))));
        }

        return new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(
                retroAchievementsGameId,
                title,
                achievementCount,
                numAwarded,
                numAwardedHardcore,
                achievements),
            lastRefreshedAt);
    }

    public void SaveDetails(RetroAchievementsGameDetails details, DateTimeOffset refreshedAt)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using (var header = connection.CreateCommand())
        {
            header.Transaction = transaction;
            header.CommandText =
                """
                INSERT INTO RetroAchievementGameDetails (
                    RetroAchievementsGameId, Title, AchievementCount, NumAwarded,
                    NumAwardedHardcore, LastRefreshUnixMilliseconds)
                VALUES ($id, $title, $count, $awarded, $hardcore, $refreshed)
                ON CONFLICT(RetroAchievementsGameId) DO UPDATE SET
                    Title = excluded.Title,
                    AchievementCount = excluded.AchievementCount,
                    NumAwarded = excluded.NumAwarded,
                    NumAwardedHardcore = excluded.NumAwardedHardcore,
                    LastRefreshUnixMilliseconds = excluded.LastRefreshUnixMilliseconds;
                """;
            header.Parameters.AddWithValue("$id", details.GameId);
            header.Parameters.AddWithValue("$title", details.Title);
            header.Parameters.AddWithValue("$count", details.AchievementCount);
            header.Parameters.AddWithValue("$awarded", details.NumAwarded);
            header.Parameters.AddWithValue("$hardcore", details.NumAwardedHardcore);
            header.Parameters.AddWithValue("$refreshed", refreshedAt.ToUnixTimeMilliseconds());
            header.ExecuteNonQuery();
        }

        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText =
                "DELETE FROM RetroAchievementDetails WHERE RetroAchievementsGameId = $id;";
            delete.Parameters.AddWithValue("$id", details.GameId);
            delete.ExecuteNonQuery();
        }

        foreach (var achievement in details.Achievements)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO RetroAchievementDetails (
                    RetroAchievementsGameId, AchievementId, Title, Description, Points, BadgeName,
                    DisplayOrder, DateEarnedUnixMilliseconds, DateEarnedHardcoreUnixMilliseconds)
                VALUES (
                    $gameId, $achievementId, $title, $description, $points, $badgeName,
                    $displayOrder, $earned, $hardcore);
                """;
            insert.Parameters.AddWithValue("$gameId", details.GameId);
            insert.Parameters.AddWithValue("$achievementId", achievement.AchievementId);
            insert.Parameters.AddWithValue("$title", achievement.Title);
            insert.Parameters.AddWithValue("$description", achievement.Description);
            insert.Parameters.AddWithValue("$points", achievement.Points);
            insert.Parameters.AddWithValue("$badgeName", achievement.BadgeName);
            insert.Parameters.AddWithValue("$displayOrder", achievement.DisplayOrder);
            insert.Parameters.AddWithValue(
                "$earned",
                achievement.DateEarned is { } earned
                    ? earned.ToUnixTimeMilliseconds()
                    : DBNull.Value);
            insert.Parameters.AddWithValue(
                "$hardcore",
                achievement.DateEarnedHardcore is { } hardcore
                    ? hardcore.ToUnixTimeMilliseconds()
                    : DBNull.Value);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void ClearDetails()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RetroAchievementGameDetails;";
        command.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<long, RetroAchievementsGameLink> GetAllLinks()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {LinkColumns} FROM RetroAchievementGameLinks;";
        using var reader = command.ExecuteReader();
        var links = new Dictionary<long, RetroAchievementsGameLink>();
        while (reader.Read())
        {
            var link = ReadLink(reader);
            links[link.GameId] = link;
        }
        return links;
    }

    public IReadOnlyDictionary<int, RetroAchievementsProgressSnapshot> GetAllProgress()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT RetroAchievementsGameId, AchievementCount, NumAwarded,
                   NumAwardedHardcore, LastRefreshUnixMilliseconds
            FROM RetroAchievementProgress;
            """;
        using var reader = command.ExecuteReader();
        var progress = new Dictionary<int, RetroAchievementsProgressSnapshot>();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            progress[id] = new RetroAchievementsProgressSnapshot(
                new RetroAchievementsGameProgress(
                    id, reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)));
        }
        return progress;
    }

    private static RetroAchievementsGameLink ReadLink(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        (RetroAchievementsIdentificationStatus)reader.GetInt32(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.IsDBNull(6) ? null : reader.GetInt64(6) != 0,
        DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
        reader.IsDBNull(8) ? null : reader.GetString(8));

    private Game ReadGame(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        SystemId = reader.GetString(1),
        Path = _pathResolver.ToAbsolutePath(reader.GetString(2)),
        Title = reader.GetString(3),
        TitleOrigin = (GameTitleOrigin)reader.GetInt32(4),
        CoverPath = reader.IsDBNull(5) ? null : _pathResolver.ToAbsolutePath(reader.GetString(5)),
        CoverOrigin = (GameCoverOrigin)reader.GetInt32(6),
        IsAvailable = reader.GetInt64(7) != 0,
        DateAdded = DateTimeOffset.Parse(
            reader.GetString(8),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind),
    };
}
