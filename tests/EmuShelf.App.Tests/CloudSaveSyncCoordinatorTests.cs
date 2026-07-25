using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.Tests;

public class CloudSaveSyncCoordinatorTests
{
    private static readonly string NonexistentRclone =
        Path.Combine(Path.GetTempPath(), "emushelf-no-rclone", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SyncNow_WhenNotConfigured_DoesNothing()
    {
        var outcome = await CreateCoordinator(new FakeSettingsService())
            .SyncNowAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloudSaveSyncStatus.NotConfigured, outcome.Status);
    }

    [Fact]
    public async Task Connect_WithMissingInput_ReportsInvalidInput()
    {
        var result = await CreateCoordinator(new FakeSettingsService())
            .ConnectGoogleDriveAsync("", "", "", "", CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.InvalidInput, result);
    }

    [Fact]
    public async Task Connect_WhenRcloneMissing_ReportsRcloneMissing()
    {
        var result = await CreateCoordinator(new FakeSettingsService())
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", "/pcsx2", "", CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public void IsRcloneAvailable_IsFalse_WhenTheBinaryDoesNotExist()
    {
        Assert.False(CreateCoordinator(new FakeSettingsService()).IsRcloneAvailable);
    }

    [Fact]
    public void UpdatePcsx2Directory_PersistsPathWithoutChangingConnection()
    {
        var settings = new FakeSettingsService
        {
            Current = new AppSettings
            {
                CloudSaveSync = new CloudSaveSyncSettings
                {
                    Enabled = true,
                    RemoteName = "gdrive",
                    CloudFolder = "EmuShelf/Saves",
                    Pcsx2ConfigDirectory = "/old/pcsx2",
                },
            },
        };
        var coordinator = CreateCoordinator(settings, settings.Current);

        coordinator.UpdatePcsx2Directory("/new/pcsx2");

        Assert.Equal("/new/pcsx2", settings.Current.CloudSaveSync.Pcsx2ConfigDirectory);
        Assert.True(settings.Current.CloudSaveSync.Enabled);
        Assert.Equal("gdrive", settings.Current.CloudSaveSync.RemoteName);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void UpdatePpssppDirectory_PersistsPathWithoutChangingConnection()
    {
        var settings = new FakeSettingsService
        {
            Current = new AppSettings
            {
                CloudSaveSync = new CloudSaveSyncSettings
                {
                    Enabled = true,
                    RemoteName = "gdrive",
                    CloudFolder = "EmuShelf/Saves",
                },
            },
        };
        var coordinator = CreateCoordinator(settings, settings.Current);

        coordinator.UpdatePpssppDirectory(" /portable/ppsspp ");

        Assert.Equal("/portable/ppsspp", settings.Current.CloudSaveSync.PpssppMemoryStickDirectory);
        Assert.True(settings.Current.CloudSaveSync.Enabled);
        Assert.Equal("gdrive", settings.Current.CloudSaveSync.RemoteName);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public async Task Connect_WithPpssppOverride_DoesNotRequirePcsx2Directory()
    {
        var settings = new FakeSettingsService();

        var result = await CreateCoordinator(settings)
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", "", "/portable/ppsspp", CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public async Task Connect_WithConfiguredPpsspp_DoesNotRequireOverrides()
    {
        var settings = new FakeSettingsService();

        var result = await CreateCoordinator(settings, defaultPpssppDirectory: () => "/app/ppsspp")
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", "", "", CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public async Task Connect_WithFlatpakPpsspp_DoesNotRequireOverrides()
    {
        var settings = new FakeSettingsService();

        var result = await CreateCoordinator(settings, isPpssppFlatpak: () => true)
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", "", "", CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    private static CloudSaveSyncCoordinator CreateCoordinator(
        FakeSettingsService settings,
        AppSettings? initial = null,
        Func<string?>? defaultPpssppDirectory = null,
        Func<bool>? isPpssppFlatpak = null) =>
        new(
            new FakePaths(),
            settings,
            initial ?? new AppSettings(),
            NullAppLogger.Instance,
            NonexistentRclone,
            defaultPpssppInstallationDirectory: defaultPpssppDirectory,
            isPpssppFlatpak: isPpssppFlatpak);

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; set; } = new();
        public int SaveCalls { get; private set; }

        public AppSettings Load() => Current;

        public void Save(AppSettings settings)
        {
            Current = settings;
            SaveCalls++;
        }
    }

    private sealed class FakePaths : IAppPaths
    {
        public string BaseDirectory => "/app";
        public string DataDirectory => "/app/Data";
        public string CoversDirectory => "/app/Covers";
        public string CacheDirectory => "/app/Cache";
        public string LogsDirectory => "/app/Logs";
        public string SettingsDirectory => "/app/Settings";
        public string SavesDirectory => "/app/Saves";
        public string DatabaseFilePath => "/app/Data/library.db";
        public string SettingsFilePath => "/app/Settings/settings.json";

        public void EnsureDirectoriesExist()
        {
        }
    }
}
