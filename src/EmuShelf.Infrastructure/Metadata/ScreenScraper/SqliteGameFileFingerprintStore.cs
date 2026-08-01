using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

public sealed class SqliteGameFileFingerprintStore : IGameFileFingerprintStore
{
    private readonly LibraryDatabase _database;
    private readonly IRelativePathResolver _pathResolver;

    public SqliteGameFileFingerprintStore(
        LibraryDatabase database,
        IRelativePathResolver pathResolver)
    {
        _database = database;
        _pathResolver = pathResolver;
    }

    public GameFileFingerprint? Get(long gameId, string providerId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SourcePath, Scope, FileSize, LastWriteUnixMilliseconds, Crc32, Md5, Sha1,
                   ComputedUnixMilliseconds
            FROM GameFileFingerprints
            WHERE GameId = $gameId AND ProviderId = $providerId;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$providerId", providerId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new GameFileFingerprint(
            gameId,
            providerId,
            _pathResolver.ToAbsolutePath(reader.GetString(0)),
            (ScreenScraperFingerprintScope)reader.GetInt32(1),
            reader.GetInt64(2),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)));
    }

    public void Upsert(GameFileFingerprint fingerprint)
    {
        if (fingerprint.GameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(fingerprint), "Game ID must be positive.");
        if (string.IsNullOrWhiteSpace(fingerprint.ProviderId))
            throw new ArgumentException("Provider ID cannot be empty.", nameof(fingerprint));

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO GameFileFingerprints (
                GameId, ProviderId, SourcePath, Scope, FileSize, LastWriteUnixMilliseconds,
                Crc32, Md5, Sha1, ComputedUnixMilliseconds)
            VALUES (
                $gameId, $providerId, $sourcePath, $scope, $fileSize, $lastWrite,
                $crc32, $md5, $sha1, $computed)
            ON CONFLICT (GameId, ProviderId) DO UPDATE SET
                SourcePath = excluded.SourcePath,
                Scope = excluded.Scope,
                FileSize = excluded.FileSize,
                LastWriteUnixMilliseconds = excluded.LastWriteUnixMilliseconds,
                Crc32 = excluded.Crc32,
                Md5 = excluded.Md5,
                Sha1 = excluded.Sha1,
                ComputedUnixMilliseconds = excluded.ComputedUnixMilliseconds;
            """;
        command.Parameters.AddWithValue("$gameId", fingerprint.GameId);
        command.Parameters.AddWithValue("$providerId", fingerprint.ProviderId.Trim());
        command.Parameters.AddWithValue("$sourcePath", _pathResolver.ToStorablePath(fingerprint.SourcePath));
        command.Parameters.AddWithValue("$scope", (int)fingerprint.Scope);
        command.Parameters.AddWithValue("$fileSize", fingerprint.FileSize);
        command.Parameters.AddWithValue("$lastWrite", fingerprint.LastWriteAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$crc32", fingerprint.Crc32);
        command.Parameters.AddWithValue("$md5", fingerprint.Md5);
        command.Parameters.AddWithValue("$sha1", fingerprint.Sha1);
        command.Parameters.AddWithValue("$computed", fingerprint.ComputedAt.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();
    }

    public void Remove(long gameId, string providerId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM GameFileFingerprints WHERE GameId = $gameId AND ProviderId = $providerId;";
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$providerId", providerId);
        command.ExecuteNonQuery();
    }
}
