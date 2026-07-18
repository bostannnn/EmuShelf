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
    private const int CurrentSchemaVersion = 5;

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
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
        foreignKeys.ExecuteNonQuery();
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

        if (version < CurrentSchemaVersion)
            ApplyMigrationV5(connection);
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
}
