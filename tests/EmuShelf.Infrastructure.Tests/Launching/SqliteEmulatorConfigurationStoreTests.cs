using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Launching;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Tests.Launching;

public class SqliteEmulatorConfigurationStoreTests : TempAppDirectoryTestBase
{
    [Fact]
    public void Save_RoundTripsPortableExecutableAndArguments()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(
            database,
            new RelativePathResolver(paths));
        var executable = Path.Combine(paths.BaseDirectory, "Emulators", "Dolphin", "Dolphin.exe");

        store.Save(new EmulatorConfiguration("gamecube", executable, "-b -e \"{GamePath}\""));

        Assert.Equal(
            new EmulatorConfiguration("gamecube", executable, "-b -e \"{GamePath}\""),
            store.Get("gamecube"));

        using var connection = database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ExecutablePath FROM EmulatorConfigs WHERE SystemId = 'gamecube';";
        Assert.Equal("Emulators/Dolphin/Dolphin.exe", command.ExecuteScalar());
    }

    [Fact]
    public void Save_AllowsClearingExecutableWithoutLosingArguments()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(
            database,
            new RelativePathResolver(paths));

        store.Save(new EmulatorConfiguration("wii", null, "-b -e \"{GamePath}\""));

        Assert.Equal(
            new EmulatorConfiguration("wii", null, "-b -e \"{GamePath}\""),
            store.Get("wii"));
    }

    [Fact]
    public void SaveAll_RollsBackEveryRowWhenOneWriteFails()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(
            database,
            new RelativePathResolver(paths));
        using (var connection = database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TRIGGER AbortTestConfiguration
                BEFORE INSERT ON EmulatorConfigs
                WHEN NEW.SystemId = 'fail'
                BEGIN
                    SELECT RAISE(ABORT, 'intentional test failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => store.SaveAll(
        [
            new EmulatorConfiguration("gamecube", "/Dolphin.exe", "gc args"),
            new EmulatorConfiguration("fail", "/Fail.exe", "fail args"),
        ]));

        Assert.Null(store.Get("gamecube"));
        Assert.Null(store.Get("fail"));
    }
}
