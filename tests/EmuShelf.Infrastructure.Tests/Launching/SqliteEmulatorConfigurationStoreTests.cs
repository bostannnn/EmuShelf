using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Launching;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace EmuShelf.Infrastructure.Tests.Launching;

public class SqliteEmulatorConfigurationStoreTests : TempAppDirectoryTestBase
{
    [Fact]
    public void Save_FlatpakTarget_RoundTripsFromSharedInstallationOnly()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        store.Save(new EmulatorConfiguration("playstation2", null, "{GamePath}")
        {
            EmulatorId = "pcsx2",
            EmulatorInstallationId = "pcsx2-flatpak",
            LaunchTarget = new FlatpakApplicationTarget("net.pcsx2.PCSX2"),
        });

        var loaded = store.Get("playstation2");
        Assert.Null(loaded!.ExecutablePath);
        Assert.Equal(new FlatpakApplicationTarget("net.pcsx2.PCSX2"), loaded.LaunchTarget);

        using var connection = database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TargetKind || ':' || TargetValue FROM EmulatorInstallations WHERE InstallationId = 'pcsx2-flatpak';";
        Assert.Equal("flatpak:net.pcsx2.PCSX2", command.ExecuteScalar());
    }

    [Fact]
    public void Save_LaunchScreen_RoundTrips()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        store.Save(new EmulatorConfiguration("playstation2", "/usr/bin/pcsx2", "{GamePath}")
        {
            EmulatorId = "pcsx2",
            EmulatorInstallationId = "pcsx2-direct",
            LaunchScreen = GameLaunchScreen.External,
        });

        Assert.Equal(GameLaunchScreen.External, store.Get("playstation2")!.LaunchScreen);
    }

    [Fact]
    public void SetLaunchScreen_UpdatesActiveProfile_WithoutRepinningEmulatorSelection()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        store.Save(new EmulatorConfiguration("playstation2", "/usr/bin/pcsx2", "{GamePath}")
        {
            EmulatorId = "pcsx2",
            EmulatorInstallationId = "pcsx2-direct",
        });

        store.SetLaunchScreen("playstation2", GameLaunchScreen.External);

        var loaded = store.Get("playstation2")!;
        Assert.Equal(GameLaunchScreen.External, loaded.LaunchScreen);
        // The real emulator selection is untouched (not clobbered to the system id), and the rest of the
        // config round-trips unchanged.
        Assert.Equal("pcsx2", loaded.EmulatorId);
        Assert.Equal("pcsx2", store.GetActiveEmulatorId("playstation2"));
    }

    [Fact]
    public void SetLaunchScreen_UnconfiguredSystem_PersistsWithoutPinningSelection()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        // A system launchable from the maintained-first default that was never configured has no row and
        // no selection. The preference is still stored, and no active-emulator selection is invented.
        store.SetLaunchScreen("playstation", GameLaunchScreen.External);

        Assert.Equal(GameLaunchScreen.External, store.Get("playstation")!.LaunchScreen);
        Assert.Null(store.GetActiveEmulatorId("playstation"));
    }

    [Fact]
    public void Get_LaunchScreenDefaultsToAsk_WhenNeverSet()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        // A config saved with no explicit screen (the migration default) reads back as Ask.
        store.Save(new EmulatorConfiguration("playstation2", "/usr/bin/pcsx2", "{GamePath}")
        {
            EmulatorId = "pcsx2",
            EmulatorInstallationId = "pcsx2-direct",
        });

        Assert.Equal(GameLaunchScreen.Ask, store.Get("playstation2")!.LaunchScreen);
    }

    [Fact]
    public void Save_BranchPinnedFlatpakTarget_RoundTripsTheBranch()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        store.Save(new EmulatorConfiguration("playstation2", null, "{GamePath}")
        {
            EmulatorId = "pcsx2",
            EmulatorInstallationId = "pcsx2-flatpak",
            LaunchTarget = new FlatpakApplicationTarget("net.pcsx2.PCSX2", "beta"),
        });

        var loaded = store.Get("playstation2");
        Assert.Equal(new FlatpakApplicationTarget("net.pcsx2.PCSX2", "beta"), loaded!.LaunchTarget);

        using var connection = database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT TargetKind || ':' || TargetValue FROM EmulatorInstallations WHERE InstallationId = 'pcsx2-flatpak';";
        Assert.Equal("flatpak:net.pcsx2.PCSX2//beta", command.ExecuteScalar());
    }

    [Fact]
    public void Save_InvalidFlatpakBranch_IsRejected()
    {
        var database = new LibraryDatabase(AppPaths);
        AppPaths.EnsureDirectoriesExist();
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(AppPaths));

        var exception = Assert.Throws<ArgumentException>(() => store.Save(
            new EmulatorConfiguration("playstation2", null, "{GamePath}")
            {
                LaunchTarget = new FlatpakApplicationTarget("net.pcsx2.PCSX2", "bad/branch"),
            }));

        Assert.Contains("Flatpak branch", exception.Message);
    }

    [Fact]
    public void Save_InvalidFlatpakApplicationId_IsRejected()
    {
        var database = new LibraryDatabase(AppPaths);
        AppPaths.EnsureDirectoriesExist();
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(AppPaths));

        var exception = Assert.Throws<ArgumentException>(() => store.Save(
            new EmulatorConfiguration("playstation2", null, "{GamePath}")
            {
                LaunchTarget = new FlatpakApplicationTarget("not-an-app-id"),
            }));

        Assert.Contains("Flatpak application id", exception.Message);
    }

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

        store.Save(new EmulatorConfiguration("gamecube", executable, "-b -e \"{GamePath}\"")
        {
            EmulatorId = "dolphin",
            EmulatorInstallationId = "dolphin-gamecube",
        });

        Assert.Equal(
            new EmulatorConfiguration("gamecube", executable, "-b -e \"{GamePath}\"")
            {
                EmulatorId = "dolphin",
                EmulatorInstallationId = "dolphin-gamecube",
                LaunchTarget = new DirectExecutableTarget(executable),
            },
            store.Get("gamecube"));

        using var connection = database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ExecutablePath FROM EmulatorConfigs WHERE SystemId = 'gamecube';";
        Assert.IsType<DBNull>(command.ExecuteScalar());
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

        store.Save(new EmulatorConfiguration("wii", null, "-b -e \"{GamePath}\"")
        {
            EmulatorId = "dolphin",
            EmulatorInstallationId = "dolphin-wii",
        });

        Assert.Equal(
            new EmulatorConfiguration("wii", null, "-b -e \"{GamePath}\"")
            {
                EmulatorId = "dolphin",
                EmulatorInstallationId = "dolphin-wii",
            },
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

    [Fact]
    public void SaveAll_SharedInstallationKeepsOnePortableExecutableAndSeparateCores()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(
            database,
            new RelativePathResolver(paths));
        var executable = Path.Combine(paths.BaseDirectory, "Emulators", "RetroArch", "retroarch.exe");
        var megaDriveCore = Path.Combine(paths.BaseDirectory, "Emulators", "RetroArch", "cores", "genesis.dll");
        var dsCore = Path.Combine(paths.BaseDirectory, "Emulators", "RetroArch", "cores", "melonds.dll");

        store.SaveAll(
        [
            new EmulatorConfiguration("megadrive", executable, "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                EmulatorInstallationId = "retroarch",
                CorePath = megaDriveCore,
            },
            new EmulatorConfiguration("nds", executable, "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                EmulatorInstallationId = "retroarch",
                CorePath = dsCore,
            },
        ]);

        var megaDrive = store.Get("megadrive");
        var ds = store.Get("nds");
        Assert.NotNull(megaDrive);
        Assert.NotNull(ds);
        Assert.Equal(executable, megaDrive.ExecutablePath);
        Assert.Equal(executable, ds.ExecutablePath);
        Assert.Equal(megaDriveCore, megaDrive.CorePath);
        Assert.Equal(dsCore, ds.CorePath);

        using var connection = database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ExecutablePath FROM EmulatorInstallations WHERE InstallationId = 'retroarch';";
        Assert.Equal("Emulators/RetroArch/retroarch.exe", command.ExecuteScalar());
    }

    [Fact]
    public void Save_ClearingOneSharedInstallationClearsItForEveryMappedSystem()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(
            database,
            new RelativePathResolver(paths));
        var executable = Path.Combine(paths.BaseDirectory, "Emulators", "RetroArch", "retroarch.exe");

        store.SaveAll(
        [
            new EmulatorConfiguration("megadrive", executable, "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                EmulatorInstallationId = "retroarch",
            },
            new EmulatorConfiguration("nds", executable, "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                EmulatorInstallationId = "retroarch",
            },
        ]);

        store.Save(new EmulatorConfiguration("megadrive", null, "-L \"{CorePath}\" \"{GamePath}\"")
        {
            EmulatorId = "retroarch",
            EmulatorInstallationId = "retroarch",
        });

        Assert.Null(store.Get("megadrive")!.ExecutablePath);
        Assert.Null(store.Get("nds")!.ExecutablePath);
    }

    [Fact]
    public void GetAll_ReturnsAnEntryPerRequestedSystemAndMatchesGet()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));
        var executable = Path.Combine(paths.BaseDirectory, "Emulators", "Dolphin", "Dolphin.exe");

        store.Save(new EmulatorConfiguration("gamecube", executable, "-b -e \"{GamePath}\"")
        {
            EmulatorId = "dolphin",
            EmulatorInstallationId = "dolphin-gamecube",
        });

        var all = store.GetAll(["gamecube", "playstation2"]);

        // Every requested id is present; unconfigured systems map to null, configured ones match Get.
        Assert.Equal(["gamecube", "playstation2"], all.Keys.OrderBy(key => key));
        Assert.Null(all["playstation2"]);
        Assert.Equal(store.Get("gamecube"), all["gamecube"]);
    }

    [Fact]
    public void GetAll_IgnoresConfiguredSystemsTheCallerDidNotRequest()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        store.Save(new EmulatorConfiguration("wii", "/Dolphin.exe", "wii args")
        {
            EmulatorId = "dolphin",
            EmulatorInstallationId = "dolphin-wii",
        });

        var all = store.GetAll(["gamecube"]);

        Assert.Equal(["gamecube"], all.Keys);
        Assert.Null(all["gamecube"]);
    }

    [Fact]
    public void Initialize_FromVersion7_MigratesExistingPerSystemPathsWithoutMergingThem()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        using (var connection = database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
                INSERT INTO SchemaVersion VALUES (7);
                CREATE TABLE EmulatorConfigs (
                    SystemId TEXT PRIMARY KEY,
                    ExecutablePath TEXT NULL,
                    LaunchArguments TEXT NULL
                );
                CREATE TABLE Games (
                    Id INTEGER PRIMARY KEY,
                    Path TEXT NOT NULL
                );
                INSERT INTO EmulatorConfigs VALUES
                    ('gamecube', 'Emulators/Dolphin-GC.exe', '-b -e "{GamePath}"'),
                    ('wii', 'Emulators/Dolphin-Wii.exe', '-b -e "{GamePath}"');
                """;
            command.ExecuteNonQuery();
        }

        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(
            database,
            new RelativePathResolver(paths));

        var gameCube = store.Get("gamecube");
        var wii = store.Get("wii");
        Assert.NotNull(gameCube);
        Assert.NotNull(wii);
        Assert.Equal(Path.Combine(paths.BaseDirectory, "Emulators", "Dolphin-GC.exe"), gameCube.ExecutablePath);
        Assert.Equal(Path.Combine(paths.BaseDirectory, "Emulators", "Dolphin-Wii.exe"), wii.ExecutablePath);
        Assert.Equal("legacy-gamecube", gameCube.EmulatorInstallationId);
        Assert.Equal("legacy-wii", wii.EmulatorInstallationId);
    }

    [Fact]
    public void SaveAll_KeepsSeveralProfilesForOneSystemAndMakesTheSavedOneActive()
    {
        var store = CreateStore(out _);
        var duckStation = Path.Combine(BaseDirectory, "Emulators", "DuckStation", "duckstation.exe");
        var retroArch = Path.Combine(BaseDirectory, "Emulators", "RetroArch", "retroarch.exe");
        var core = Path.Combine(BaseDirectory, "Emulators", "RetroArch", "cores", "swanstation_libretro.dll");

        // Configure DuckStation first, then RetroArch: both profiles are kept, and RetroArch (saved
        // last) is the active one for the system.
        store.Save(new EmulatorConfiguration("playstation", duckStation, "-batch -- \"{GamePath}\"")
        {
            EmulatorId = "duckstation",
            EmulatorInstallationId = "duckstation-playstation",
        });
        store.Save(new EmulatorConfiguration("playstation", retroArch, "-L \"{CorePath}\" \"{GamePath}\"")
        {
            EmulatorId = "retroarch",
            EmulatorInstallationId = "retroarch",
            CorePath = core,
        });

        Assert.Equal("retroarch", store.GetActiveEmulatorId("playstation"));
        Assert.Equal("retroarch", store.Get("playstation")!.EmulatorId);
        Assert.Equal(core, store.Get("playstation")!.CorePath);

        var profiles = store.GetProfiles("playstation");
        Assert.Equal(["duckstation", "retroarch"], profiles.Configurations.Select(c => c.EmulatorId).OrderBy(id => id));
        Assert.Equal(duckStation, profiles.ForEmulator("duckstation")!.ExecutablePath);
        Assert.Equal(retroArch, profiles.ForEmulator("retroarch")!.ExecutablePath);
    }

    [Fact]
    public void SetActiveEmulator_SwitchesTheProfileGetReturnsWithoutLosingTheOther()
    {
        var store = CreateStore(out _);
        var duckStation = Path.Combine(BaseDirectory, "Emulators", "DuckStation", "duckstation.exe");
        var retroArch = Path.Combine(BaseDirectory, "Emulators", "RetroArch", "retroarch.exe");
        store.SaveAll(
        [
            new EmulatorConfiguration("playstation", duckStation, "-batch -- \"{GamePath}\"")
            {
                EmulatorId = "duckstation",
                EmulatorInstallationId = "duckstation-playstation",
            },
            new EmulatorConfiguration("playstation", retroArch, "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                EmulatorInstallationId = "retroarch",
            },
        ]);

        store.SetActiveEmulator("playstation", "duckstation");

        Assert.Equal("duckstation", store.Get("playstation")!.EmulatorId);
        Assert.Equal(duckStation, store.Get("playstation")!.ExecutablePath);
        // The RetroArch profile is still stored and reachable by name.
        Assert.Equal(retroArch, store.GetForEmulator("playstation", "retroarch")!.ExecutablePath);
    }

    [Fact]
    public void Initialize_FromVersion15_KeysConfigsByEmulatorAndSeedsTheActiveSelection()
    {
        var paths = AppPaths;
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        using (var connection = database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (Version INTEGER NOT NULL);
                INSERT INTO SchemaVersion VALUES (15);
                CREATE TABLE Games (
                    Id INTEGER PRIMARY KEY,
                    Path TEXT NOT NULL
                );
                CREATE TABLE EmulatorConfigs (
                    SystemId TEXT PRIMARY KEY,
                    ExecutablePath TEXT NULL,
                    LaunchArguments TEXT NULL,
                    EmulatorId TEXT NULL,
                    EmulatorInstallationId TEXT NULL,
                    CorePath TEXT NULL
                );
                CREATE TABLE EmulatorInstallations (
                    InstallationId TEXT PRIMARY KEY,
                    EmulatorId TEXT NOT NULL,
                    ExecutablePath TEXT NULL,
                    TargetKind TEXT NULL,
                    TargetValue TEXT NULL
                );
                INSERT INTO EmulatorConfigs
                    (SystemId, ExecutablePath, LaunchArguments, EmulatorId, EmulatorInstallationId)
                VALUES
                    ('playstation', 'Emulators/DuckStation/duckstation.exe', '-batch -- "{GamePath}"',
                     'duckstation', 'duckstation-playstation');
                """;
            command.ExecuteNonQuery();
        }

        database.Initialize();
        var store = new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(paths));

        // The migrated row keeps DuckStation active and the composite key now admits a second profile.
        Assert.Equal("duckstation", store.GetActiveEmulatorId("playstation"));
        store.Save(new EmulatorConfiguration("playstation", "Emulators/RetroArch/retroarch.exe",
            "-L \"{CorePath}\" \"{GamePath}\"")
        {
            EmulatorId = "retroarch",
            EmulatorInstallationId = "retroarch",
        });
        Assert.Equal(2, store.GetProfiles("playstation").Configurations.Count);
        Assert.Equal("retroarch", store.GetActiveEmulatorId("playstation"));
    }

    private SqliteEmulatorConfigurationStore CreateStore(out LibraryDatabase database)
    {
        AppPaths.EnsureDirectoriesExist();
        database = new LibraryDatabase(AppPaths);
        database.Initialize();
        return new SqliteEmulatorConfigurationStore(database, new RelativePathResolver(AppPaths));
    }

    [Fact]
    public void SharedRetroArchInstallation_RelocatesWithItsSystemCores()
    {
        var originalBase = Path.Combine(BaseDirectory, "Portable");
        var originalPaths = new AppPaths(originalBase);
        originalPaths.EnsureDirectoriesExist();
        var originalDatabase = new LibraryDatabase(originalPaths);
        originalDatabase.Initialize();
        var originalStore = new SqliteEmulatorConfigurationStore(
            originalDatabase,
            new RelativePathResolver(originalPaths));
        var executable = Path.Combine(originalBase, "Emulators", "RetroArch", "retroarch.exe");
        var core = Path.Combine(originalBase, "Emulators", "RetroArch", "cores", "mgba.dll");
        originalStore.SaveAll(
        [
            new EmulatorConfiguration("megadrive", executable, "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                EmulatorInstallationId = "retroarch",
                CorePath = Path.Combine(originalBase, "Emulators", "RetroArch", "cores", "genesis.dll"),
            },
            new EmulatorConfiguration("gba", executable, "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                EmulatorInstallationId = "retroarch",
                CorePath = core,
            },
        ]);

        var movedBase = Path.Combine(BaseDirectory, "MovedPortable");
        Directory.Move(originalBase, movedBase);
        var movedPaths = new AppPaths(movedBase);
        var movedDatabase = new LibraryDatabase(movedPaths);
        movedDatabase.Initialize();
        var movedStore = new SqliteEmulatorConfigurationStore(
            movedDatabase,
            new RelativePathResolver(movedPaths));

        var megaDrive = movedStore.Get("megadrive");
        var gba = movedStore.Get("gba");
        Assert.NotNull(megaDrive);
        Assert.NotNull(gba);
        Assert.Equal(Path.Combine(movedBase, "Emulators", "RetroArch", "retroarch.exe"),
            megaDrive.ExecutablePath);
        Assert.Equal(megaDrive.ExecutablePath, gba.ExecutablePath);
        Assert.Equal(Path.Combine(movedBase, "Emulators", "RetroArch", "cores", "genesis.dll"),
            megaDrive.CorePath);
        Assert.Equal(Path.Combine(movedBase, "Emulators", "RetroArch", "cores", "mgba.dll"),
            gba.CorePath);
    }
}
