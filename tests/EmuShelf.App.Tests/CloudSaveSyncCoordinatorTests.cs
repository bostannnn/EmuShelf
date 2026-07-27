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
            .ConnectGoogleDriveAsync("", "", Overrides(), CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.InvalidInput, result);
    }

    [Fact]
    public async Task Connect_WithNoUsablePlatform_ReportsInvalidInput()
    {
        // A remote alone is not enough: without a platform that can produce a provider there would
        // be nothing to sync into the newly created cloud folder.
        var result = await CreateCoordinator(new FakeSettingsService())
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", Overrides(), CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.InvalidInput, result);
    }

    [Fact]
    public async Task Connect_WhenRcloneMissing_ReportsRcloneMissing()
    {
        var result = await CreateCoordinator(new FakeSettingsService())
            .ConnectGoogleDriveAsync(
                "gdrive",
                "EmuShelf/Saves",
                Overrides(("playstation2", "/pcsx2")),
                CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public void IsRcloneAvailable_IsFalse_WhenTheBinaryDoesNotExist()
    {
        Assert.False(CreateCoordinator(new FakeSettingsService()).IsRcloneAvailable);
    }

    [Fact]
    public void CanSyncSystem_RequiresConnectionAndAResolvedPlatformDirectory()
    {
        var disconnectedSettings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                PpssppMemoryStickDirectory = "/portable/ppsspp",
            },
        };
        var disconnected = CreateCoordinator(new FakeSettingsService(), disconnectedSettings);

        Assert.False(disconnected.CanSyncSystem("psp"));

        var connectedSettings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                RemoteName = "gdrive",
                CloudFolder = "EmuShelf/Saves",
                Pcsx2ConfigDirectory = "/portable/pcsx2",
                PpssppMemoryStickDirectory = "/portable/ppsspp",
            },
        };
        var connected = CreateCoordinator(new FakeSettingsService(), connectedSettings);

        Assert.True(connected.CanSyncSystem("playstation2"));
        Assert.True(connected.CanSyncSystem("psp"));
        Assert.False(connected.CanSyncSystem("playstation"));
    }

    [Fact]
    public void UpdateOverride_PersistsPathWithoutChangingConnection()
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

        coordinator.UpdateOverride("playstation2", "/new/pcsx2");

        Assert.Equal("/new/pcsx2", settings.Current.CloudSaveSync.GetOverride("playstation2"));
        Assert.True(settings.Current.CloudSaveSync.Enabled);
        Assert.Equal("gdrive", settings.Current.CloudSaveSync.RemoteName);
        Assert.Equal(1, settings.SaveCalls);
    }

    [Fact]
    public void UpdateOverride_TrimsAndMirrorsOntoTheLegacyField()
    {
        var settings = new FakeSettingsService();
        var coordinator = CreateCoordinator(settings);

        coordinator.UpdateOverride("psp", " /portable/ppsspp ");

        Assert.Equal("/portable/ppsspp", settings.Current.CloudSaveSync.GetOverride("psp"));
        // Mirrored so rolling back to a build that predates the per-system dictionary still reads it.
        Assert.Equal("/portable/ppsspp", settings.Current.CloudSaveSync.PpssppMemoryStickDirectory);
    }

    [Fact]
    public async Task Detection_SaysSoWhenTheResolvedFolderDoesNotExistOnThisMachine()
    {
        // The quietest possible failure: a platform resolves a path, finds nothing there, and
        // reports a successful sync of zero saves. The row has to say the folder is not there.
        var root = Path.Combine(Path.GetTempPath(), "emushelf-detect", Guid.NewGuid().ToString("N"));
        var present = Path.Combine(root, "memstick");
        Directory.CreateDirectory(Path.Combine(present, "PSP", "SAVEDATA"));
        var absent = Path.Combine(root, "not-installed");
        try
        {
            var coordinator = CreateCoordinator(
                new FakeSettingsService(),
                new AppSettings
                {
                    CloudSaveSync = new CloudSaveSyncSettings
                    {
                        Enabled = true,
                        RemoteName = "gdrive",
                        CloudFolder = "EmuShelf/Saves",
                    }.WithOverride("psp", present),
                });

            var found = await coordinator.GetDetectionAsync("psp", TestContext.Current.CancellationToken);
            Assert.NotNull(found);
            Assert.Null(found.Warning);

            coordinator.UpdateOverride("psp", absent);
            var missing = await coordinator.GetDetectionAsync("psp", TestContext.Current.CancellationToken);

            Assert.NotNull(missing);
            Assert.Contains("does not exist on this machine", missing.Warning);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void LegacySettings_AreMigratedIntoPerSystemLocations()
    {
        var legacy = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                RemoteName = "gdrive",
                CloudFolder = "EmuShelf/Saves",
                Pcsx2ConfigDirectory = "/legacy/pcsx2",
                PpssppMemoryStickDirectory = "/legacy/ppsspp",
            },
        };

        var coordinator = CreateCoordinator(new FakeSettingsService(), legacy);

        Assert.Equal("/legacy/pcsx2", coordinator.Current.GetOverride("playstation2"));
        Assert.Equal("/legacy/ppsspp", coordinator.Current.GetOverride("psp"));
        Assert.True(coordinator.CanSyncSystem("playstation2"));
        Assert.True(coordinator.CanSyncSystem("psp"));
    }

    [Fact]
    public async Task Connect_WithPpssppOverride_DoesNotRequirePcsx2Directory()
    {
        var result = await CreateCoordinator(new FakeSettingsService())
            .ConnectGoogleDriveAsync(
                "gdrive",
                "EmuShelf/Saves",
                Overrides(("psp", "/portable/ppsspp")),
                CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public async Task Connect_WithConfiguredPpsspp_DoesNotRequireOverrides()
    {
        var result = await CreateCoordinator(
                new FakeSettingsService(),
                emulators: systemId => systemId == "psp" ? new SaveEmulatorInstallation("/app/ppsspp", false) : null)
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", Overrides(), CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public async Task Connect_WithConfiguredDuckStation_DoesNotRequireOverrides()
    {
        var result = await CreateCoordinator(
                new FakeSettingsService(),
                emulators: systemId => systemId == "playstation"
                    ? new SaveEmulatorInstallation("/app/duckstation", false)
                    : null)
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", Overrides(), CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public async Task Connect_WithFlatpakPpsspp_DoesNotRequireOverrides()
    {
        var result = await CreateCoordinator(
                new FakeSettingsService(),
                emulators: systemId => systemId == "psp" ? new SaveEmulatorInstallation(null, true) : null)
            .ConnectGoogleDriveAsync("gdrive", "EmuShelf/Saves", Overrides(), CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.RcloneMissing, result);
    }

    [Fact]
    public void SettingsContext_ExposesOneRowPerRegisteredPlatform()
    {
        var context = CreateCoordinator(new FakeSettingsService()).CreateSettingsContext();

        Assert.Equal(
            SaveProviderRegistry.SystemIds,
            context.GetPlatforms().Select(platform => platform.SystemId).ToArray());
    }

    private static IReadOnlyDictionary<string, string?> Overrides(params (string SystemId, string? Path)[] entries) =>
        entries.ToDictionary(entry => entry.SystemId, entry => entry.Path, StringComparer.Ordinal);

    private static CloudSaveSyncCoordinator CreateCoordinator(
        ISettingsService settings,
        AppSettings? initial = null,
        Func<string, SaveEmulatorInstallation?>? emulators = null) =>
        new(
            new FakePaths(),
            settings,
            initial ?? new AppSettings(),
            NullAppLogger.Instance,
            NonexistentRclone,
            emulatorInstallations: emulators);

    [Fact]
    public async Task SyncFailure_WhenRecordingTheResultCannotBeSaved_ReturnsTheOriginalFailure()
    {
        // The settings write is metadata about a transfer that already happened. A portable install
        // on a removed or read-only drive must not turn a completed sync into a reported failure,
        // and the retry inside the catch block must not escape the pipeline.
        var settings = new ThrowingSettingsService
        {
            Current = new AppSettings
            {
                CloudSaveSync = new CloudSaveSyncSettings
                {
                    Enabled = true,
                    RemoteName = "gdrive",
                    CloudFolder = "EmuShelf/Saves",
                    Pcsx2ConfigDirectory = "/pcsx2",
                },
            },
        };
        var coordinator = CreateCoordinator(settings, settings.Current);

        // rclone is absent, so the transport fails and the pipeline takes its catch path — the
        // exact route where RecordOutcome used to throw a second time and escape.
        var outcome = await coordinator.SyncNowAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloudSaveSyncStatus.Failed, outcome.Status);
        Assert.True(settings.SaveAttempts > 0);
    }

    [Fact]
    public async Task MultiPlatformFailure_DoesNotAttributeTheGlobalErrorToEveryPlatform()
    {
        var configuration = new CloudSaveSyncSettings
        {
            Enabled = true,
            RemoteName = "gdrive",
            CloudFolder = "EmuShelf/Saves",
        }
            .WithOverride("playstation2", "/pcsx2")
            .WithOverride("psp", "/ppsspp");
        var initial = new AppSettings { CloudSaveSync = configuration };
        var settings = new FakeSettingsService { Current = initial };
        var coordinator = CreateCoordinator(settings, initial);

        var outcome = await coordinator.SyncNowAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloudSaveSyncStatus.Failed, outcome.Status);
        Assert.Null(coordinator.Current.GetLocation("playstation2").LastError);
        Assert.Null(coordinator.Current.GetLocation("psp").LastError);
        Assert.Equal(0, settings.SaveCalls);
    }

    [Fact]
    public async Task MultiPlatformTargetConstructionFailure_DoesNotBlameAnEarlierPlatform()
    {
        var configuration = new CloudSaveSyncSettings
        {
            Enabled = true,
            RemoteName = "gdrive",
            CloudFolder = "EmuShelf/Saves",
        }
            .WithOverride("playstation", "/duckstation")
            .WithOverride("playstation2", "invalid\0path");
        var initial = new AppSettings { CloudSaveSync = configuration };
        var settings = new FakeSettingsService { Current = initial };
        var coordinator = CreateCoordinator(settings, initial);

        var outcome = await coordinator.SyncNowAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloudSaveSyncStatus.Failed, outcome.Status);
        Assert.Null(coordinator.Current.GetLocation("playstation").LastError);
        Assert.NotNull(coordinator.Current.GetLocation("playstation2").LastError);
        Assert.Equal(1, settings.SaveCalls);
    }

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

    private sealed class ThrowingSettingsService : FakeSettingsServiceBase
    {
        public int SaveAttempts { get; private set; }

        public override void Save(AppSettings settings)
        {
            SaveAttempts++;
            throw new IOException("the settings drive is not available");
        }
    }

    private abstract class FakeSettingsServiceBase : ISettingsService
    {
        public AppSettings Current { get; set; } = new();

        public AppSettings Load() => Current;

        public abstract void Save(AppSettings settings);
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
