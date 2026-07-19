using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Launching;
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

        Assert.Equal(9, viewModel.Rows.Count);
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

        Assert.True(megaDrive.IsExecutableShared);
        Assert.True(ds.IsExecutableShared);
        Assert.True(gba.IsExecutableShared);
        Assert.True(ds.RequiresCorePath);
        Assert.Equal("RetroArch", ds.EmulatorName);

        megaDrive.ExecutablePath = "/portable/RetroArch/retroarch.exe";
        megaDrive.CorePath = "/portable/RetroArch/cores/genesis_plus_gx_libretro.dll";
        gba.CorePath = "/portable/RetroArch/cores/mgba_libretro.dll";
        await ds.BrowseCoreCommand.ExecuteAsync(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(megaDrive.ExecutablePath, ds.ExecutablePath);
        Assert.Equal(megaDrive.ExecutablePath, gba.ExecutablePath);
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

        Assert.True(playStation.IsExpanded);
        Assert.All(viewModel.Rows.Skip(1), row => Assert.False(row.IsExpanded));
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
    public void Sections_WithoutRetroAchievementsContext_OmitThatSection()
    {
        var viewModel = CreateViewModel();

        Assert.DoesNotContain(SettingsSection.RetroAchievements, viewModel.Sections);
        Assert.False(viewModel.HasRetroAchievements);
    }

    private EmulatorSettingsViewModel CreateViewModel(
        LibraryMaintenanceActions? maintenance = null,
        RetroAchievementsSettingsContext? retroAchievements = null) => new(
        KnownSystems.All,
        KnownEmulators.All,
        KnownSystems.All.ToDictionary(
            system => system.Id,
            _ => (EmulatorConfiguration?)null,
            StringComparer.Ordinal),
        _configurations,
        _dialogs,
        maintenance,
        retroAchievements: retroAchievements);

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
