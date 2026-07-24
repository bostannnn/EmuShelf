using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Tests.Persistence;

public class LibraryDatabaseTests : TempAppDirectoryTestBase
{
    public LibraryDatabaseTests()
    {
        AppPaths.EnsureDirectoriesExist();
    }

    [Fact]
    public void Initialize_CreatesExpectedTables()
    {
        var database = new LibraryDatabase(AppPaths);

        database.Initialize();

        Assert.Contains("Games", GetTableNames(database));
        Assert.Contains("LibraryFolders", GetTableNames(database));
        Assert.Contains("EmulatorConfigs", GetTableNames(database));
        Assert.Contains("EmulatorInstallations", GetTableNames(database));
        Assert.Contains("ExternalLibrarySources", GetTableNames(database));
        Assert.Contains("GameIdentifiers", GetTableNames(database));
        Assert.Contains("GameMetadata", GetTableNames(database));
        Assert.Contains("RetroAchievementGameLinks", GetTableNames(database));
        Assert.Contains("RetroAchievementProgress", GetTableNames(database));
        Assert.Contains("RetroAchievementProgressSync", GetTableNames(database));
        Assert.Contains("RetroAchievementGameDetails", GetTableNames(database));
        Assert.Contains("RetroAchievementDetails", GetTableNames(database));
        Assert.Contains("SchemaVersion", GetTableNames(database));
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotFailOrDuplicateSchema()
    {
        var database = new LibraryDatabase(AppPaths);

        database.Initialize();
        database.Initialize();

        using var connection = database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SchemaVersion;";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
        command.CommandText = "SELECT Version FROM SchemaVersion LIMIT 1;";
        Assert.Equal(11L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void Initialize_EmptySchemaVersionTable_HealsInsteadOfCrashing()
    {
        // A present-but-empty SchemaVersion table (external corruption / interrupted edit)
        // reads as version 0; the migration must be idempotent, not throw 'already exists'.
        var database = new LibraryDatabase(AppPaths);
        using (var connection = database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);";
            command.ExecuteNonQuery();
        }

        database.Initialize();

        Assert.Contains("Games", GetTableNames(database));
        using var check = database.CreateConnection();
        using var versionCommand = check.CreateCommand();
        versionCommand.CommandText = "SELECT COUNT(*) FROM SchemaVersion;";
        Assert.Equal(1L, (long)versionCommand.ExecuteScalar()!);
    }

    [Fact]
    public void Initialize_DataPathContainingSemicolon_OpensCorrectFile()
    {
        // ';' and '=' are legal filename chars; the connection string must not be corrupted
        // by them when the portable app is dropped in such a folder.
        var quirkyBase = Path.Combine(BaseDirectory, "Games; Emu=x");
        var quirkyPaths = new AppPaths(quirkyBase);
        quirkyPaths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(quirkyPaths);

        database.Initialize();

        Assert.True(File.Exists(quirkyPaths.DatabaseFilePath));
        Assert.Contains("Games", GetTableNames(database));
    }

    [Fact]
    public void Games_PathColumn_RejectsDuplicateInsert()
    {
        var database = new LibraryDatabase(AppPaths);
        database.Initialize();

        using var connection = database.CreateConnection();
        InsertGame(connection, "PS1/game.cue");

        Assert.Throws<SqliteException>(() => InsertGame(connection, "PS1/game.cue"));
    }

    [Fact]
    public void Games_PathColumn_RejectsDuplicateInsert_DifferingOnlyByCase()
    {
        // Games identity is the file path, and v1's target platforms (Windows, macOS) both
        // have case-insensitive file systems, so "Game.cue" and "game.CUE" are the same file.
        var database = new LibraryDatabase(AppPaths);
        database.Initialize();

        using var connection = database.CreateConnection();
        InsertGame(connection, "PS1/game.cue");

        Assert.Throws<SqliteException>(() => InsertGame(connection, "PS1/GAME.CUE"));
    }

    [Fact]
    public void Initialize_AddsIndexedRecentlyAddedTimestamp()
    {
        var database = new LibraryDatabase(AppPaths);

        database.Initialize();

        using var connection = database.CreateConnection();
        using var columnCommand = connection.CreateCommand();
        columnCommand.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('Games') " +
            "WHERE name = 'DateAddedUnixMilliseconds';";
        Assert.Equal(1L, (long)columnCommand.ExecuteScalar()!);

        using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' " +
            "AND name = 'IX_Games_DateAddedUnixMilliseconds';";
        Assert.Equal(1L, (long)indexCommand.ExecuteScalar()!);
    }

    [Fact]
    public void Initialize_FromVersion2_PreservesExistingCoverAsUserOwned()
    {
        var database = new LibraryDatabase(AppPaths);
        using (var connection = database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
                INSERT INTO SchemaVersion (Version) VALUES (2);
                CREATE TABLE Games (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SystemId TEXT NOT NULL,
                    Path TEXT NOT NULL COLLATE NOCASE,
                    Title TEXT NOT NULL,
                    CoverPath TEXT NULL,
                    IsAvailable INTEGER NOT NULL DEFAULT 1,
                    DateAdded TEXT NOT NULL,
                    DateAddedUnixMilliseconds INTEGER NULL
                );
                INSERT INTO Games (
                    SystemId, Path, Title, CoverPath, IsAvailable, DateAdded,
                    DateAddedUnixMilliseconds)
                VALUES (
                    'playstation', 'PS1/game.cue', 'Custom title', 'Covers/1.jpg', 1,
                    '2026-07-12T00:00:00.0000000+00:00', 1783814400000);
                """;
            command.ExecuteNonQuery();
        }

        database.Initialize();

        using var check = database.CreateConnection();
        using var version = check.CreateCommand();
        version.CommandText = "SELECT Version FROM SchemaVersion LIMIT 1;";
        Assert.Equal(11L, (long)version.ExecuteScalar()!);

        using var origins = check.CreateCommand();
        origins.CommandText = "SELECT TitleOrigin, CoverOrigin FROM Games LIMIT 1;";
        using var reader = origins.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(2L, reader.GetInt64(1));
    }

    [Fact]
    public void Initialize_FromVersion9_DistinguishesPriorSourceMissingStateFromLocalRows()
    {
        var database = new LibraryDatabase(AppPaths);
        using (var connection = database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
                INSERT INTO SchemaVersion (Version) VALUES (9);
                CREATE TABLE Games (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SystemId TEXT NOT NULL,
                    Path TEXT NOT NULL COLLATE NOCASE,
                    Title TEXT NOT NULL,
                    IsAvailable INTEGER NOT NULL,
                    ExternalSourceId TEXT NULL,
                    ExternalSourceEntryId TEXT NULL
                );
                INSERT INTO Games (
                    SystemId, Path, Title, IsAvailable, ExternalSourceId, ExternalSourceEntryId)
                VALUES
                    ('playstation3', 'Games/current', 'Current', 1, 'rpcs3-library', 'BLES12345'),
                    ('playstation3', 'Games/missing', 'Missing', 0, 'rpcs3-library', 'BLES12346'),
                    ('playstation', 'Games/local', 'Local', 0, NULL, NULL);
                """;
            command.ExecuteNonQuery();
        }

        database.Initialize();

        using var check = database.CreateConnection();
        using var presenceCommand = check.CreateCommand();
        presenceCommand.CommandText =
            "SELECT ExternalSourcePresent FROM Games ORDER BY Id;";
        using var reader = presenceCommand.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(reader.Read());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
    }

    private static List<string> GetTableNames(LibraryDatabase database)
    {
        using var connection = database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        using var reader = command.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private static void InsertGame(SqliteConnection connection, string path)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Games (SystemId, Path, Title, IsAvailable, DateAdded) " +
            "VALUES ('playstation', $path, 'Test', 1, '2026-07-12');";
        command.Parameters.AddWithValue("$path", path);
        command.ExecuteNonQuery();
    }
}
