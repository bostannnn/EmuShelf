using EmuShelf.Core.Storage;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Persistence;

/// <summary>
/// Owns the schema of Data/library.db and applies versioned migrations.
/// Games, LibraryFolders, and EmulatorConfigs are read/written by later
/// milestones; this milestone only guarantees the schema exists.
/// </summary>
public sealed class LibraryDatabase
{
    private const int CurrentSchemaVersion = 15;

    private readonly IAppPaths _appPaths;

    public LibraryDatabase(IAppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public SqliteConnection CreateConnection()
    {
        // Use the builder rather than string interpolation so a data path containing
        // ';' or '=' (both legal filename chars) can't corrupt the connection string.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _appPaths.DatabaseFilePath,
            // Disable connection pooling so closing a connection releases the OS handle on
            // library.db immediately. EmuShelf is portable: the Data/ folder must be safe to
            // move, back up, or sync while the app is idle, and a pooled handle would keep the
            // file open between operations (harmless on macOS/Linux, but on Windows it blocks
            // moving or deleting the folder). Pooling saves only microseconds for this
            // occasional-write desktop workload.
            Pooling = false,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var pragmas = connection.CreateCommand();
        // busy_timeout: the app reads the library on background threads (a platform switch, an
        // availability pass) while other work writes it (availability updates, RetroAchievements,
        // save sync). With the default rollback journal a reader that overlaps a writer fails with
        // SQLITE_BUSY *immediately*; rapid platform switching then threw mid-load and left the grid
        // blank until relaunch. A busy timeout makes the reader wait for the writer instead.
        pragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragmas.ExecuteNonQuery();
        return connection;
    }

    /// <summary>Creates the database file and brings its schema up to <see cref="CurrentSchemaVersion"/>. Safe to call on every startup.</summary>
    public void Initialize()
    {
        using var connection = CreateConnection();

        var version = GetSchemaVersion(connection);
        if (version < 1)
        {
            ApplyMigrationV1(connection);
            version = 1;
        }

        if (version < 2)
        {
            ApplyMigrationV2(connection);
            version = 2;
        }

        if (version < 3)
        {
            ApplyMigrationV3(connection);
            version = 3;
        }

        if (version < 4)
        {
            ApplyMigrationV4(connection);
            version = 4;
        }

        if (version < 5)
        {
            ApplyMigrationV5(connection);
            version = 5;
        }

        if (version < 6)
        {
            ApplyMigrationV6(connection);
            version = 6;
        }

        if (version < 7)
        {
            ApplyMigrationV7(connection);
            version = 7;
        }

        if (version < 8)
        {
            ApplyMigrationV8(connection);
            version = 8;
        }

        if (version < 9)
        {
            ApplyMigrationV9(connection);
            version = 9;
        }

        if (version < 10)
        {
            ApplyMigrationV10(connection);
            version = 10;
        }

        if (version < 11)
        {
            ApplyMigrationV11(connection);
            version = 11;
        }

        if (version < 12)
        {
            ApplyMigrationV12(connection);
            version = 12;
        }

        if (version < 13)
        {
            ApplyMigrationV13(connection);
            version = 13;
        }

        if (version < 14)
        {
            ApplyMigrationV14(connection);
            version = 14;
        }

        if (version < 15)
            ApplyMigrationV15(connection);
    }

    private static int GetSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'SchemaVersion';";
        var tableExists = (long)command.ExecuteScalar()! > 0;
        if (!tableExists)
            return 0;

        command.CommandText = "SELECT Version FROM SchemaVersion LIMIT 1;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void ApplyMigrationV1(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // All statements use IF NOT EXISTS and the version row is inserted only when
        // absent, so applying this migration over a partially-created or empty-versioned
        // database heals it instead of throwing 'table already exists' on every startup.
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER NOT NULL
            );
            INSERT INTO SchemaVersion (Version)
                SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM SchemaVersion);

            CREATE TABLE IF NOT EXISTS LibraryFolders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SystemId TEXT NOT NULL,
                Path TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_LibraryFolders_SystemId ON LibraryFolders (SystemId);

            CREATE TABLE IF NOT EXISTS Games (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SystemId TEXT NOT NULL,
                Path TEXT NOT NULL COLLATE NOCASE,
                Title TEXT NOT NULL,
                CoverPath TEXT NULL,
                IsAvailable INTEGER NOT NULL DEFAULT 1,
                DateAdded TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Games_SystemId ON Games (SystemId);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Games_Path ON Games (Path);

            CREATE TABLE IF NOT EXISTS EmulatorConfigs (
                SystemId TEXT PRIMARY KEY,
                ExecutablePath TEXT NULL,
                LaunchArguments TEXT NULL
            );
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV2(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            ALTER TABLE Games ADD COLUMN DateAddedUnixMilliseconds INTEGER NULL;
            UPDATE Games
            SET DateAddedUnixMilliseconds = CAST(
                (julianday(DateAdded) - 2440587.5) * 86400000 AS INTEGER)
            WHERE DateAddedUnixMilliseconds IS NULL;
            CREATE INDEX IX_Games_DateAddedUnixMilliseconds
                ON Games (DateAddedUnixMilliseconds DESC);
            UPDATE SchemaVersion SET Version = 2;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV3(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            ALTER TABLE Games ADD COLUMN TitleOrigin INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE Games ADD COLUMN CoverOrigin INTEGER NOT NULL DEFAULT 0;
            UPDATE Games SET CoverOrigin = 2 WHERE CoverPath IS NOT NULL;

            CREATE TABLE GameIdentifiers (
                GameId INTEGER NOT NULL,
                Kind INTEGER NOT NULL,
                Value TEXT NOT NULL COLLATE NOCASE,
                Source TEXT NOT NULL,
                IsPrimary INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (GameId, Kind, Value),
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_GameIdentifiers_KindValue
                ON GameIdentifiers (Kind, Value);

            CREATE TABLE GameMetadata (
                GameId INTEGER PRIMARY KEY,
                Status INTEGER NOT NULL DEFAULT 0,
                CatalogId TEXT NULL,
                CatalogEntryId TEXT NULL,
                CanonicalTitle TEXT NULL,
                Region TEXT NULL,
                CoverProviderId TEXT NULL,
                CoverSourceUri TEXT NULL,
                LastAttemptUnixMilliseconds INTEGER NULL,
                LastError TEXT NULL,
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_GameMetadata_Status ON GameMetadata (Status);

            UPDATE SchemaVersion SET Version = 3;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV4(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE RetroAchievementGameLinks (
                GameId INTEGER PRIMARY KEY,
                Status INTEGER NOT NULL DEFAULT 0,
                CanonicalHash TEXT NULL COLLATE NOCASE,
                HashAlgorithmVersion TEXT NOT NULL,
                SourceFingerprint TEXT NOT NULL,
                RetroAchievementsGameId INTEGER NULL,
                HasAchievements INTEGER NULL,
                LastAttemptUnixMilliseconds INTEGER NOT NULL,
                LastError TEXT NULL,
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );
            CREATE INDEX IX_RetroAchievementGameLinks_CanonicalHash
                ON RetroAchievementGameLinks (CanonicalHash);
            CREATE INDEX IX_RetroAchievementGameLinks_RetroAchievementsGameId
                ON RetroAchievementGameLinks (RetroAchievementsGameId);

            UPDATE SchemaVersion SET Version = 4;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV5(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE RetroAchievementProgress (
                RetroAchievementsGameId INTEGER PRIMARY KEY,
                AchievementCount INTEGER NOT NULL,
                NumAwarded INTEGER NOT NULL,
                NumAwardedHardcore INTEGER NOT NULL,
                LastRefreshUnixMilliseconds INTEGER NOT NULL
            );

            UPDATE SchemaVersion SET Version = 5;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV6(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE RetroAchievementGameDetails (
                RetroAchievementsGameId INTEGER PRIMARY KEY,
                Title TEXT NOT NULL,
                AchievementCount INTEGER NOT NULL,
                NumAwarded INTEGER NOT NULL,
                NumAwardedHardcore INTEGER NOT NULL,
                LastRefreshUnixMilliseconds INTEGER NOT NULL
            );

            CREATE TABLE RetroAchievementDetails (
                RetroAchievementsGameId INTEGER NOT NULL,
                AchievementId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                Points INTEGER NOT NULL,
                BadgeName TEXT NOT NULL,
                DisplayOrder INTEGER NOT NULL,
                DateEarnedUnixMilliseconds INTEGER NULL,
                DateEarnedHardcoreUnixMilliseconds INTEGER NULL,
                PRIMARY KEY (RetroAchievementsGameId, AchievementId),
                FOREIGN KEY (RetroAchievementsGameId)
                    REFERENCES RetroAchievementGameDetails (RetroAchievementsGameId)
                    ON DELETE CASCADE
            );
            CREATE INDEX IX_RetroAchievementDetails_DisplayOrder
                ON RetroAchievementDetails (RetroAchievementsGameId, DisplayOrder, AchievementId);

            UPDATE SchemaVersion SET Version = 6;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV7(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE RetroAchievementProgressSync (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                LastRefreshUnixMilliseconds INTEGER NOT NULL
            );

            UPDATE SchemaVersion SET Version = 7;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV8(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        EnsureEmulatorConfigColumns(connection, transaction);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS EmulatorInstallations (
                InstallationId TEXT PRIMARY KEY,
                EmulatorId TEXT NOT NULL,
                ExecutablePath TEXT NULL
            );

            UPDATE EmulatorConfigs
            SET EmulatorId = CASE SystemId
                WHEN 'playstation' THEN 'duckstation'
                WHEN 'playstation2' THEN 'pcsx2'
                WHEN 'playstation3' THEN 'rpcs3'
                WHEN 'gamecube' THEN 'dolphin'
                WHEN 'wii' THEN 'dolphin'
                WHEN 'psp' THEN 'ppsspp'
                WHEN 'megadrive' THEN 'retroarch'
                WHEN 'nds' THEN 'retroarch'
                WHEN 'gba' THEN 'retroarch'
                WHEN 'snes' THEN 'retroarch'
                ELSE SystemId
            END
            WHERE EmulatorId IS NULL OR trim(EmulatorId) = '';

            -- Existing configurations deliberately receive private installation ids. This keeps
            -- a user who had configured different Dolphin paths for GameCube and Wii working
            -- exactly as before; new compatible systems can intentionally choose one shared id.
            UPDATE EmulatorConfigs
            SET EmulatorInstallationId = 'legacy-' || SystemId
            WHERE EmulatorInstallationId IS NULL OR trim(EmulatorInstallationId) = '';

            INSERT OR IGNORE INTO EmulatorInstallations (
                InstallationId, EmulatorId, ExecutablePath)
            SELECT EmulatorInstallationId, EmulatorId, ExecutablePath
            FROM EmulatorConfigs
            WHERE EmulatorInstallationId IS NOT NULL;

            UPDATE SchemaVersion SET Version = 8;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void EnsureEmulatorConfigColumns(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText =
                """
                CREATE TABLE IF NOT EXISTS EmulatorConfigs (
                    SystemId TEXT PRIMARY KEY,
                    ExecutablePath TEXT NULL,
                    LaunchArguments TEXT NULL
                );
                """;
            create.ExecuteNonQuery();
        }

        AddColumnIfMissing(connection, transaction, "EmulatorId", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "EmulatorInstallationId", "TEXT NULL");
        AddColumnIfMissing(connection, transaction, "CorePath", "TEXT NULL");
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        string columnDefinition)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('EmulatorConfigs') WHERE name = $columnName;";
        check.Parameters.AddWithValue("$columnName", columnName);
        if ((long)check.ExecuteScalar()! > 0)
            return;

        using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE EmulatorConfigs ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static void ApplyMigrationV9(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        AddGameColumnIfMissing(connection, transaction, "ExternalSourceId", "TEXT NULL");
        AddGameColumnIfMissing(connection, transaction, "ExternalSourceEntryId", "TEXT NULL");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS ExternalLibrarySources (
                SourceId TEXT PRIMARY KEY,
                SystemId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Location TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Games_ExternalSourceEntry
                ON Games (ExternalSourceId, ExternalSourceEntryId)
                WHERE ExternalSourceId IS NOT NULL AND ExternalSourceEntryId IS NOT NULL;

            UPDATE SchemaVersion SET Version = 9;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV10(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        // The original schema always had IsAvailable. Retain the migration layer's existing
        // interrupted-schema healing behaviour before deriving the new external-source state.
        AddGameColumnIfMissing(connection, transaction, "IsAvailable", "INTEGER NOT NULL DEFAULT 1");
        AddGameColumnIfMissing(connection, transaction, "ExternalSourcePresent", "INTEGER NULL");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            -- Schema v9 represented both an absent source record and an unavailable listed path
            -- with IsAvailable. Preserve the established source-missing display for old rows;
            -- future syncs record the two states independently.
            UPDATE Games
            SET ExternalSourcePresent = IsAvailable
            WHERE ExternalSourceId IS NOT NULL AND ExternalSourcePresent IS NULL;

            UPDATE SchemaVersion SET Version = 10;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV11(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText =
                """
                CREATE TABLE IF NOT EXISTS EmulatorInstallations (
                    InstallationId TEXT PRIMARY KEY,
                    EmulatorId TEXT NOT NULL,
                    ExecutablePath TEXT NULL
                );
                """;
            create.ExecuteNonQuery();
        }

        AddTableColumnIfMissing(connection, transaction, "EmulatorInstallations", "TargetKind", "TEXT NULL");
        AddTableColumnIfMissing(connection, transaction, "EmulatorInstallations", "TargetValue", "TEXT NULL");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            -- v8 made installations shareable; v11 makes their target typed and authoritative.
            -- Existing executable paths are direct targets. The legacy config column remains
            -- readable only for databases that were interrupted before this migration finished.
            UPDATE EmulatorInstallations
            SET TargetKind = 'direct', TargetValue = ExecutablePath
            WHERE (TargetKind IS NULL OR trim(TargetKind) = '')
              AND ExecutablePath IS NOT NULL AND trim(ExecutablePath) <> '';

            UPDATE SchemaVersion SET Version = 11;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV12(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS GameDiscSelections (
                TitleSetKey TEXT PRIMARY KEY,
                GameId INTEGER NOT NULL,
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_GameDiscSelections_GameId
                ON GameDiscSelections (GameId);

            UPDATE SchemaVersion SET Version = 12;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV13(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS GameMetadataValues (
                GameId INTEGER NOT NULL,
                Field INTEGER NOT NULL,
                Locale TEXT NOT NULL DEFAULT '',
                Value TEXT NOT NULL,
                Origin INTEGER NOT NULL,
                ProviderId TEXT NULL,
                ProviderItemId TEXT NULL,
                SourceUri TEXT NULL,
                UpdatedUnixMilliseconds INTEGER NOT NULL,
                PRIMARY KEY (GameId, Field, Locale),
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_GameMetadataValues_Provider
                ON GameMetadataValues (ProviderId);

            CREATE TABLE IF NOT EXISTS GameMediaAssets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GameId INTEGER NOT NULL,
                Kind INTEGER NOT NULL,
                LocalPath TEXT NOT NULL COLLATE NOCASE,
                IsSelected INTEGER NOT NULL DEFAULT 0,
                SelectionOrigin INTEGER NULL,
                Origin INTEGER NOT NULL,
                ProviderId TEXT NULL,
                ProviderItemId TEXT NULL,
                SourceUri TEXT NULL,
                Region TEXT NULL,
                Language TEXT NULL,
                FileExtension TEXT NOT NULL,
                Width INTEGER NULL,
                Height INTEGER NULL,
                Crc32 TEXT NULL,
                Md5 TEXT NULL,
                Sha1 TEXT NULL,
                UpdatedUnixMilliseconds INTEGER NOT NULL,
                UNIQUE (GameId, Kind, LocalPath),
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_GameMediaAssets_GameKind
                ON GameMediaAssets (GameId, Kind);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_GameMediaAssets_Selected
                ON GameMediaAssets (GameId, Kind)
                WHERE IsSelected = 1;

            CREATE TABLE IF NOT EXISTS GameProviderMatches (
                GameId INTEGER NOT NULL,
                ProviderId TEXT NOT NULL COLLATE NOCASE,
                ProviderSystemId TEXT NULL,
                SystemMappingVersion INTEGER NULL,
                ProviderGameId TEXT NULL,
                ProviderRomId TEXT NULL,
                MatchMethod INTEGER NOT NULL,
                EvidenceValue TEXT NULL,
                Status INTEGER NOT NULL,
                LastAttemptUnixMilliseconds INTEGER NOT NULL,
                LastError TEXT NULL,
                PRIMARY KEY (GameId, ProviderId),
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );

            UPDATE SchemaVersion SET Version = 13;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV14(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS GameFileFingerprints (
                GameId INTEGER NOT NULL,
                ProviderId TEXT NOT NULL COLLATE NOCASE,
                SourcePath TEXT NOT NULL,
                Scope INTEGER NOT NULL,
                FileSize INTEGER NOT NULL,
                LastWriteUnixMilliseconds INTEGER NOT NULL,
                Crc32 TEXT NOT NULL,
                Md5 TEXT NOT NULL,
                Sha1 TEXT NOT NULL,
                ComputedUnixMilliseconds INTEGER NOT NULL,
                PRIMARY KEY (GameId, ProviderId),
                FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
            );

            UPDATE SchemaVersion SET Version = 14;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void ApplyMigrationV15(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        // A nullable column, so existing rows are "never played" (NULL) rather than a fabricated
        // timestamp. AddGameColumnIfMissing heals a database interrupted mid-migration, matching v9/v10.
        AddGameColumnIfMissing(connection, transaction, "LastPlayedUnixMilliseconds", "INTEGER NULL");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Partial index over played rows only: the Recently Played query orders by this descending
        // and never-played rows are filtered out, so indexing NULLs would only bloat the index.
        command.CommandText =
            """
            CREATE INDEX IF NOT EXISTS IX_Games_LastPlayedUnixMilliseconds
                ON Games (LastPlayedUnixMilliseconds DESC)
                WHERE LastPlayedUnixMilliseconds IS NOT NULL;

            UPDATE SchemaVersion SET Version = 15;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void AddGameColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        string columnDefinition)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('Games') WHERE name = $columnName;";
        check.Parameters.AddWithValue("$columnName", columnName);
        if ((long)check.ExecuteScalar()! > 0)
            return;

        using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE Games ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }

    private static void AddTableColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = $columnName;";
        check.Parameters.AddWithValue("$columnName", columnName);
        if ((long)check.ExecuteScalar()! > 0)
            return;

        using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
        alter.ExecuteNonQuery();
    }
}
