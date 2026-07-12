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
    private const int CurrentSchemaVersion = 1;

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
        return connection;
    }

    /// <summary>Creates the database file and brings its schema up to <see cref="CurrentSchemaVersion"/>. Safe to call on every startup.</summary>
    public void Initialize()
    {
        using var connection = CreateConnection();

        var version = GetSchemaVersion(connection);
        if (version < CurrentSchemaVersion)
        {
            ApplyMigrationV1(connection);
        }
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
}
