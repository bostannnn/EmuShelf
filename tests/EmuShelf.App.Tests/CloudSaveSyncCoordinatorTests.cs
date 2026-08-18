using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.Tests;

public class CloudSaveSyncCoordinatorTests
{
    [Fact]
    public async Task SyncNow_WhenNotConfigured_DoesNothing()
    {
        var outcome = await CreateCoordinator(new FakeSettingsService())
            .SyncNowAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloudSaveSyncStatus.NotConfigured, outcome.Status);
    }

    [Fact]
    public async Task ExportSaves_DeviceAndCloud_WhenNotConnected_ReturnsNotConfigured()
    {
        var destination = Path.Combine(Path.GetTempPath(), "emushelf-export-" + Guid.NewGuid().ToString("N") + ".zip");

        var result = await CreateCoordinator(new FakeSettingsService()).ExportSavesAsync(
            destination, SaveExportScope.DeviceAndCloud, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SaveExportStatus.NotConfigured, result.Status);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ExportSaves_Device_WithNoConfiguredPlatforms_ExportsNothing()
    {
        var destination = Path.Combine(Path.GetTempPath(), "emushelf-export-" + Guid.NewGuid().ToString("N") + ".zip");

        var result = await CreateCoordinator(new FakeSettingsService()).ExportSavesAsync(
            destination, SaveExportScope.Device, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(SaveExportStatus.NothingToExport, result.Status);
        Assert.False(File.Exists(destination));
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
                TransportKind = CloudTransportKind.GoogleDrive,
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
    public void CanSyncSystem_ManagedTransportNeedsNoRemoteName()
    {
        // The managed client authenticates as the connected account, so there is no named remote to
        // require. Demanding one would leave a correctly connected user silently unable to sync.
        var settings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                TransportKind = CloudTransportKind.GoogleDrive,
                RemoteName = null,
                CloudFolder = "EmuShelf/Saves",
                Pcsx2ConfigDirectory = "/portable/pcsx2",
            },
        };

        Assert.True(CreateCoordinator(new FakeSettingsService(), settings).CanSyncSystem("playstation2"));
    }

    [Fact]
    public void CanSyncSystem_ManagedTransportStillNeedsAFolder()
    {
        var settings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                TransportKind = CloudTransportKind.GoogleDrive,
                CloudFolder = null,
                Pcsx2ConfigDirectory = "/portable/pcsx2",
            },
        };

        Assert.False(CreateCoordinator(new FakeSettingsService(), settings).CanSyncSystem("playstation2"));
    }

    [Fact]
    public void CanSyncSystem_TreatsAStoredRcloneConnectionAsNotConfigured()
    {
        // rclone is retired: a fully-populated connection left over from it (remote name, folder, and
        // an emulator directory) is deliberately not syncable, so the user reconnects through the
        // built-in client rather than syncing against a transport that no longer exists.
        var settings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                TransportKind = CloudTransportKind.Rclone,
                RemoteName = "gdrive",
                CloudFolder = "EmuShelf/Saves",
                Pcsx2ConfigDirectory = "/portable/pcsx2",
            },
        };

        Assert.False(CreateCoordinator(new FakeSettingsService(), settings).CanSyncSystem("playstation2"));
    }

    [Fact]
    public async Task ConnectManaged_WithNoFolderReportsInvalidInput()
    {
        var result = await CreateCoordinator(new FakeSettingsService()).ConnectGoogleDriveManagedAsync(
            string.Empty,
            Overrides(("playstation2", "/pcsx2")),
            _ => { },
            CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.InvalidInput, result);
    }

    [Fact]
    public async Task ConnectManaged_OnABuildWithNoEmbeddedClientSaysSoDistinctly()
    {
        // A build with no baked-in OAuth client — an unconfigured local build, as here — cannot offer
        // this transport at all. Reporting it as a generic failure would send the user looking at
        // their network or their Google account for something neither can fix.
        var browserOpened = false;

        var result = await CreateCoordinator(new FakeSettingsService()).ConnectGoogleDriveManagedAsync(
            "EmuShelf/Saves",
            Overrides(("playstation2", "/pcsx2")),
            _ => browserOpened = true,
            CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.ManagedTransportUnavailable, result);
        Assert.False(browserOpened);
    }

    [Fact]
    public async Task ConnectManaged_WithNoUsablePlatformReportsInvalidInputBeforeOpeningABrowser()
    {
        // Sending the user through a Google consent screen only to find there is nothing to sync is
        // the wrong order; the cheap local check comes first.
        var browserOpened = false;

        var result = await CreateCoordinator(new FakeSettingsService()).ConnectGoogleDriveManagedAsync(
            "EmuShelf/Saves",
            Overrides(),
            _ => browserOpened = true,
            CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.InvalidInput, result);
        Assert.False(browserOpened);
    }

    [Fact]
    public async Task SyncNow_WhenTheManagedTransportCannotBeBuilt_FailsInsteadOfThrowing()
    {
        // Configured for a transport this build cannot construct (no embedded OAuth client). An
        // automatic sync runs on the launch path, so an escaping exception here would surface as an
        // unhandled failure while starting a game rather than as a reported sync problem.
        var settings = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings
            {
                Enabled = true,
                TransportKind = CloudTransportKind.GoogleDrive,
                CloudFolder = "EmuShelf/Saves",
                Pcsx2ConfigDirectory = "/portable/pcsx2",
            },
        };

        var outcome = await CreateCoordinator(new FakeSettingsService(), settings)
            .SyncNowAsync(cancellationToken: CancellationToken.None);

        Assert.Equal(CloudSaveSyncStatus.Failed, outcome.Status);
    }

    [Fact]
    public async Task Disconnect_ClearsTheCachedCloudFolderId()
    {
        // A folder id belongs to the connection that resolved it. Carrying it into the next connect
        // would address the previous account's folder.
        var settings = new FakeSettingsService
        {
            Current = new AppSettings
            {
                CloudSaveSync = new CloudSaveSyncSettings
                {
                    Enabled = true,
                    RemoteName = "gdrive",
                    CloudFolder = "EmuShelf/Saves",
                    CloudFolderId = "folder-abc",
                },
            },
        };
        var coordinator = CreateCoordinator(settings, settings.Current);

        await coordinator.DisconnectAsync(CancellationToken.None);

        Assert.Null(settings.Current.CloudSaveSync.CloudFolderId);
        Assert.False(settings.Current.CloudSaveSync.Enabled);
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
    public void UpdateOverrides_PersistsEveryPathInOneSettingsWrite()
    {
        var settings = new FakeSettingsService();
        var coordinator = CreateCoordinator(settings);

        coordinator.UpdateOverrides(Overrides(
            ("playstation2", " /portable/pcsx2 "),
            ("psp", "/portable/ppsspp")));

        Assert.Equal("/portable/pcsx2", settings.Current.CloudSaveSync.GetOverride("playstation2"));
        Assert.Equal("/portable/ppsspp", settings.Current.CloudSaveSync.GetOverride("psp"));
        Assert.Equal(1, settings.SaveCalls);
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
    public async Task OptionalDetectionFailure_DoesNotInvalidateDirectPcsx2MemoryCardLocation()
    {
        var memcards = Path.Combine(Path.GetTempPath(), "emushelf-direct-memcards", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(memcards);
        await File.WriteAllTextAsync(
            Path.Combine(memcards, "Mcd001.ps2"),
            "card",
            TestContext.Current.CancellationToken);
        try
        {
            var settings = new AppSettings
            {
                CloudSaveSync = new CloudSaveSyncSettings
                {
                    Enabled = true,
                    RemoteName = "gdrive",
                    CloudFolder = "EmuShelf/Saves",
                }.WithOverride("playstation2", memcards),
            };
            var coordinator = CreateCoordinator(new FakeSettingsService(), settings);

            var detection = await coordinator.GetDetectionAsync(
                "playstation2",
                TestContext.Current.CancellationToken);

            Assert.NotNull(detection);
            Assert.Equal(Path.GetFullPath(memcards), detection.Directory);
            Assert.Null(detection.Warning);
            Assert.NotEmpty(detection.OptionalContent!);
            Assert.All(detection.OptionalContent!, location =>
                Assert.Contains("memory-card folder", location.Warning, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(memcards, recursive: true); } catch (IOException) { }
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
                TransportKind = CloudTransportKind.GoogleDrive,
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
    public void UpdateOverride_KeysByTheActiveEmulator_AndMirrorsToTheBareKeyForRollback()
    {
        var settings = new FakeSettingsService();
        var coordinator = CreateCoordinator(
            settings,
            emulators: systemId => systemId == "playstation"
                ? new SaveEmulatorInstallation("/app/retroarch", false, EmulatorId: "retroarch")
                : null);

        coordinator.UpdateOverride("playstation", "/ra/saves");

        // Stored under the active emulator, not the other emulator on the same system.
        Assert.Equal("/ra/saves", coordinator.Current.GetOverride("playstation", "retroarch"));
        Assert.Null(coordinator.Current.GetOverride("playstation", "duckstation"));
        // Mirrored onto the bare key so an older build still reads the active emulator's choice.
        Assert.Equal("/ra/saves", coordinator.Current.GetOverride("playstation"));
    }

    [Fact]
    public void SwitchingTheActiveEmulator_DoesNotInheritTheOtherEmulatorsOverride()
    {
        var settings = new FakeSettingsService();
        CreateCoordinator(
                settings,
                emulators: systemId => systemId == "playstation"
                    ? new SaveEmulatorInstallation("/app/duckstation", false, EmulatorId: "duckstation")
                    : null)
            .UpdateOverride("playstation", "/duck/saves");

        // Re-open with RetroArch active (the previous coordinator persisted into the shared settings).
        var retroArch = CreateCoordinator(
            settings,
            settings.Current,
            emulators: systemId => systemId == "playstation"
                ? new SaveEmulatorInstallation("/app/retroarch", false, EmulatorId: "retroarch")
                : null);

        Assert.Null(retroArch.Current.GetOverride("playstation", "retroarch"));
        Assert.Equal("/duck/saves", retroArch.Current.GetOverride("playstation", "duckstation"));
    }

    [Fact]
    public void DescribePlatformForEmulator_ReadsEachEmulatorsOwnOverride_RegardlessOfActive()
    {
        var settings = new FakeSettingsService();
        // DuckStation is the active emulator; its override is filed under (playstation, duckstation).
        var coordinator = CreateCoordinator(
            settings,
            emulators: systemId => systemId == "playstation"
                ? new SaveEmulatorInstallation("/app/duckstation", false, EmulatorId: "duckstation")
                : null);
        coordinator.UpdateOverride("playstation", "/duck/saves");
        // Give RetroArch its own stored override without making it the active emulator.
        coordinator.UpdateConfiguration(coordinator.Current.WithOverride("playstation", "retroarch", "/ra/saves"));

        // The picker-driven read returns each emulator's own folder even though DuckStation is active,
        // which is what lets the Saves row follow the picker before the switch is saved.
        Assert.Equal("/duck/saves", coordinator.DescribePlatformForEmulator("playstation", "duckstation")?.Override);
        Assert.Equal("/ra/saves", coordinator.DescribePlatformForEmulator("playstation", "retroarch")?.Override);
        // An unknown emulator id falls back to the system's default profile (DuckStation), matching launch.
        Assert.Equal("/duck/saves", coordinator.DescribePlatformForEmulator("playstation", "nonsense")?.Override);
        // A system with no save platform yields null rather than throwing.
        Assert.Null(coordinator.DescribePlatformForEmulator("no-such-system", "x"));
    }

    [Fact]
    public void LegacyBareOverride_IsReKeyedToTheActiveEmulatorOnLoad()
    {
        var legacy = new AppSettings
        {
            CloudSaveSync = new CloudSaveSyncSettings { Enabled = true, RemoteName = "gdrive" }
                .WithOverride("playstation", "/legacy/ps1"),
        };

        var coordinator = CreateCoordinator(
            new FakeSettingsService(),
            legacy,
            emulators: systemId => systemId == "playstation"
                ? new SaveEmulatorInstallation("/app/retroarch", false, EmulatorId: "retroarch")
                : null);

        Assert.Equal("/legacy/ps1", coordinator.Current.GetOverride("playstation", "retroarch"));
        // The legacy bare entry is retained for rollback.
        Assert.Equal("/legacy/ps1", coordinator.Current.GetOverride("playstation"));
    }

    // These four exercise the "at least one usable save platform" gate the connect applies before it
    // reaches the transport. A local test build embeds no OAuth client, so a connect whose platform
    // check passes returns ManagedTransportUnavailable — proof the gate let it through — rather than
    // InvalidInput, which is what an empty platform set returns.
    [Fact]
    public async Task Connect_WithPpssppOverride_DoesNotRequirePcsx2Directory()
    {
        var result = await CreateCoordinator(new FakeSettingsService())
            .ConnectGoogleDriveManagedAsync(
                "EmuShelf/Saves",
                Overrides(("psp", "/portable/ppsspp")),
                _ => { },
                CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.ManagedTransportUnavailable, result);
    }

    [Fact]
    public async Task Connect_WithConfiguredPpsspp_DoesNotRequireOverrides()
    {
        var result = await CreateCoordinator(
                new FakeSettingsService(),
                emulators: systemId => systemId == "psp" ? new SaveEmulatorInstallation("/app/ppsspp", false) : null)
            .ConnectGoogleDriveManagedAsync("EmuShelf/Saves", Overrides(), _ => { }, CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.ManagedTransportUnavailable, result);
    }

    [Fact]
    public async Task Connect_WithConfiguredDuckStation_DoesNotRequireOverrides()
    {
        var result = await CreateCoordinator(
                new FakeSettingsService(),
                emulators: systemId => systemId == "playstation"
                    ? new SaveEmulatorInstallation("/app/duckstation", false)
                    : null)
            .ConnectGoogleDriveManagedAsync("EmuShelf/Saves", Overrides(), _ => { }, CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.ManagedTransportUnavailable, result);
    }

    [Fact]
    public async Task Connect_WithFlatpakPpsspp_DoesNotRequireOverrides()
    {
        var result = await CreateCoordinator(
                new FakeSettingsService(),
                emulators: systemId => systemId == "psp" ? new SaveEmulatorInstallation(null, true) : null)
            .ConnectGoogleDriveManagedAsync("EmuShelf/Saves", Overrides(), _ => { }, CancellationToken.None);

        Assert.Equal(CloudSaveSyncConnectResult.ManagedTransportUnavailable, result);
    }

    [Fact]
    public void SettingsContext_ExposesOneRowPerRegisteredPlatform()
    {
        var context = CreateCoordinator(new FakeSettingsService()).CreateSettingsContext();

        Assert.Equal(
            SaveProviderRegistry.SystemIds,
            context.GetPlatforms().Select(platform => platform.SystemId).ToArray());
    }

    [Fact]
    public void UpdatingCloudSettingsPreservesAThemeChangedAfterCoordinatorStartup()
    {
        var initial = new AppSettings { Theme = ThemePreference.System };
        var settings = new FakeSettingsService { Current = initial };
        var coordinator = CreateCoordinator(settings, initial);
        settings.Save(initial with { Theme = ThemePreference.Dark });

        coordinator.UpdateOverride("playstation2", "/pcsx2");

        Assert.Equal(ThemePreference.Dark, settings.Current.Theme);
        Assert.Equal("/pcsx2", settings.Current.CloudSaveSync.GetOverride("playstation2"));
    }

    [Fact]
    public void CatalogIntegrityFailure_RetainsTheStableCloudFolderId()
    {
        Assert.False(CloudSaveSyncCoordinator.ShouldForgetCloudFolderIdAfter(
            new InvalidDataException("catalog is damaged")));
        Assert.True(CloudSaveSyncCoordinator.ShouldForgetCloudFolderIdAfter(
            new IOException("folder id is no longer reachable")));
    }

    [Fact]
    public void StatePhaseHint_IsEmpty_WhenStatesMatchedTheGame()
    {
        Assert.Equal(
            string.Empty,
            CloudSaveSyncCoordinator.StatePhaseHint(
                compatible: true, localStatesMatched: 2, folder: "/states/bsnes", folderStates: 5, folderWarning: null));
    }

    [Fact]
    public void StatePhaseHint_NamesTheIncompatibility_WhenCompatibilityCouldNotBeDetected()
    {
        var hint = CloudSaveSyncCoordinator.StatePhaseHint(
            compatible: false, localStatesMatched: 0, folder: "/states/bsnes", folderStates: 5, folderWarning: null);

        Assert.Contains("compatibilityDetected=false", hint);
    }

    [Fact]
    public void StatePhaseHint_DistinguishesAnEmptyFolderFromAnUnmatchedName()
    {
        // The exact confusion this replaced: a zero match in an empty folder means "nothing was made
        // yet", not "the names did not match" — and the message must say which of the two it is.
        var emptyFolder = CloudSaveSyncCoordinator.StatePhaseHint(
            compatible: true, localStatesMatched: 0, folder: "/states/bsnes", folderStates: 0, folderWarning: null);
        var unmatched = CloudSaveSyncCoordinator.StatePhaseHint(
            compatible: true, localStatesMatched: 0, folder: "/states/bsnes", folderStates: 5, folderWarning: null);

        Assert.Contains("no manual save states yet", emptyFolder);
        Assert.DoesNotContain("none matched", emptyFolder);
        Assert.Contains("5 manual save state(s)", unmatched);
        Assert.Contains("none matched", unmatched);
        Assert.Contains("/states/bsnes", unmatched);
    }

    [Fact]
    public void StatePhaseHint_SurfacesAnUnresolvedFolderAndItsWarning()
    {
        var hint = CloudSaveSyncCoordinator.StatePhaseHint(
            compatible: true,
            localStatesMatched: 0,
            folder: null,
            folderStates: 0,
            folderWarning: "The emulator configuration does not expose a safe folder for save states.");

        Assert.Contains("could not be resolved", hint);
        Assert.Contains("does not expose a safe folder", hint);
    }

    [Fact]
    public void StatePhaseHint_IncludesTheFolderNotYetCreatedWarning()
    {
        var hint = CloudSaveSyncCoordinator.StatePhaseHint(
            compatible: true,
            localStatesMatched: 0,
            folder: "/states/bsnes",
            folderStates: 0,
            folderWarning: "The folder does not exist yet.");

        Assert.Contains("no manual save states yet", hint);
        Assert.Contains("The folder does not exist yet.", hint);
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
                    TransportKind = CloudTransportKind.GoogleDrive,
                    CloudFolder = "EmuShelf/Saves",
                    Pcsx2ConfigDirectory = "/pcsx2",
                },
            },
        };
        var coordinator = CreateCoordinator(settings, settings.Current);

        // This build embeds no OAuth client, so the transport cannot be built and the pipeline takes
        // its catch path — the exact route where RecordOutcome used to throw a second time and escape.
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
            TransportKind = CloudTransportKind.GoogleDrive,
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
            TransportKind = CloudTransportKind.GoogleDrive,
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
