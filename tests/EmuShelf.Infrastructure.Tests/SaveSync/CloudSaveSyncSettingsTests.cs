using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Settings;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class CloudSaveSyncSettingsTests : TempAppDirectoryTestBase
{
    [Fact]
    public void CloudSaveSyncSettings_RoundTripThroughPortableSettingsFile()
    {
        AppPaths.EnsureDirectoriesExist();
        var service = new JsonSettingsService(AppPaths, NullAppLogger.Instance);
        var settings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                RemoteName = "emushelf-gdrive",
                CloudFolder = "EmuShelf/Saves",
                Pcsx2ConfigDirectory = "/home/deck/pcsx2",
                PpssppMemoryStickDirectory = "/home/deck/Emulation/saves/ppsspp",
            },
        };

        service.Save(settings);

        Assert.Equal(settings.CloudSaveSync, service.Load().CloudSaveSync);
    }

    [Fact]
    public void PerSystemSaveLocations_RoundTripThroughPortableSettingsFile()
    {
        AppPaths.EnsureDirectoriesExist();
        var service = new JsonSettingsService(AppPaths, NullAppLogger.Instance);
        var configuration = new CloudSaveSyncSettings { Enabled = true, RemoteName = "gdrive" }
            .WithOverride("playstation2", "/home/deck/pcsx2")
            .WithSyncSuccess("playstation2", new DateTimeOffset(2026, 7, 26, 9, 0, 0, TimeSpan.Zero))
            .WithSyncFailure("psp", "the remote was unreachable");

        service.Save(new AppSettings { CloudSaveSync = configuration });
        var loaded = service.Load().CloudSaveSync;

        Assert.Equal(configuration, loaded);
        Assert.Equal("/home/deck/pcsx2", loaded.GetOverride("playstation2"));
        Assert.Equal("the remote was unreachable", loaded.GetLocation("psp").LastError);
        Assert.NotNull(loaded.GetLocation("playstation2").LastSuccessUtc);
    }

    [Fact]
    public void LegacyEmulatorFields_MigrateIntoPerSystemLocations()
    {
        var legacy = new CloudSaveSyncSettings
        {
            Pcsx2ConfigDirectory = "/legacy/pcsx2",
            PpssppMemoryStickDirectory = "/legacy/ppsspp",
        };

        var migrated = legacy.NormalizeSaveLocations();

        Assert.Equal("/legacy/pcsx2", migrated.GetOverride("playstation2"));
        Assert.Equal("/legacy/ppsspp", migrated.GetOverride("psp"));
    }

    [Fact]
    public void Migration_NeverOverwritesAnExplicitPerSystemOverride()
    {
        var configuration = new CloudSaveSyncSettings { Pcsx2ConfigDirectory = "/legacy/pcsx2" }
            .WithOverride("playstation2", "/current/pcsx2");

        // WithOverride mirrors onto the legacy field, so re-normalizing must be a no-op rather
        // than resurrecting a stale value over the newer choice.
        Assert.Equal("/current/pcsx2", configuration.NormalizeSaveLocations().GetOverride("playstation2"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"playstation2\":null}")]
    public void ExplicitJsonNullSaveLocations_AreSanitizedBeforeUse(string saveLocationsJson)
    {
        AppPaths.EnsureDirectoriesExist();
        File.WriteAllText(
            AppPaths.SettingsFilePath,
            "{\"CloudSaveSync\":{\"Pcsx2ConfigDirectory\":\"/legacy/pcsx2\",\"SaveLocations\":" +
            saveLocationsJson + "}}");
        var loaded = new JsonSettingsService(AppPaths, NullAppLogger.Instance).Load();

        var normalized = loaded.CloudSaveSync.NormalizeSaveLocations();

        Assert.Equal("/legacy/pcsx2", normalized.GetOverride("playstation2"));
        Assert.NotNull(normalized.SaveLocations);
        Assert.DoesNotContain(normalized.SaveLocations, entry => entry.Value is null);
    }

    [Fact]
    public void Migration_DoesNotResurrectLegacyPathWhenANewerEntryClearedIt()
    {
        var configuration = new CloudSaveSyncSettings
        {
            Pcsx2ConfigDirectory = "/legacy/pcsx2",
            SaveLocations = new Dictionary<string, SaveLocationSettings>(StringComparer.Ordinal)
            {
                ["playstation2"] = new() { DirectoryOverride = null, LastError = "previous failure" },
            },
        };

        var normalized = configuration.NormalizeSaveLocations();

        Assert.Null(normalized.GetOverride("playstation2"));
        Assert.Equal("previous failure", normalized.GetLocation("playstation2").LastError);
    }

    [Fact]
    public void CloudSaveSyncSettings_DefaultsToDisabledWhenAbsentFromFile()
    {
        var loaded = new JsonSettingsService(AppPaths, NullAppLogger.Instance).Load();

        Assert.False(loaded.CloudSaveSync.Enabled);
        Assert.Null(loaded.CloudSaveSync.RemoteName);
    }

    [Fact]
    public void OptionalContent_IsOptIn()
    {
        var defaults = new CloudSaveSyncSettings().GetLocation("playstation2");
        Assert.False(defaults.SyncSaveStates);

        var enabled = new CloudSaveSyncSettings()
            .WithOptionalContent("playstation2", syncSaveStates: true)
            .GetLocation("playstation2");

        Assert.True(enabled.SyncSaveStates);
    }

    [Fact]
    public void SaveStateOverride_RoundTripsAndIsIndependentOfTheSaveFolder()
    {
        AppPaths.EnsureDirectoriesExist();
        var service = new JsonSettingsService(AppPaths, NullAppLogger.Instance);
        var configuration = new CloudSaveSyncSettings { Enabled = true, RemoteName = "gdrive" }
            .WithOverride("arcade", "/home/deck/retroarch/saves")
            .WithStateOverride("arcade", "/home/deck/retroarch/states")
            .WithOptionalContent("arcade", syncSaveStates: true);

        service.Save(new AppSettings { CloudSaveSync = configuration });
        var loaded = service.Load().CloudSaveSync;

        Assert.Equal(configuration, loaded);
        // The two folders are independent, mirroring the save-folder override 1:1 for save states.
        Assert.Equal("/home/deck/retroarch/saves", loaded.GetOverride("arcade"));
        Assert.Equal("/home/deck/retroarch/states", loaded.GetStateOverride("arcade"));
        Assert.True(loaded.GetLocation("arcade").SyncSaveStates);

        // Clearing one leaves the other and the opt-in intact.
        var cleared = loaded.WithStateOverride("arcade", null);
        Assert.Null(cleared.GetStateOverride("arcade"));
        Assert.Equal("/home/deck/retroarch/saves", cleared.GetOverride("arcade"));
        Assert.True(cleared.GetLocation("arcade").SyncSaveStates);
    }

    // Cheats and patches are no longer synced, so the flag is gone from the record. A settings file
    // written by a build that had it must still load, and must not disturb the state opt-in beside it.
    [Fact]
    public void RemovedCheatsAndPatchesSetting_DoesNotBreakExistingSettingsFiles()
    {
        AppPaths.EnsureDirectoriesExist();
        File.WriteAllText(
            AppPaths.SettingsFilePath,
            "{\"CloudSaveSync\":{\"SaveLocations\":{\"playstation2\":" +
            "{\"SyncCheatsAndPatches\":true,\"SyncSaveStates\":true}}}}");

        var loaded = new JsonSettingsService(AppPaths).Load();

        Assert.True(loaded.CloudSaveSync.GetLocation("playstation2").SyncSaveStates);
    }

    [Fact]
    public void RemovedRetentionSetting_DoesNotBreakExistingSettingsFiles()
    {
        AppPaths.EnsureDirectoriesExist();
        File.WriteAllText(
            AppPaths.SettingsFilePath,
            "{\"CloudSaveSync\":{\"SaveLocations\":{\"playstation2\":" +
            "{\"SyncSaveStates\":true,\"SaveStateRetention\":7}}}}");
        var service = new JsonSettingsService(AppPaths, NullAppLogger.Instance);

        var loaded = service.Load();

        Assert.True(loaded.CloudSaveSync.GetLocation("playstation2").SyncSaveStates);
        service.Save(loaded);
        Assert.DoesNotContain("SaveStateRetention", File.ReadAllText(AppPaths.SettingsFilePath), StringComparison.Ordinal);
    }

    [Fact]
    public void TransportKind_DefaultsToRcloneForASettingsFileWrittenBeforeTheChoiceExisted()
    {
        // Back-compat only: a settings.json from before this field existed must still deserialize.
        // It lands on the retired Rclone kind, which the coordinator treats as not configured, so the
        // user reconnects through the built-in client rather than syncing against a dead transport.
        AppPaths.EnsureDirectoriesExist();
        File.WriteAllText(
            AppPaths.SettingsFilePath,
            "{\"CloudSaveSync\":{\"Enabled\":true,\"RemoteName\":\"emushelf-gdrive\",\"CloudFolder\":\"EmuShelf/Saves\"}}");

        var loaded = new JsonSettingsService(AppPaths).Load();

        Assert.Equal(CloudTransportKind.Rclone, loaded.CloudSaveSync.TransportKind);
        Assert.Equal("emushelf-gdrive", loaded.CloudSaveSync.RemoteName);
    }

    [Fact]
    public void TransportKind_SurvivesARoundTripThroughSettingsJson()
    {
        AppPaths.EnsureDirectoriesExist();
        var service = new JsonSettingsService(AppPaths);
        var saved = service.Load() with
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                TransportKind = CloudTransportKind.GoogleDrive,
                CloudFolder = "EmuShelf/Saves",
            },
        };

        service.Save(saved);

        Assert.Equal(CloudTransportKind.GoogleDrive, service.Load().CloudSaveSync.TransportKind);
    }

    [Fact]
    public void TransportKind_ParticipatesInEquality()
    {
        // The hand-written Equals exists so a round-trip compares equal. A field left out of it
        // reads as "nothing changed" for a change that matters.
        var legacy = new CloudSaveSyncSettings { Enabled = true, CloudFolder = "EmuShelf/Saves" };
        var managed = legacy with { TransportKind = CloudTransportKind.GoogleDrive };

        Assert.NotEqual(legacy, managed);
        Assert.Equal(legacy, legacy with { TransportKind = CloudTransportKind.Rclone });
    }

    [Fact]
    public void BatteryNamespaceMigrated_ParticipatesInEquality()
    {
        // It is persistent state (the one-time migration guard), not a cache — flipping it must not
        // read as "nothing changed", or a future equality-gated write would re-run the migration.
        var before = new CloudSaveSyncSettings { Enabled = true, CloudFolder = "EmuShelf/Saves" };
        Assert.NotEqual(before, before with { BatteryNamespaceMigrated = true });
    }

    [Fact]
    public void BatteryNamespaceMigrated_RoundTripsAndDefaultsToFalse()
    {
        AppPaths.EnsureDirectoriesExist();
        var service = new JsonSettingsService(AppPaths, NullAppLogger.Instance);
        // An older settings.json without the field must deserialize to false so the migration runs once.
        Assert.False(service.Load().CloudSaveSync.BatteryNamespaceMigrated);

        service.Save(new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings { Enabled = true, BatteryNamespaceMigrated = true },
        });

        Assert.True(service.Load().CloudSaveSync.BatteryNamespaceMigrated);
    }

    [Fact]
    public void PerEmulatorOverride_IsIsolatedFromOtherEmulatorsOnTheSameSystem()
    {
        var configuration = new CloudSaveSyncSettings()
            .WithOverride("playstation", "duckstation", "/saves/duck")
            .WithOverride("playstation", "retroarch", "/saves/ra");

        Assert.Equal("/saves/duck", configuration.GetOverride("playstation", "duckstation"));
        Assert.Equal("/saves/ra", configuration.GetOverride("playstation", "retroarch"));
        // A per-emulator write never leaks onto the bare system-id key.
        Assert.Null(configuration.GetOverride("playstation"));
    }

    [Fact]
    public void PerEmulatorLocation_MovesTheWholeRecordTogether()
    {
        var configuration = new CloudSaveSyncSettings()
            .WithOverride("playstation", "retroarch", "/ra/saves")
            .WithStateOverride("playstation", "retroarch", "/ra/states")
            .WithOptionalContent("playstation", "retroarch", syncSaveStates: true)
            .WithSyncFailure("playstation", "retroarch", "boom");

        var location = configuration.GetLocation("playstation", "retroarch");
        Assert.Equal("/ra/saves", location.DirectoryOverride);
        Assert.Equal("/ra/states", location.StateDirectoryOverride);
        Assert.True(location.SyncSaveStates);
        Assert.Equal("boom", location.LastError);
        // The other emulator on the same system is untouched.
        Assert.Equal(new SaveLocationSettings(), configuration.GetLocation("playstation", "duckstation"));
    }

    [Fact]
    public void MigrateOverridesToPerEmulator_ReKeysLegacyOverrideToTheActiveEmulatorAndKeepsTheLegacyEntry()
    {
        var legacy = new CloudSaveSyncSettings().WithOverride("playstation", "/saves/ps1");

        var migrated = legacy.MigrateOverridesToPerEmulator(
            new Dictionary<string, string> { ["playstation"] = "duckstation" });

        Assert.Equal("/saves/ps1", migrated.GetOverride("playstation", "duckstation"));
        Assert.Null(migrated.GetOverride("playstation", "retroarch"));
        // The bare entry is retained so an older build still reads it (rollback safety).
        Assert.Equal("/saves/ps1", migrated.GetOverride("playstation"));
    }

    [Fact]
    public void MigrateOverridesToPerEmulator_PresenceWins_DoesNotOverwriteAnExplicitCompositeEntry()
    {
        var configuration = new CloudSaveSyncSettings()
            .WithOverride("playstation", "/legacy/ps1")
            .WithOverride("playstation", "duckstation", "/explicit/duck");

        var migrated = configuration.MigrateOverridesToPerEmulator(
            new Dictionary<string, string> { ["playstation"] = "duckstation" });

        Assert.Equal("/explicit/duck", migrated.GetOverride("playstation", "duckstation"));
    }

    [Fact]
    public void MigrateOverridesToPerEmulator_DoesNotReKeyABareMirrorOnceTheSystemHasAPerEmulatorEntry()
    {
        // Once a system has a per-emulator entry the feature is active, so its bare entry is a
        // rollback mirror — switching the active emulator must not inherit the other's folder.
        var configuration = new CloudSaveSyncSettings()
            .WithOverride("playstation", "duckstation", "/duck")
            .WithOverride("playstation", "/duck");

        var migrated = configuration.MigrateOverridesToPerEmulator(
            new Dictionary<string, string> { ["playstation"] = "retroarch" });

        Assert.Null(migrated.GetOverride("playstation", "retroarch"));
        Assert.Equal("/duck", migrated.GetOverride("playstation", "duckstation"));
    }

    [Fact]
    public void MigrateOverridesToPerEmulator_IsIdempotent()
    {
        var mapping = new Dictionary<string, string> { ["psp"] = "ppsspp" };
        var once = new CloudSaveSyncSettings()
            .WithOverride("psp", "/saves/psp")
            .MigrateOverridesToPerEmulator(mapping);

        Assert.Equal(once, once.MigrateOverridesToPerEmulator(mapping));
    }

    [Fact]
    public void PerEmulatorOverride_RoundTripsThroughPortableSettingsFile()
    {
        AppPaths.EnsureDirectoriesExist();
        var service = new JsonSettingsService(AppPaths, NullAppLogger.Instance);
        var configuration = new CloudSaveSyncSettings { Enabled = true, RemoteName = "gdrive" }
            .WithOverride("playstation", "retroarch", "/ra/saves");

        service.Save(new AppSettings { CloudSaveSync = configuration });
        var loaded = service.Load().CloudSaveSync;

        Assert.Equal(configuration, loaded);
        Assert.Equal("/ra/saves", loaded.GetOverride("playstation", "retroarch"));
    }
}
