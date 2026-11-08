using System.Globalization;
using EmuShelf.Core.Library;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Library;

/// <summary>
/// SQLite-backed <see cref="IGameLibrary"/>. Stores portable (relative-when-possible)
/// paths via <see cref="IRelativePathResolver"/> and hands back absolute paths.
/// </summary>
public sealed class GameLibrary : IGameLibrary
{
    private readonly LibraryDatabase _database;
    private readonly IRelativePathResolver _pathResolver;
    private readonly IDeviceIdentity _device;

    public GameLibrary(LibraryDatabase database, IRelativePathResolver pathResolver, IDeviceIdentity? device = null)
    {
        _database = database;
        _pathResolver = pathResolver;
        _device = device ?? FixedDeviceIdentity.Unknown;
    }

    public IReadOnlyList<Game> GetGames(string? systemId = null)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin,
                   ExternalSourceId, ExternalSourceEntryId, ExternalSourcePresent, IsAvailable, DateAdded,
                   LastPlayedUnixMilliseconds, PlaytimeSeconds, PlayCount
            FROM Games
            WHERE ($systemId IS NULL OR SystemId = $systemId)
            ORDER BY Title COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$systemId", (object?)systemId ?? DBNull.Value);

        var games = new List<Game>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            games.Add(ReadGame(reader));
        return games;
    }

    public IReadOnlySet<string> GetPopulatedSystemIds()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT SystemId FROM Games;";

        var systemIds = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            systemIds.Add(reader.GetString(0));
        return systemIds;
    }

    public IReadOnlyList<Game> GetRecentlyAddedGames(int limit)
    {
        if (limit <= 0)
            return [];

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin,
                   ExternalSourceId, ExternalSourceEntryId, ExternalSourcePresent, IsAvailable, DateAdded,
                   LastPlayedUnixMilliseconds, PlaytimeSeconds, PlayCount
            FROM Games
            ORDER BY DateAddedUnixMilliseconds DESC, Id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var games = new List<Game>(limit);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            games.Add(ReadGame(reader));
        return games;
    }

    public int AddGames(IEnumerable<Game> games) =>
        WriteGames(games, systemId: null, suppressedPaths: []).AddedCount;

    public GameImportResult ReconcileImport(
        string systemId,
        IEnumerable<Game> entries,
        IReadOnlyList<string> suppressedPaths) =>
        WriteGames(entries, systemId, suppressedPaths);

    public ExternalLibraryImportResult ReconcileExternalLibrary(
        ExternalLibrarySource source,
        IReadOnlyList<ExternalLibraryGameEntry> entries)
    {
        ValidateExternalSource(source, entries);

        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        ValidateExternalPathConflicts(connection, transaction, source, entries);
        UpsertExternalSource(connection, transaction, source);

        var addedIds = new List<long>();
        var updated = 0;
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in entries)
        {
            var existing = FindExternalEntry(connection, transaction, source.Id, entry.SourceEntryId);
            if (existing is { } game)
            {
                updated += UpdateExternalEntry(connection, transaction, game, entry);
                continue;
            }

            addedIds.Add(InsertExternalEntry(connection, transaction, source, entry, now));
        }

        var markedSourceMissing = MarkMissingSourceEntriesUnavailable(
            connection,
            transaction,
            source.Id,
            entries);
        if (addedIds.Count > 0)
            RelinkRemovedGameSessions(connection, transaction, addedIds);
        transaction.Commit();
        return new ExternalLibraryImportResult(addedIds, updated, markedSourceMissing);
    }

    private void UpsertExternalSource(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExternalLibrarySource source)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO ExternalLibrarySources (SourceId, SystemId, DisplayName, Location)
            VALUES ($sourceId, $systemId, $displayName, $location)
            ON CONFLICT(SourceId) DO UPDATE SET
                SystemId = excluded.SystemId,
                DisplayName = excluded.DisplayName,
                Location = excluded.Location;
            """;
        command.Parameters.AddWithValue("$sourceId", source.Id);
        command.Parameters.AddWithValue("$systemId", source.SystemId);
        command.Parameters.AddWithValue("$displayName", source.DisplayName);
        command.Parameters.AddWithValue("$location", _pathResolver.ToStorablePath(source.Location));
        command.ExecuteNonQuery();
    }

    private static int MarkMissingSourceEntriesUnavailable(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        IReadOnlyList<ExternalLibraryGameEntry> entries)
    {
        using (var createTable = connection.CreateCommand())
        {
            createTable.Transaction = transaction;
            createTable.CommandText =
                "CREATE TEMP TABLE IF NOT EXISTS CurrentExternalSourceEntries (SourceEntryId TEXT PRIMARY KEY);";
            createTable.ExecuteNonQuery();
        }

        using (var clearEntries = connection.CreateCommand())
        {
            clearEntries.Transaction = transaction;
            clearEntries.CommandText = "DELETE FROM CurrentExternalSourceEntries;";
            clearEntries.ExecuteNonQuery();
        }

        using (var recordEntry = connection.CreateCommand())
        {
            recordEntry.Transaction = transaction;
            recordEntry.CommandText =
                "INSERT INTO CurrentExternalSourceEntries (SourceEntryId) VALUES ($sourceEntryId);";
            var sourceEntryId = recordEntry.Parameters.Add("$sourceEntryId", SqliteType.Text);
            foreach (var entry in entries)
            {
                sourceEntryId.Value = entry.SourceEntryId;
                recordEntry.ExecuteNonQuery();
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE Games
            SET IsAvailable = 0,
                ExternalSourcePresent = 0
            WHERE ExternalSourceId = $sourceId
              AND ExternalSourcePresent IS NOT 0
              AND NOT EXISTS (
                  SELECT 1
                  FROM CurrentExternalSourceEntries
                  WHERE SourceEntryId = Games.ExternalSourceEntryId
              );
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);
        return command.ExecuteNonQuery();
    }

    private static ExistingExternalEntry? FindExternalEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sourceId,
        string sourceEntryId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT Id, TitleOrigin, Path
            FROM Games
            WHERE ExternalSourceId = $sourceId AND ExternalSourceEntryId = $sourceEntryId;
            """;
        command.Parameters.AddWithValue("$sourceId", sourceId);
        command.Parameters.AddWithValue("$sourceEntryId", sourceEntryId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ExistingExternalEntry(reader.GetInt64(0), (GameTitleOrigin)reader.GetInt32(1), reader.GetString(2))
            : null;
    }

    private int UpdateExternalEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExistingExternalEntry existing,
        ExternalLibraryGameEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE Games
            SET Path = $path,
                Title = CASE
                    WHEN TitleOrigin IN ($legacy, $filename, $embedded) THEN $title
                    ELSE Title
                END,
                TitleOrigin = CASE
                    WHEN TitleOrigin IN ($legacy, $filename, $embedded) THEN $titleOrigin
                    ELSE TitleOrigin
                END,
                ExternalSourcePresent = 1,
                IsAvailable = $isAvailable
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$path", _pathResolver.ToStorablePath(entry.Path));
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$legacy", (int)GameTitleOrigin.LegacyUnknown);
        command.Parameters.AddWithValue("$filename", (int)GameTitleOrigin.Filename);
        command.Parameters.AddWithValue("$embedded", (int)GameTitleOrigin.Embedded);
        command.Parameters.AddWithValue("$titleOrigin", (int)entry.TitleOrigin);
        command.Parameters.AddWithValue("$isAvailable", entry.IsAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$id", existing.Id);
        return command.ExecuteNonQuery();
    }

    private long InsertExternalEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExternalLibrarySource source,
        ExternalLibraryGameEntry entry,
        DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Games (
                SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin,
                ExternalSourceId, ExternalSourceEntryId, ExternalSourcePresent, IsAvailable, DateAdded,
                DateAddedUnixMilliseconds)
            VALUES (
                $systemId, $path, $title, $titleOrigin, NULL, $coverOrigin,
                $sourceId, $sourceEntryId, 1, $isAvailable, $dateAdded,
                $dateAddedUnixMilliseconds);
            """;
        command.Parameters.AddWithValue("$systemId", source.SystemId);
        command.Parameters.AddWithValue("$path", _pathResolver.ToStorablePath(entry.Path));
        command.Parameters.AddWithValue("$title", entry.Title);
        command.Parameters.AddWithValue("$titleOrigin", (int)entry.TitleOrigin);
        command.Parameters.AddWithValue("$coverOrigin", (int)GameCoverOrigin.None);
        command.Parameters.AddWithValue("$sourceId", source.Id);
        command.Parameters.AddWithValue("$sourceEntryId", entry.SourceEntryId);
        command.Parameters.AddWithValue("$isAvailable", entry.IsAvailable ? 1 : 0);
        command.Parameters.AddWithValue("$dateAdded", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$dateAddedUnixMilliseconds", now.ToUnixTimeMilliseconds());
        command.ExecuteNonQuery();

        using var id = connection.CreateCommand();
        id.Transaction = transaction;
        id.CommandText = "SELECT last_insert_rowid();";
        return (long)id.ExecuteScalar()!;
    }

    private static void ValidateExternalSource(
        ExternalLibrarySource source,
        IReadOnlyList<ExternalLibraryGameEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrWhiteSpace(source.Id) ||
            string.IsNullOrWhiteSpace(source.SystemId) ||
            string.IsNullOrWhiteSpace(source.DisplayName) ||
            string.IsNullOrWhiteSpace(source.Location))
        {
            throw new ArgumentException("An external source must include an id, system, display name, and location.");
        }

        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceEntryId) ||
                string.IsNullOrWhiteSpace(entry.Path) ||
                string.IsNullOrWhiteSpace(entry.Title))
            {
                throw new ArgumentException("Every external library entry needs an id, path, and title.");
            }

            if (!entryIds.Add(entry.SourceEntryId))
                throw new ArgumentException("An external library source returned duplicate entry ids.");

            if (!paths.Add(entry.Path))
                throw new ArgumentException("An external library source returned duplicate entry paths.");
        }
    }

    private void ValidateExternalPathConflicts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ExternalLibrarySource source,
        IReadOnlyList<ExternalLibraryGameEntry> entries)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT 1
            FROM Games
            WHERE Path = $path
              AND (
                  ExternalSourceId IS NULL OR ExternalSourceId <> $sourceId OR
                  ExternalSourceEntryId IS NULL OR ExternalSourceEntryId <> $sourceEntryId
              )
            LIMIT 1;
            """;
        var path = command.Parameters.Add("$path", SqliteType.Text);
        command.Parameters.AddWithValue("$sourceId", source.Id);
        var sourceEntryId = command.Parameters.Add("$sourceEntryId", SqliteType.Text);

        foreach (var entry in entries)
        {
            path.Value = _pathResolver.ToStorablePath(entry.Path);
            sourceEntryId.Value = entry.SourceEntryId;
            if (command.ExecuteScalar() is not null)
            {
                throw new ExternalLibrarySourceConflictException(
                    $"{source.DisplayName} entry '{entry.SourceEntryId}' points to a path already " +
                    "owned by a different EmuShelf game. Resolve the duplicate path and sync again.");
            }
        }
    }

    // Export filenames may change; the Steam app ID owns history and manual metadata.
    // A changed ID at the same path is rejected rather than attributing a new game to old history.
    private bool ReconcileSteamEntry(SqliteConnection connection, SqliteTransaction transaction, Game game)
    {
        var incoming = _pathResolver.ToStorablePath(game.Path);
        using var atPath = connection.CreateCommand();
        atPath.Transaction = transaction;
        atPath.CommandText = "SELECT Id, SystemId, ExternalSourceEntryId FROM Games WHERE Path = $path";
        atPath.Parameters.AddWithValue("$path", incoming);
        long? legacyId = null;
        using (var reader = atPath.ExecuteReader())
        {
            if (reader.Read())
            {
                if (reader.GetString(1) != "steam" || (!reader.IsDBNull(2) && reader.GetString(2) != game.ExternalSourceEntryId))
                    throw new ExternalLibrarySourceConflictException("A Steam export now identifies a different game. Remove the old library entry, then import the export again. Game files are untouched.");
                if (reader.IsDBNull(2)) legacyId = reader.GetInt64(0);
            }
        }
        var existing = FindExternalEntry(connection, transaction, "gamenative-steam", game.ExternalSourceEntryId!);
        if (existing is { } found)
        {
            if (legacyId is { } occupied && occupied != found.Id)
                throw new ExternalLibrarySourceConflictException(
                    "A legacy Steam entry already owns this export path. Resolve the duplicate library entry before moving its history.");
            // Multiple export roots may contain the same app. Keep the existing live export.
            var path = File.Exists(_pathResolver.ToAbsolutePath(found.Path)) ? found.Path : incoming;
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Games SET Path = $path, IsAvailable = 1 WHERE Id = $id";
            update.Parameters.AddWithValue("$path", path);
            update.Parameters.AddWithValue("$id", found.Id);
            update.ExecuteNonQuery();
            return true;
        }
        if (legacyId is { } legacy)
        {
            using var adopt = connection.CreateCommand();
            adopt.Transaction = transaction;
            adopt.CommandText = "UPDATE Games SET ExternalSourceId = 'gamenative-steam', ExternalSourceEntryId = $entry, IsAvailable = 1 WHERE Id = $id";
            adopt.Parameters.AddWithValue("$entry", game.ExternalSourceEntryId!);
            adopt.Parameters.AddWithValue("$id", legacy);
            adopt.ExecuteNonQuery();
            return true;
        }
        return false;
    }

    private GameImportResult WriteGames(
        IEnumerable<Game> games,
        string? systemId,
        IReadOnlyList<string> suppressedPaths)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();

        if (systemId is not null && suppressedPaths.Count > 0)
        {
            using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText =
                "DELETE FROM Games WHERE SystemId = $systemId AND Path = $path;";
            deleteCommand.Parameters.AddWithValue("$systemId", systemId);
            var suppressedPath = deleteCommand.Parameters.Add("$path", SqliteType.Text);

            using var keepSessions = connection.CreateCommand();
            keepSessions.Transaction = transaction;
            keepSessions.CommandText = KeepSessionsOfRemovedGamesSql(
                "SELECT Id FROM Games WHERE SystemId = $systemId AND Path = $path");
            keepSessions.Parameters.AddWithValue("$systemId", systemId);
            var keepPath = keepSessions.Parameters.Add("$path", SqliteType.Text);

            foreach (var pathToSuppress in suppressedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                suppressedPath.Value = _pathResolver.ToStorablePath(pathToSuppress);
                keepPath.Value = suppressedPath.Value;
                keepSessions.ExecuteNonQuery();
                deleteCommand.ExecuteNonQuery();
            }
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO Games (
                SystemId, Path, Title, TitleOrigin, CoverPath, CoverOrigin,
                IsAvailable, DateAdded, DateAddedUnixMilliseconds, ExternalSourceId, ExternalSourceEntryId)
            VALUES (
                $systemId, $path, $title, $titleOrigin, $coverPath, $coverOrigin,
                $isAvailable, $dateAdded, $dateAddedUnixMilliseconds, $source, $entry);
            """;
        var source = command.Parameters.Add("$source", SqliteType.Text);
        var entryId = command.Parameters.Add("$entry", SqliteType.Text);
        var systemIdParameter = command.Parameters.Add("$systemId", SqliteType.Text);
        var path = command.Parameters.Add("$path", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var titleOrigin = command.Parameters.Add("$titleOrigin", SqliteType.Integer);
        var coverPath = command.Parameters.Add("$coverPath", SqliteType.Text);
        var coverOrigin = command.Parameters.Add("$coverOrigin", SqliteType.Integer);
        var isAvailable = command.Parameters.Add("$isAvailable", SqliteType.Integer);
        var dateAdded = command.Parameters.Add("$dateAdded", SqliteType.Text);
        var dateAddedUnixMilliseconds = command.Parameters.Add(
            "$dateAddedUnixMilliseconds",
            SqliteType.Integer);

        using var insertedIdCommand = connection.CreateCommand();
        insertedIdCommand.Transaction = transaction;
        insertedIdCommand.CommandText = "SELECT last_insert_rowid();";

        using var repairFilenameTitleCommand = connection.CreateCommand();
        repairFilenameTitleCommand.Transaction = transaction;
        repairFilenameTitleCommand.CommandText =
            """
            UPDATE Games
            SET Title = $title, TitleOrigin = $filename
            WHERE SystemId = $systemId AND Path = $path
              AND TitleOrigin = $embedded AND $incomingOrigin = $filename;
            """;
        var repairSystemId = repairFilenameTitleCommand.Parameters.Add("$systemId", SqliteType.Text);
        var repairPath = repairFilenameTitleCommand.Parameters.Add("$path", SqliteType.Text);
        var repairTitle = repairFilenameTitleCommand.Parameters.Add("$title", SqliteType.Text);
        repairFilenameTitleCommand.Parameters.AddWithValue("$filename", (int)GameTitleOrigin.Filename);
        repairFilenameTitleCommand.Parameters.AddWithValue("$embedded", (int)GameTitleOrigin.Embedded);
        var repairIncomingOrigin = repairFilenameTitleCommand.Parameters.Add(
            "$incomingOrigin",
            SqliteType.Integer);

        var addedIds = new List<long>();
        foreach (var game in games)
        {
            if (game.ExternalSourceId == "gamenative-steam" && game.ExternalSourceEntryId is not null &&
                ReconcileSteamEntry(connection, transaction, game)) continue;
            source.Value = (object?)game.ExternalSourceId ?? DBNull.Value;
            entryId.Value = (object?)game.ExternalSourceEntryId ?? DBNull.Value;
            systemIdParameter.Value = game.SystemId;
            path.Value = _pathResolver.ToStorablePath(game.Path);
            title.Value = game.Title;
            titleOrigin.Value = (int)game.TitleOrigin;
            coverPath.Value = game.CoverPath is null
                ? DBNull.Value
                : _pathResolver.ToStorablePath(game.CoverPath);
            // Art discovered while scanning/importing keeps its origin (None) so a later scrape may
            // replace it. Only a cover the user hand-picks (UpdateCoverPath) is stamped User.
            coverOrigin.Value = (int)game.CoverOrigin;
            isAvailable.Value = game.IsAvailable ? 1 : 0;
            dateAdded.Value = game.DateAdded.ToString("O", CultureInfo.InvariantCulture);
            dateAddedUnixMilliseconds.Value = game.DateAdded.ToUnixTimeMilliseconds();
            repairSystemId.Value = systemIdParameter.Value;
            repairPath.Value = path.Value;
            repairTitle.Value = title.Value;
            repairIncomingOrigin.Value = titleOrigin.Value;
            repairFilenameTitleCommand.ExecuteNonQuery();
            if (command.ExecuteNonQuery() > 0)
                addedIds.Add((long)insertedIdCommand.ExecuteScalar()!);
        }

        if (addedIds.Count > 0)
            RelinkRemovedGameSessions(connection, transaction, addedIds);
        transaction.Commit();
        return new GameImportResult(addedIds);
    }

    public void SetAvailability(long gameId, bool isAvailable) =>
        SetAvailabilities([new GameAvailabilityUpdate(gameId, isAvailable)]);

    public void SetAvailabilities(IReadOnlyList<GameAvailabilityUpdate> updates)
    {
        if (updates.Count == 0)
            return;

        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Games SET IsAvailable = $isAvailable WHERE Id = $id;";
        var availability = command.Parameters.Add("$isAvailable", SqliteType.Integer);
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        foreach (var update in updates)
        {
            availability.Value = update.IsAvailable ? 1 : 0;
            id.Value = update.GameId;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public long? RecordLaunchStarted(long gameId, DateTimeOffset startedAt)
    {
        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // The session row and the cached totals move together in one transaction: last-played drives
        // Recently Played, the count is the "times launched" total, and the open row is this sitting in the
        // play log. All are stamped at start (not exit), so a session ended by an app kill still counts.
        command.CommandText =
            """
            UPDATE Games SET LastPlayedUnixMilliseconds = $started, PlayCount = PlayCount + 1 WHERE Id = $id;
            INSERT INTO PlaySessions (GameId, GameTitle, SystemId, StartedUnixMilliseconds, DeviceId)
            SELECT Id, Title, SystemId, $started, $device FROM Games WHERE Id = $id;
            SELECT CASE WHEN changes() > 0 THEN last_insert_rowid() END;
            """;
        command.Parameters.AddWithValue("$started", startedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$id", gameId);
        command.Parameters.AddWithValue("$device", _device.DeviceId);
        var sessionId = command.ExecuteScalar();
        transaction.Commit();
        return sessionId is long id ? id : null;
    }

    public bool CompletePlaySession(long sessionId, TimeSpan duration, PlaySessionMeasurement measurement)
    {
        // Whole seconds only; a negative span (clock skew) is a zero-length session, not a deduction.
        var seconds = Math.Max(0, (long)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));

        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var close = connection.CreateCommand();
        close.Transaction = transaction;
        // Only an open session closes, so completing twice (a retried Android return) never adds twice.
        close.CommandText =
            """
            UPDATE PlaySessions SET DurationSeconds = $seconds, Measurement = $measurement
            WHERE Id = $id AND DurationSeconds IS NULL;
            """;
        close.Parameters.AddWithValue("$seconds", seconds);
        close.Parameters.AddWithValue("$measurement", (int)measurement);
        close.Parameters.AddWithValue("$id", sessionId);
        if (close.ExecuteNonQuery() == 0)
            return false;

        if (seconds > 0)
        {
            // A session whose game was removed mid-play updates no row here; its time joins the totals if
            // the game is re-added (RelinkRemovedGameSessions).
            using var accrue = connection.CreateCommand();
            accrue.Transaction = transaction;
            accrue.CommandText =
                """
                UPDATE Games SET PlaytimeSeconds = PlaytimeSeconds + $seconds
                WHERE Id = (SELECT GameId FROM PlaySessions WHERE Id = $id AND GamePath IS NULL);
                """;
            accrue.Parameters.AddWithValue("$seconds", seconds);
            accrue.Parameters.AddWithValue("$id", sessionId);
            accrue.ExecuteNonQuery();
        }

        transaction.Commit();
        return true;
    }

    public long? FindOpenPlaySession(long gameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id FROM PlaySessions
            WHERE GameId = $id AND GamePath IS NULL AND DurationSeconds IS NULL
            ORDER BY StartedUnixMilliseconds DESC, Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", gameId);
        return command.ExecuteScalar() is long id ? id : null;
    }

    public IReadOnlyList<PlaySession> GetPlaySessions(long gameId)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, GameId, GameTitle, SystemId, StartedUnixMilliseconds, DurationSeconds, Measurement, DeviceId
            FROM PlaySessions
            WHERE GameId = $id AND GamePath IS NULL
            ORDER BY StartedUnixMilliseconds DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$id", gameId);
        using var reader = command.ExecuteReader();
        var sessions = new List<PlaySession>();
        while (reader.Read())
        {
            sessions.Add(new PlaySession(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)),
                reader.IsDBNull(5) ? null : TimeSpan.FromSeconds(reader.GetInt64(5)),
                reader.IsDBNull(6) ? null : (PlaySessionMeasurement)reader.GetInt32(6),
                reader.GetString(7)));
        }
        return sessions;
    }

    // Marks the sessions of games about to be deleted as belonging to a removed game: the game's stored path
    // and latest title are stamped on them, so the log keeps reading and re-adding the same file relinks
    // them. gameIdSelector is SQL selecting the ids of the games being removed.
    private static string KeepSessionsOfRemovedGamesSql(string gameIdSelector) =>
        $"""
        UPDATE PlaySessions
        SET GamePath = (SELECT Path FROM Games WHERE Games.Id = PlaySessions.GameId),
            GameTitle = COALESCE((SELECT Title FROM Games WHERE Games.Id = PlaySessions.GameId), GameTitle)
        WHERE GamePath IS NULL AND GameId IN ({gameIdSelector})
          AND EXISTS (SELECT 1 FROM Games WHERE Games.Id = PlaySessions.GameId);
        """;

    // A game added at the path of a removed one gets that game's sessions back, and their count, time and
    // latest start join its totals. Runs inside the import transaction, for the rows this import inserted
    // only, so a game that merely moved to that path (a folder relocation) never takes another game's
    // history. Paths compare in their stored form, case-insensitively, the same rule as Games.Path's unique
    // index. Play from before the log lived only on the removed row, so it is not recovered.
    private static void RelinkRemovedGameSessions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<long> addedIds)
    {
        using (var any = connection.CreateCommand())
        {
            any.Transaction = transaction;
            any.CommandText = "SELECT EXISTS (SELECT 1 FROM PlaySessions WHERE GamePath IS NOT NULL);";
            if ((long)any.ExecuteScalar()! == 0)
                return;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE Games SET
                PlayCount = PlayCount +
                    (SELECT COUNT(*) FROM PlaySessions s WHERE s.GamePath = Games.Path),
                PlaytimeSeconds = PlaytimeSeconds +
                    (SELECT COALESCE(SUM(s.DurationSeconds), 0) FROM PlaySessions s WHERE s.GamePath = Games.Path),
                LastPlayedUnixMilliseconds = max(
                    COALESCE(LastPlayedUnixMilliseconds, 0),
                    (SELECT MAX(s.StartedUnixMilliseconds) FROM PlaySessions s WHERE s.GamePath = Games.Path))
            WHERE Id = $id AND Path IN (SELECT GamePath FROM PlaySessions WHERE GamePath IS NOT NULL);

            UPDATE PlaySessions
            SET GameId = $id, GamePath = NULL
            WHERE GamePath IS NOT NULL AND GamePath = (SELECT Path FROM Games WHERE Id = $id);
            """;
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        foreach (var addedId in addedIds)
        {
            id.Value = addedId;
            command.ExecuteNonQuery();
        }
    }

    public void UpdateTitle(long gameId, string title)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Games SET Title = $title, TitleOrigin = $origin WHERE Id = $id;";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$origin", (int)GameTitleOrigin.User);
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    public void UpdateCoverPath(long gameId, string? coverPath)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE Games SET CoverPath = $coverPath, CoverOrigin = $origin WHERE Id = $id;";
        command.Parameters.AddWithValue(
            "$coverPath",
            coverPath is null ? DBNull.Value : _pathResolver.ToStorablePath(coverPath));
        command.Parameters.AddWithValue(
            "$origin",
            coverPath is null ? (int)GameCoverOrigin.None : (int)GameCoverOrigin.User);
        command.Parameters.AddWithValue("$id", gameId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, long> GetDiscSelections()
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TitleSetKey, GameId
            FROM GameDiscSelections;
            """;

        var selections = new Dictionary<string, long>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            selections.Add(reader.GetString(0), reader.GetInt64(1));
        return selections;
    }

    public void SetDiscSelection(string titleSetKey, long gameId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSetKey);

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO GameDiscSelections (TitleSetKey, GameId)
            VALUES ($titleSetKey, $gameId)
            ON CONFLICT(TitleSetKey) DO UPDATE SET GameId = excluded.GameId;
            """;
        command.Parameters.AddWithValue("$titleSetKey", titleSetKey);
        command.Parameters.AddWithValue("$gameId", gameId);
        command.ExecuteNonQuery();
    }

    public void RemoveGame(long gameId)
    {
        RemoveGames([gameId]);
    }

    public void RemoveGames(IReadOnlyList<long> gameIds)
    {
        if (gameIds.Count == 0)
            return;

        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var keepSessions = connection.CreateCommand();
        keepSessions.Transaction = transaction;
        keepSessions.CommandText = KeepSessionsOfRemovedGamesSql("$id");
        var keepId = keepSessions.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM Games WHERE Id = $id;";
        var id = command.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Integer);
        foreach (var gameId in gameIds.Distinct())
        {
            // The play log outlives the library record (only EmuShelf's own rows change; no file is touched).
            keepId.Value = gameId;
            keepSessions.ExecuteNonQuery();
            id.Value = gameId;
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<LibraryFolder> GetLibraryFolders(string? systemId = null)
    {
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, SystemId, Path
            FROM LibraryFolders
            WHERE ($systemId IS NULL OR SystemId = $systemId)
            ORDER BY Id;
            """;
        command.Parameters.AddWithValue("$systemId", (object?)systemId ?? DBNull.Value);

        var folders = new List<LibraryFolder>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            folders.Add(new LibraryFolder
            {
                Id = reader.GetInt64(0),
                SystemId = reader.GetString(1),
                Path = _pathResolver.ToAbsolutePath(reader.GetString(2)),
            });
        }
        return folders;
    }

    public void AddLibraryFolder(string systemId, string folderPath)
    {
        var storable = _pathResolver.ToStorablePath(folderPath);

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        // Skip if this system already tracks this exact folder.
        command.CommandText =
            """
            INSERT INTO LibraryFolders (SystemId, Path)
            SELECT $systemId, $path
            WHERE NOT EXISTS (
                SELECT 1 FROM LibraryFolders WHERE SystemId = $systemId AND Path = $path
            );
            """;
        command.Parameters.AddWithValue("$systemId", systemId);
        command.Parameters.AddWithValue("$path", storable);
        command.ExecuteNonQuery();
    }

    public LibraryFolderChangeResult ReplaceLibraryFolder(
        long folderId,
        string systemId,
        string replacementPath,
        IReadOnlyDictionary<long, string> verifiedGamePaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(replacementPath);
        ArgumentNullException.ThrowIfNull(verifiedGamePaths);

        var replacementRoot = Path.GetFullPath(replacementPath);
        var replacementStorable = _pathResolver.ToStorablePath(replacementRoot);

        using var connection = _database.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var originalRoot = GetFolderPath(connection, transaction, folderId, systemId);

        using (var duplicate = connection.CreateCommand())
        {
            duplicate.Transaction = transaction;
            duplicate.CommandText =
                "SELECT 1 FROM LibraryFolders WHERE SystemId = $systemId AND Path = $path AND Id <> $id LIMIT 1;";
            duplicate.Parameters.AddWithValue("$systemId", systemId);
            duplicate.Parameters.AddWithValue("$path", replacementStorable);
            duplicate.Parameters.AddWithValue("$id", folderId);
            if (duplicate.ExecuteScalar() is not null)
                throw new InvalidOperationException("That folder is already remembered for this platform.");
        }

        var rebases = new List<(long Id, string Path)>();
        using (var games = connection.CreateCommand())
        {
            games.Transaction = transaction;
            games.CommandText = "SELECT Id, Path FROM Games WHERE SystemId = $systemId;";
            games.Parameters.AddWithValue("$systemId", systemId);
            using var reader = games.ExecuteReader();
            while (reader.Read())
            {
                var currentPath = _pathResolver.ToAbsolutePath(reader.GetString(1));
                var gameId = reader.GetInt64(0);
                if (!TryRelativePath(originalRoot, currentPath, out _) ||
                    !verifiedGamePaths.TryGetValue(gameId, out var verifiedPath))
                    continue;
                var candidate = Path.GetFullPath(verifiedPath);
                if (!TryRelativePath(replacementRoot, candidate, out _))
                    throw new InvalidOperationException("A verified replacement path is outside the replacement folder.");
                rebases.Add((gameId, candidate));
            }
        }

        using (var conflict = connection.CreateCommand())
        {
            conflict.Transaction = transaction;
            conflict.CommandText = "SELECT Id FROM Games WHERE Path = $path AND Id <> $id LIMIT 1;";
            var path = conflict.Parameters.Add("$path", SqliteType.Text);
            var id = conflict.Parameters.Add("$id", SqliteType.Integer);
            foreach (var rebase in rebases)
            {
                path.Value = _pathResolver.ToStorablePath(rebase.Path);
                id.Value = rebase.Id;
                if (conflict.ExecuteScalar() is not null)
                {
                    throw new InvalidOperationException(
                        $"Cannot change the folder because '{rebase.Path}' is already owned by another library entry.");
                }
            }
        }

        using (var updateGame = connection.CreateCommand())
        {
            updateGame.Transaction = transaction;
            updateGame.CommandText = "UPDATE Games SET Path = $path, IsAvailable = 1 WHERE Id = $id;";
            var path = updateGame.Parameters.Add("$path", SqliteType.Text);
            var id = updateGame.Parameters.Add("$id", SqliteType.Integer);
            foreach (var rebase in rebases)
            {
                path.Value = _pathResolver.ToStorablePath(rebase.Path);
                id.Value = rebase.Id;
                updateGame.ExecuteNonQuery();
            }
        }

        using (var updateFolder = connection.CreateCommand())
        {
            updateFolder.Transaction = transaction;
            updateFolder.CommandText =
                "UPDATE LibraryFolders SET Path = $path WHERE Id = $id AND SystemId = $systemId;";
            updateFolder.Parameters.AddWithValue("$path", replacementStorable);
            updateFolder.Parameters.AddWithValue("$id", folderId);
            updateFolder.Parameters.AddWithValue("$systemId", systemId);
            updateFolder.ExecuteNonQuery();
        }

        transaction.Commit();
        return new LibraryFolderChangeResult(rebases.Count);
    }

    public void RemoveLibraryFolder(long folderId, string systemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM LibraryFolders WHERE Id = $id AND SystemId = $systemId;";
        command.Parameters.AddWithValue("$id", folderId);
        command.Parameters.AddWithValue("$systemId", systemId);
        command.ExecuteNonQuery();
    }

    private string GetFolderPath(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long folderId,
        string systemId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Path FROM LibraryFolders WHERE Id = $id AND SystemId = $systemId;";
        command.Parameters.AddWithValue("$id", folderId);
        command.Parameters.AddWithValue("$systemId", systemId);
        return command.ExecuteScalar() is string path
            ? _pathResolver.ToAbsolutePath(path)
            : throw new InvalidOperationException("That remembered folder no longer exists.");
    }

    private static bool TryRelativePath(string root, string path, out string relativePath)
    {
        relativePath = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relativePath != "." &&
            !Path.IsPathRooted(relativePath) &&
            relativePath != ".." &&
            !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static StringComparer PathComparer => FilePathComparison.Comparer;

    private Game ReadGame(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        SystemId = reader.GetString(1),
        Path = _pathResolver.ToAbsolutePath(reader.GetString(2)),
        Title = reader.GetString(3),
        TitleOrigin = (GameTitleOrigin)reader.GetInt32(4),
        CoverPath = reader.IsDBNull(5) ? null : _pathResolver.ToAbsolutePath(reader.GetString(5)),
        CoverOrigin = (GameCoverOrigin)reader.GetInt32(6),
        ExternalSourceId = reader.IsDBNull(7) ? null : reader.GetString(7),
        ExternalSourceEntryId = reader.IsDBNull(8) ? null : reader.GetString(8),
        IsPresentInExternalSource = reader.IsDBNull(9) ? null : reader.GetInt64(9) != 0,
        IsAvailable = reader.GetInt64(10) != 0,
        // Written with the invariant round-trip ("O") format; parse the same way so a
        // non-Gregorian current culture can't shift the year or throw.
        DateAdded = DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        LastPlayedAt = reader.IsDBNull(12)
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(12)),
        Playtime = TimeSpan.FromSeconds(reader.GetInt64(13)),
        PlayCount = (int)reader.GetInt64(14),
    };

    private sealed record ExistingExternalEntry(long Id, GameTitleOrigin TitleOrigin, string Path);
}
