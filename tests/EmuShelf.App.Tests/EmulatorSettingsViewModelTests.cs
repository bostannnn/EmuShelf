using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Launching;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

public class EmulatorSettingsViewModelTests
{
    private readonly FakeDialogService _dialogs = new();
    private readonly RecordingConfigurationStore _configurations = new();

    [AvaloniaFact]
    public async Task Rows_UseIntegrationDefaultsAndSaveIndependentSystemConfigurations()
    {
        var viewModel = CreateViewModel();
        var gameCube = viewModel.Rows.Single(row => row.SystemId == "gamecube");
        var wii = viewModel.Rows.Single(row => row.SystemId == "wii");
        bool? closeResult = null;
        viewModel.CloseRequested += saved => closeResult = saved;

        Assert.Equal(11, viewModel.Rows.Count);
        Assert.Equal("Dolphin", gameCube.EmulatorName);
        Assert.Equal("Dolphin", wii.EmulatorName);
        Assert.Equal(gameCube.DefaultLaunchArguments, wii.DefaultLaunchArguments);

        gameCube.ExecutablePath = "/portable/Dolphin-GC";
        gameCube.LaunchArguments = "-b -e \"{GamePath}\" --config=GC";
        wii.ExecutablePath = "/portable/Dolphin-Wii";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(closeResult);
        Assert.Equal(1, _configurations.BatchSaveCalls);
        Assert.Equal("/portable/Dolphin-GC", _configurations.Saved["gamecube"].ExecutablePath);
        Assert.Equal("/portable/Dolphin-Wii", _configurations.Saved["wii"].ExecutablePath);
        Assert.Equal(
            "-b -e \"{GamePath}\" --config=GC",
            _configurations.Saved["gamecube"].LaunchArguments);
    }

    [AvaloniaFact]
    public async Task RetroArchRows_ShareTheExecutableButKeepIndependentCores()
    {
        _dialogs.LibretroCoreToReturn = "/portable/RetroArch/cores/melonds_libretro.dll";
        var viewModel = CreateViewModel();
        var megaDrive = viewModel.Rows.Single(row => row.SystemId == "megadrive");
        var ds = viewModel.Rows.Single(row => row.SystemId == "nds");
        var gba = viewModel.Rows.Single(row => row.SystemId == "gba");
        var dreamcast = viewModel.Rows.Single(row => row.SystemId == "dreamcast");

        Assert.True(megaDrive.IsExecutableShared);
        Assert.True(ds.IsExecutableShared);
        Assert.True(gba.IsExecutableShared);
        Assert.True(dreamcast.IsExecutableShared);
        Assert.True(ds.RequiresCorePath);
        Assert.Equal("RetroArch", ds.EmulatorName);

        megaDrive.ExecutablePath = "/portable/RetroArch/retroarch.exe";
        megaDrive.CorePath = "/portable/RetroArch/cores/genesis_plus_gx_libretro.dll";
        gba.CorePath = "/portable/RetroArch/cores/mgba_libretro.dll";
        await ds.BrowseCoreCommand.ExecuteAsync(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(megaDrive.ExecutablePath, ds.ExecutablePath);
        Assert.Equal(megaDrive.ExecutablePath, gba.ExecutablePath);
        Assert.Equal(megaDrive.ExecutablePath, dreamcast.ExecutablePath);
        Assert.Equal(megaDrive.ExecutablePath, _configurations.Saved["megadrive"].ExecutablePath);
        Assert.Equal(megaDrive.ExecutablePath, _configurations.Saved["nds"].ExecutablePath);
        Assert.Equal("/portable/RetroArch/cores/genesis_plus_gx_libretro.dll",
            _configurations.Saved["megadrive"].CorePath);
        Assert.Equal("/portable/RetroArch/cores/melonds_libretro.dll",
            _configurations.Saved["nds"].CorePath);
        Assert.Equal("/portable/RetroArch/cores/mgba_libretro.dll",
            _configurations.Saved["gba"].CorePath);
    }

    [AvaloniaFact]
    public async Task RetroArchCoreRow_ShowsTheFileNameAndCanClearOrReplaceTheCore()
    {
        _dialogs.LibretroCoreToReturn = "/portable/RetroArch/cores/melonds_libretro.dll";
        var viewModel = CreateViewModel();
        var row = viewModel.Rows.Single(candidate => candidate.SystemId == "nds");

        row.CorePath = "/portable/RetroArch/cores/old_core.dll";

        Assert.True(row.HasCorePath);
        Assert.Equal("old_core.dll", row.CoreFileName);
        Assert.True(row.ClearCoreCommand.CanExecute(null));

        row.ClearCoreCommand.Execute(null);

        Assert.False(row.HasCorePath);
        Assert.Equal("No core selected", row.CoreFileName);
        Assert.False(row.ClearCoreCommand.CanExecute(null));

        await row.BrowseCoreCommand.ExecuteAsync(null);

        Assert.Equal(_dialogs.LibretroCoreToReturn, row.CorePath);
        Assert.Equal("melonds_libretro.dll", row.CoreFileName);
    }

    [AvaloniaFact]
    public void RetroArchCoreRow_FiltersInstalledCoreOptionsWithoutChangingSelection()
    {
        var row = CreateViewModel().Rows.Single(candidate => candidate.SystemId == "gba");
        var selected = new EmulatorSettingsRowViewModel.LibretroCoreOption(
            "mgba_libretro.dll", "/portable/RetroArch/cores/mgba_libretro.dll");
        row.AvailableCores.Add(selected);
        row.AvailableCores.Add(new EmulatorSettingsRowViewModel.LibretroCoreOption(
            "vba_next_libretro.dll", "/portable/RetroArch/cores/vba_next_libretro.dll"));
        row.SelectedCore = selected;

        row.CoreSearchText = "vba";

        Assert.Single(row.FilteredCores);
        Assert.Equal("vba_next_libretro.dll", row.FilteredCores[0].Name);
        Assert.Same(selected, row.SelectedCore);
        Assert.Equal(selected.Path, row.CorePath);
    }

    [AvaloniaFact]
    public void RetroArchCores_AreDiscoveredFromTheUserRetroArchConfigDirectoryOffWindows()
    {
        // A Linux/SteamOS RetroArch (native or AppImage) keeps cores under the user's config
        // directory, not beside the executable, so the adjacent-only scan would leave the picker
        // empty. Windows keeps only the adjacent scan.
        var root = Path.Combine(Path.GetTempPath(), "EmuShelfCoreDiscovery", Guid.NewGuid().ToString("N"));
        var emulatorDirectory = Path.Combine(root, "emulator");
        var configHome = Path.Combine(root, "config");
        var coresDirectory = Path.Combine(configHome, "retroarch", "cores");
        Directory.CreateDirectory(emulatorDirectory);
        Directory.CreateDirectory(coresDirectory);
        File.WriteAllText(Path.Combine(coresDirectory, "genesis_plus_gx_libretro.so"), "core");

        var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", configHome);
        try
        {
            var viewModel = CreateViewModel();
            var megaDrive = viewModel.Rows.Single(row => row.SystemId == "megadrive");
            megaDrive.ExecutablePath = Path.Combine(emulatorDirectory, "retroarch");

            var discovered = megaDrive.AvailableCores.Any(core => core.Name == "genesis_plus_gx_libretro.so");
            Assert.Equal(!OperatingSystem.IsWindows(), discovered);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public async Task BrowseAndResetCommands_UpdateTheEditableRow()
    {
        _dialogs.EmulatorExecutableToReturn = "/portable/duckstation.exe";
        var viewModel = CreateViewModel();
        var row = viewModel.Rows.Single(candidate => candidate.SystemId == "playstation");
        row.LaunchArguments = "custom";

        await row.BrowseCommand.ExecuteAsync(null);
        row.ResetArgumentsCommand.Execute(null);

        Assert.Equal("/portable/duckstation.exe", row.ExecutablePath);
        Assert.Equal(row.DefaultLaunchArguments, row.LaunchArguments);
    }

    [AvaloniaFact]
    public void PspRow_UsesPpssppAndTheSingleGamePathArgumentTemplate()
    {
        var viewModel = CreateViewModel();
        var psp = viewModel.Rows.Single(row => row.SystemId == "psp");

        Assert.Equal("PPSSPP", psp.EmulatorName);
        Assert.Equal("\"{GamePath}\"", psp.DefaultLaunchArguments);
        Assert.False(psp.RequiresCorePath);
    }

    [AvaloniaFact]
    public async Task LibraryMaintenance_RunsInsideSettingsAndReportsResult()
    {
        var rescannedSystems = new List<string>();
        var allCalls = 0;
        var maintenance = new LibraryMaintenanceActions(
            systemId =>
            {
                rescannedSystems.Add(systemId);
                return Task.FromResult($"{systemId} rescan complete");
            },
            () =>
            {
                allCalls++;
                return Task.FromResult("All console folders rescanned");
            });
        var viewModel = CreateViewModel(maintenance);
        var playStation = viewModel.Rows.Single(row => row.SystemId == "playstation");

        Assert.All(viewModel.Rows, row => Assert.False(row.IsExpanded));
        await playStation.RescanLibraryCommand.ExecuteAsync(null);
        Assert.Equal(["playstation"], rescannedSystems);
        Assert.Equal("playstation rescan complete", playStation.MaintenanceStatusText);

        await viewModel.RescanAllCommand.ExecuteAsync(null);
        Assert.Equal(1, allCalls);
        Assert.Equal("All console folders rescanned", viewModel.MaintenanceStatusText);
    }

    [AvaloniaFact]
    public async Task PlayStation3Row_ExposesTheExplicitRpcs3LibrarySyncOnly()
    {
        var calls = 0;
        var maintenance = new LibraryMaintenanceActions(
            _ => Task.FromResult("unused"),
            () => Task.FromResult("unused"),
            SyncRpcs3Library: () =>
            {
                calls++;
                return Task.FromResult("RPCS3 library sync complete — 1 added");
            });
        var viewModel = CreateViewModel(maintenance);
        var playStation3 = viewModel.Rows.Single(row => row.SystemId == "playstation3");
        var playStation2 = viewModel.Rows.Single(row => row.SystemId == "playstation2");

        Assert.True(playStation3.HasSyncLibrary);
        Assert.True(playStation3.CanSyncLibrary);
        Assert.False(playStation3.HasRescanLibrary);
        Assert.False(playStation2.HasSyncLibrary);

        await playStation3.SyncLibraryCommand.ExecuteAsync(null);

        Assert.Equal(1, calls);
        Assert.Equal("RPCS3 library sync complete — 1 added", playStation3.MaintenanceStatusText);
    }

    [AvaloniaFact]
    public async Task RetroAchievements_Connect_UpdatesStateAndRunsPipeline()
    {
        var calls = new List<(string User, string Key)>();
        var context = new RetroAchievementsSettingsContext(
            CurrentAccount: null,
            IsConnected: false,
            ConnectAsync: (user, key, _, _) =>
            {
                calls.Add((user, key));
                return Task.FromResult(new RetroAchievementsConnectionSummary(
                    RetroAchievementsConnectionResult.Connected));
            },
            DisconnectAsync: _ => Task.CompletedTask);
        var viewModel = CreateViewModel(retroAchievements: context);
        viewModel.RetroAchievementsUsername = "Player";
        viewModel.RetroAchievementsApiKey = "SECRET";

        await viewModel.ConnectRetroAchievementsCommand.ExecuteAsync(null);

        Assert.Equal(("Player", "SECRET"), Assert.Single(calls));
        Assert.True(viewModel.IsRetroAchievementsConnected);
        Assert.Equal("Player", viewModel.ConnectedAccountName);
        Assert.Equal(string.Empty, viewModel.RetroAchievementsApiKey); // key not kept in the form
        Assert.Contains(SettingsSection.RetroAchievements, viewModel.Sections);
    }

    [AvaloniaFact]
    public async Task RetroAchievements_Connect_DescribesCurrentSyncWorkAndReusedResults()
    {
        var sync = new RetroAchievementsLibrarySyncSummary(
            new RetroAchievementsIdentificationSummary(
                Processed: 7,
                Reused: 1,
                Hashed: 5,
                Unsupported: 0,
                Failed: 1),
            Matching: null,
            Progress: null);
        var context = new RetroAchievementsSettingsContext(
            CurrentAccount: null,
            IsConnected: false,
            ConnectAsync: (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.Connected,
                sync)),
            DisconnectAsync: _ => Task.CompletedTask);
        var viewModel = CreateViewModel(retroAchievements: context);
        viewModel.RetroAchievementsUsername = "Player";
        viewModel.RetroAchievementsApiKey = "SECRET";

        await viewModel.ConnectRetroAchievementsCommand.ExecuteAsync(null);

        Assert.Equal(
            "Connected. 5 hashes calculated this sync, 1 prior result reused, 1 unreadable or invalid; " +
            "matching unavailable; progress refresh unavailable.",
            viewModel.RetroAchievementsStatusText);
    }

    [AvaloniaFact]
    public async Task RetroAchievements_ConnectAuthFailure_ReportsWithoutConnecting()
    {
        var context = new RetroAchievementsSettingsContext(
            null,
            false,
            (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.AuthenticationFailed)),
            _ => Task.CompletedTask);
        var viewModel = CreateViewModel(retroAchievements: context);
        viewModel.RetroAchievementsUsername = "Player";
        viewModel.RetroAchievementsApiKey = "WRONG";

        await viewModel.ConnectRetroAchievementsCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRetroAchievementsConnected);
        Assert.Contains("wasn't accepted", viewModel.RetroAchievementsStatusText);
    }

    [AvaloniaFact]
    public void RetroAchievements_SessionOnlyCredentialAfterRestart_RequiresReconnect()
    {
        var context = new RetroAchievementsSettingsContext(
            new RetroAchievementsAccount("Player", "ULID-9"),
            IsConnected: false,
            ConnectAsync: (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.Connected)),
            DisconnectAsync: _ => Task.CompletedTask);

        var viewModel = CreateViewModel(retroAchievements: context);

        Assert.False(viewModel.IsRetroAchievementsConnected);
        Assert.True(viewModel.IsRetroAchievementsDisconnected);
        Assert.Equal("Player", viewModel.RetroAchievementsUsername);
        Assert.Contains("Reconnect required", viewModel.RetroAchievementsStatusText);
    }

    [AvaloniaFact]
    public void RetroAchievements_ProgressNamesTheGameCurrentlyBeingIdentified()
    {
        var viewModel = CreateViewModel();
        viewModel.IsRetroAchievementsBusy = true;

        viewModel.ApplyRetroAchievementsProgress(new RetroAchievementsLibrarySyncProgress(
            RetroAchievementsLibrarySyncPhase.Identifying,
            Completed: 2,
            Total: 7,
            CurrentGameTitle: "Metal Gear Solid"));

        Assert.True(viewModel.HasRetroAchievementsProgress);
        Assert.Equal(2, viewModel.RetroAchievementsProgressCompleted);
        Assert.Equal(7, viewModel.RetroAchievementsProgressTotal);
        Assert.Equal("Identifying 3 of 7: Metal Gear Solid", viewModel.RetroAchievementsProgressText);
    }

    [AvaloniaFact]
    public async Task RetroAchievements_ConnectWithEmptyFields_DoesNotCallPipeline()
    {
        var calls = 0;
        var context = new RetroAchievementsSettingsContext(
            null,
            false,
            (_, _, _, _) =>
            {
                calls++;
                return Task.FromResult(new RetroAchievementsConnectionSummary(
                    RetroAchievementsConnectionResult.Connected));
            },
            _ => Task.CompletedTask);
        var viewModel = CreateViewModel(retroAchievements: context);

        await viewModel.ConnectRetroAchievementsCommand.ExecuteAsync(null);

        Assert.Equal(0, calls);
        Assert.Contains("Enter your username", viewModel.RetroAchievementsStatusText);
    }

    [AvaloniaFact]
    public async Task RetroAchievements_Disconnect_ClearsStateAndRunsPipeline()
    {
        var disconnects = 0;
        var context = new RetroAchievementsSettingsContext(
            new RetroAchievementsAccount("Player", "ULID-9"),
            true,
            (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.Connected)),
            _ =>
            {
                disconnects++;
                return Task.CompletedTask;
            });
        var viewModel = CreateViewModel(retroAchievements: context);
        Assert.True(viewModel.IsRetroAchievementsConnected); // seeded from the existing account

        await viewModel.DisconnectRetroAchievementsCommand.ExecuteAsync(null);

        Assert.Equal(1, disconnects);
        Assert.False(viewModel.IsRetroAchievementsConnected);
    }

    [AvaloniaFact]
    public async Task RetroAchievements_RefreshMatches_RunsTheDedicatedMaintenanceAction()
    {
        var refreshes = 0;
        var context = new RetroAchievementsSettingsContext(
            new RetroAchievementsAccount("Player", "ULID-9"),
            IsConnected: true,
            ConnectAsync: (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.Connected)),
            DisconnectAsync: _ => Task.CompletedTask,
            RefreshMatchesAsync: (_, _) =>
            {
                refreshes++;
                return Task.FromResult<RetroAchievementsLibrarySyncSummary?>(
                    new RetroAchievementsLibrarySyncSummary(
                        new RetroAchievementsIdentificationSummary(3, 2, 0, 0, 0),
                        new RetroAchievementsMatchSummary(3, 2, 1, 0, 0),
                        new RetroAchievementsProgressRefreshSummary(
                            2,
                            2,
                            RetroAchievementsRequestStatus.Success)));
            });
        var viewModel = CreateViewModel(retroAchievements: context);

        Assert.True(viewModel.CanRefreshRetroAchievementsMatches);
        await viewModel.RefreshRetroAchievementsMatchesCommand.ExecuteAsync(null);

        Assert.Equal(1, refreshes);
        Assert.Equal(
            "Achievement matches refreshed. 2 cached results reused; 2 matched, 1 without achievements, " +
            "0 unresolved; 2 progress summaries refreshed.",
            viewModel.RetroAchievementsStatusText);
    }

    [AvaloniaFact]
    public void Sections_WithoutRetroAchievementsContext_OmitThatSection()
    {
        var viewModel = CreateViewModel();

        Assert.DoesNotContain(SettingsSection.RetroAchievements, viewModel.Sections);
        Assert.False(viewModel.HasRetroAchievements);
    }

    [AvaloniaFact]
    public void Sections_WithCloudSavesContext_IncludesSavesSection()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext());

        Assert.Contains(SettingsSection.Saves, viewModel.Sections);
        Assert.True(viewModel.HasCloudSaves);
    }

    [AvaloniaFact]
    public void Sections_WithoutCloudSavesContext_OmitSavesSection()
    {
        var viewModel = CreateViewModel();

        Assert.DoesNotContain(SettingsSection.Saves, viewModel.Sections);
        Assert.False(viewModel.HasCloudSaves);
    }

    [AvaloniaFact]
    public void CloudSaves_SeededFromConnectedSettings_ShowConnectedState()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(new CloudSaveSyncSettings
        {
            Enabled = true,
            RemoteName = "my-drive",
            CloudFolder = "Saves",
            Pcsx2ConfigDirectory = "/pcsx2",
        }));

        Assert.True(viewModel.IsCloudConnected);
        Assert.Equal("my-drive", viewModel.CloudRemoteName);
        Assert.Equal("/pcsx2", viewModel.Pcsx2ConfigDirectory);
    }

    [AvaloniaFact]
    public async Task CloudSaves_Connect_Success_MarksConnectedAndPassesFields()
    {
        var calls = new List<(string Remote, string Folder, string Pcsx2)>();
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(connect: (remote, folder, pcsx2, _) =>
        {
            calls.Add((remote, folder, pcsx2));
            return Task.FromResult(CloudSaveSyncConnectResult.Connected);
        }));
        viewModel.CloudRemoteName = "my-drive";
        viewModel.CloudFolder = "Saves";
        viewModel.Pcsx2ConfigDirectory = "/pcsx2";

        await viewModel.ConnectCloudCommand.ExecuteAsync(null);

        Assert.Equal(("my-drive", "Saves", "/pcsx2"), Assert.Single(calls));
        Assert.True(viewModel.IsCloudConnected);
        Assert.Contains("Connected", viewModel.CloudStatusText);
    }

    [AvaloniaFact]
    public async Task CloudSaves_Connect_RcloneMissing_StaysDisconnected()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            connect: (_, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.RcloneMissing)));

        await viewModel.ConnectCloudCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsCloudConnected);
        Assert.Contains("rclone", viewModel.CloudStatusText);
    }

    [AvaloniaFact]
    public async Task CloudSaves_SyncNow_ReportsCompletedSummary()
    {
        var report = new SaveSyncReport(
        [
            new SaveUnitSyncResult("pcsx2/Mcd001.ps2", SaveSyncAction.Upload, "up"),
            new SaveUnitSyncResult("pcsx2/Mcd002.ps2", SaveSyncAction.Upload, "up"),
        ]);
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            syncNow: (_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(report))));

        await viewModel.SyncCloudNowCommand.ExecuteAsync(null);

        Assert.Contains("2 uploaded", viewModel.CloudStatusText);
    }

    [AvaloniaFact]
    public void CloudSaves_PreFillsPcsx2DirectoryFromConfiguredEmulator()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            defaultPcsx2Directory: @"F:\ES-DE\Emulators\pcsx2-qt"));

        Assert.Equal(@"F:\ES-DE\Emulators\pcsx2-qt", viewModel.Pcsx2ConfigDirectory);
    }

    [AvaloniaFact]
    public void CloudSaves_SavedPcsx2DirectoryWinsOverTheDerivedDefault()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            current: new CloudSaveSyncSettings { Pcsx2ConfigDirectory = "/saved/pcsx2" },
            defaultPcsx2Directory: "/derived/pcsx2"));

        Assert.Equal("/saved/pcsx2", viewModel.Pcsx2ConfigDirectory);
    }

    [AvaloniaFact]
    public void CloudSaves_WhenRcloneMissing_FlagsItInTheViewModel()
    {
        Assert.False(CreateViewModel(cloudSaves: CreateCloudContext(rcloneAvailable: true)).IsRcloneMissing);
        Assert.True(CreateViewModel(cloudSaves: CreateCloudContext(rcloneAvailable: false)).IsRcloneMissing);
    }

    [AvaloniaFact]
    public async Task CloudSaves_DownloadRclone_Success_ClearsTheMissingWarning()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            rcloneAvailable: false,
            downloadRclone: _ => Task.FromResult(true)));
        Assert.True(viewModel.IsRcloneMissing);

        await viewModel.DownloadRcloneCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRcloneMissing);
        Assert.Contains("installed", viewModel.CloudStatusText);
    }

    [AvaloniaFact]
    public async Task CloudSaves_DownloadRclone_Failure_KeepsTheWarning()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            rcloneAvailable: false,
            downloadRclone: _ => Task.FromResult(false)));

        await viewModel.DownloadRcloneCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsRcloneMissing);
        Assert.Contains("Couldn't download", viewModel.CloudStatusText);
    }

    [AvaloniaFact]
    public async Task CloudSaves_PickPcsx2Directory_PersistsAndDetectsMemoryCards()
    {
        string? persisted = null;
        _dialogs.FolderToReturn = "/picked/pcsx2";
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(updateDirectory: dir => persisted = dir));

        await viewModel.PickPcsx2DirectoryCommand.ExecuteAsync(null);

        Assert.Equal("/picked/pcsx2", viewModel.Pcsx2ConfigDirectory);
        Assert.Equal("/picked/pcsx2", persisted); // change is saved even without reconnecting
        Assert.Equal("/pcsx2/memcards", viewModel.DetectedMemoryCardsDirectory);
    }

    [AvaloniaFact]
    public async Task CloudSaves_ForceDownload_CallsContextWithDownloadDirection()
    {
        SaveSyncDirection? captured = null;
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(force: (direction, _, _) =>
        {
            captured = direction;
            return Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])));
        }));

        await viewModel.ForceCloudDownloadCommand.ExecuteAsync(null);

        Assert.Equal(SaveSyncDirection.Download, captured);
    }

    private EmulatorSettingsViewModel CreateViewModel(
        LibraryMaintenanceActions? maintenance = null,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null) => new(
        KnownSystems.All,
        KnownEmulators.All,
        KnownSystems.All.ToDictionary(
            system => system.Id,
            _ => (EmulatorConfiguration?)null,
            StringComparer.Ordinal),
        _configurations,
        _dialogs,
        maintenance,
        retroAchievements: retroAchievements,
        cloudSaves: cloudSaves);

    private static CloudSaveSyncSettingsContext CreateCloudContext(
        CloudSaveSyncSettings? current = null,
        Func<string, string, string, CancellationToken, Task<CloudSaveSyncConnectResult>>? connect = null,
        Func<IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>>? syncNow = null,
        Func<SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>>? force = null,
        Action<string?>? updateDirectory = null,
        bool rcloneAvailable = true,
        Func<CancellationToken, Task<bool>>? downloadRclone = null,
        string? defaultPcsx2Directory = null) => new(
        current ?? new CloudSaveSyncSettings(),
        rcloneAvailable,
        "/app/rclone",
        defaultPcsx2Directory,
        _ => Task.FromResult<string?>("/pcsx2/memcards"),
        connect ?? ((_, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.Connected)),
        _ => Task.CompletedTask,
        syncNow ?? ((_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])))),
        force ?? ((_, _, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])))),
        updateDirectory ?? (_ => { }),
        downloadRclone ?? (_ => Task.FromResult(true)));

    private sealed class RecordingConfigurationStore : IEmulatorConfigurationStore
    {
        public Dictionary<string, EmulatorConfiguration> Saved { get; } =
            new(StringComparer.Ordinal);
        public int BatchSaveCalls { get; private set; }

        public EmulatorConfiguration? Get(string systemId) =>
            Saved.GetValueOrDefault(systemId);

        public void Save(EmulatorConfiguration configuration) =>
            Saved[configuration.SystemId] = configuration;

        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
        {
            BatchSaveCalls++;
            foreach (var configuration in configurations)
                Save(configuration);
        }
    }
}
