using System.Globalization;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Achievements;

public sealed class SqliteRetroAchievementsStore : IRetroAchievementsStore
{
    private const string GameColumns =
        "Id, SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin, IsAvailable, DateAdded";

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
            """
            SELECT GameId, Status, CanonicalHash, HashAlgorithmVersion,
                   SourceFingerprint, RetroAchievementsGameId, HasAchievements,
                   LastAttemptUnixMilliseconds, LastError
            FROM RetroAchievementGameLinks
            WHERE GameId = $gameId;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new RetroAchievementsGameLink(
            reader.GetInt64(0),
            (RetroAchievementsIdentificationStatus)reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6) != 0,
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
            reader.IsDBNull(8) ? null : reader.GetString(8));
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
            SELECT l.GameId, g.SystemId, l.CanonicalHash
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
                reader.GetString(2)));
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
