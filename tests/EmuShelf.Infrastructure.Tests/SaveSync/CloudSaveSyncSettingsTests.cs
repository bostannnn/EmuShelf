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
}
