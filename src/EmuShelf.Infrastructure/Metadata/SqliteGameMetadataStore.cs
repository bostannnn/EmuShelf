using System.Globalization;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Metadata;

public sealed class SqliteGameMetadataStore : IGameMetadataStore
{
    private const string GameColumns =
        "Id, SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin, IsAvailable, DateAdded, " +
        "ExternalSourceId, ExternalSourceEntryId, ExternalSourcePresent";

    private readonly LibraryDatabase _database;
    private readonly IRelativePathResolver _pathResolver;

    public SqliteGameMetadataStore(
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

    public IReadOnlyList<Game> GetGamesMissingMetadata(string? systemId = null)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {GameColumns}
            FROM Games
            WHERE ($systemId IS NULL OR SystemId = $systemId)
              AND (
                  CoverPath IS NULL
                  OR (SystemId = 'playstation3' AND CoverOrigin = $downloaded)
                  OR TitleOrigin IN ($legacy, $filename)
                  OR (
                      TitleOrigin = $embedded
                      AND EXISTS (
                          SELECT 1
                          FROM GameMetadata
                          WHERE GameMetadata.GameId = Games.Id
                            AND GameMetadata.CanonicalTitle IS NOT NULL
                      )
                  )
              )
            ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$systemId", (object?)systemId ?? DBNull.Value);
        command.Parameters.AddWithValue("$legacy", (int)GameTitleOrigin.LegacyUnknown);
        command.Parameters.AddWithValue("$filename", (int)GameTitleOrigin.Filename);
        command.Parameters.AddWithValue("$embedded", (int)GameTitleOrigin.Embedded);
        command.Parameters.AddWithValue("$downloaded", (int)GameCoverOrigin.Downloaded);

        var games = new List<Game>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            games.Add(ReadGame(reader));
        return games;
    }

    public IReadOnlyList<GameIdentifier> GetIdentifiers(long gameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Kind, Value, Source, IsPrimary
            FROM GameIdentifiers
            WHERE GameId = $gameId
            ORDER BY IsPrimary DESC, rowid;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);

        var identifiers = new List<GameIdentifier>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            identifiers.Add(new GameIdentifier(
                (GameIdentifierKind)reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3) != 0));
        }
        return identifiers;
    }

    public void ReplaceIdentifiers(long gameId, IReadOnlyList<GameIdentifier> identifiers)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM GameIdentifiers WHERE GameId = $gameId;";
            delete.Parameters.AddWithValue("$gameId", gameId);
            delete.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT OR IGNORE INTO GameIdentifiers (GameId, Kind, Value, Source, IsPrimary)
            VALUES ($gameId, $kind, $value, $source, $isPrimary);
            """;
        insert.Parameters.AddWithValue("$gameId", gameId);
        var kind = insert.Parameters.Add("$kind", SqliteType.Integer);
        var value = insert.Parameters.Add("$value", SqliteType.Text);
        var source = insert.Parameters.Add("$source", SqliteType.Text);
        var isPrimary = insert.Parameters.Add("$isPrimary", SqliteType.Integer);

        foreach (var identifier in identifiers)
        {
            kind.Value = (int)identifier.Kind;
            value.Value = identifier.Value;
            source.Value = identifier.Source;
            isPrimary.Value = identifier.IsPrimary ? 1 : 0;
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public bool TryApplyCatalogTitle(long gameId, string canonicalTitle, string filenameTitle)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Games
            SET Title = $title, TitleOrigin = $catalog
            WHERE Id = $id
              AND (
                  TitleOrigin IN ($filename, $embedded, $catalog)
                  OR (TitleOrigin = $legacy AND Title = $filenameTitle)
              );
            """;
        command.Parameters.AddWithValue("$title", canonicalTitle);
        command.Parameters.AddWithValue("$id", gameId);
        command.Parameters.AddWithValue("$catalog", (int)GameTitleOrigin.Catalog);
        command.Parameters.AddWithValue("$filename", (int)GameTitleOrigin.Filename);
        command.Parameters.AddWithValue("$embedded", (int)GameTitleOrigin.Embedded);
        command.Parameters.AddWithValue("$legacy", (int)GameTitleOrigin.LegacyUnknown);
        command.Parameters.AddWithValue("$filenameTitle", filenameTitle);
        return command.ExecuteNonQuery() > 0;
    }

    public bool TryApplyDownloadedCover(
        long gameId,
        string coverPath,
        string providerId,
        string sourceUri)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var updateGame = connection.CreateCommand();
        updateGame.Transaction = transaction;
        updateGame.CommandText =
            """
            UPDATE Games
            SET CoverPath = $coverPath, CoverOrigin = $downloaded
            WHERE Id = $id AND (CoverPath IS NULL AND CoverOrigin = $none OR CoverOrigin = $downloaded);
            """;
        updateGame.Parameters.AddWithValue(
            "$coverPath",
            _pathResolver.ToStorablePath(coverPath));
        updateGame.Parameters.AddWithValue("$downloaded", (int)GameCoverOrigin.Downloaded);
        updateGame.Parameters.AddWithValue("$none", (int)GameCoverOrigin.None);
        updateGame.Parameters.AddWithValue("$id", gameId);
        if (updateGame.ExecuteNonQuery() == 0)
        {
            transaction.Rollback();
            return false;
        }

        using var updateMetadata = connection.CreateCommand();
        updateMetadata.Transaction = transaction;
        updateMetadata.CommandText =
            """
            INSERT INTO GameMetadata (GameId, CoverProviderId, CoverSourceUri)
            VALUES ($gameId, $providerId, $sourceUri)
            ON CONFLICT(GameId) DO UPDATE SET
                CoverProviderId = excluded.CoverProviderId,
                CoverSourceUri = excluded.CoverSourceUri;
            """;
        updateMetadata.Parameters.AddWithValue("$gameId", gameId);
        updateMetadata.Parameters.AddWithValue("$providerId", providerId);
        updateMetadata.Parameters.AddWithValue("$sourceUri", sourceUri);
        updateMetadata.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }

    public void RecordAttempt(GameMetadataAttempt attempt)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO GameMetadata (
                GameId, Status, CatalogId, CatalogEntryId, CanonicalTitle, Region,
                CoverProviderId, CoverSourceUri, LastAttemptUnixMilliseconds, LastError)
            VALUES (
                $gameId, $status, $catalogId, $catalogEntryId, $canonicalTitle, $region,
                $coverProviderId, $coverSourceUri, $attemptedAt, $error)
            ON CONFLICT(GameId) DO UPDATE SET
                Status = excluded.Status,
                CatalogId = excluded.CatalogId,
                CatalogEntryId = excluded.CatalogEntryId,
                CanonicalTitle = excluded.CanonicalTitle,
                Region = excluded.Region,
                CoverProviderId = COALESCE(excluded.CoverProviderId, GameMetadata.CoverProviderId),
                CoverSourceUri = COALESCE(excluded.CoverSourceUri, GameMetadata.CoverSourceUri),
                LastAttemptUnixMilliseconds = excluded.LastAttemptUnixMilliseconds,
                LastError = excluded.LastError;
            """;
        command.Parameters.AddWithValue("$gameId", attempt.GameId);
        command.Parameters.AddWithValue("$status", (int)attempt.Status);
        command.Parameters.AddWithValue("$catalogId", DbValue(attempt.Match?.CatalogId));
        command.Parameters.AddWithValue("$catalogEntryId", DbValue(attempt.Match?.CatalogEntryId));
        command.Parameters.AddWithValue("$canonicalTitle", DbValue(attempt.Match?.CanonicalTitle));
        command.Parameters.AddWithValue("$region", DbValue(attempt.Match?.Region));
        command.Parameters.AddWithValue("$coverProviderId", DbValue(attempt.CoverProviderId));
        command.Parameters.AddWithValue("$coverSourceUri", DbValue(attempt.CoverSourceUri));
        command.Parameters.AddWithValue("$attemptedAt", attempt.AttemptedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$error", DbValue(attempt.Error));
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
        ExternalSourceId = reader.IsDBNull(9) ? null : reader.GetString(9),
        ExternalSourceEntryId = reader.IsDBNull(10) ? null : reader.GetString(10),
        IsPresentInExternalSource = reader.IsDBNull(11) || reader.GetInt64(11) != 0,
    };

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;
}
