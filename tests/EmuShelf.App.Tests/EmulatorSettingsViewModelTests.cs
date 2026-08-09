using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
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

        Assert.Equal(15, viewModel.Rows.Count);
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
    public void PlayStationRow_OffersDuckStationAndRetroArchProfiles()
    {
        var ps1 = CreateViewModel().Rows.Single(row => row.SystemId == "playstation");

        Assert.True(ps1.HasMultipleProfiles);
        Assert.Equal(
            ["DuckStation", "RetroArch"],
            ps1.AvailableProfiles.Select(profile => profile.EmulatorName));
        // The default (first-supporting) profile is active, so single-emulator behavior is unchanged.
        Assert.Equal("DuckStation", ps1.EmulatorName);
        Assert.False(ps1.RequiresCorePath);
    }

    [AvaloniaFact]
    public async Task PlayStationRow_SwitchingToRetroArchKeepsBothProfilesAndPinsTheActiveOne()
    {
        var viewModel = CreateViewModel();
        var ps1 = viewModel.Rows.Single(row => row.SystemId == "playstation");

        // Configure DuckStation, then switch the profile to RetroArch and configure its core.
        ps1.ExecutablePath = "/portable/DuckStation/duckstation";
        ps1.SelectedProfile = ps1.AvailableProfiles.Single(profile => profile.EmulatorId == "retroarch");

        Assert.Equal("RetroArch", ps1.EmulatorName);
        Assert.True(ps1.RequiresCorePath);
        ps1.ExecutablePath = "/portable/RetroArch/retroarch";
        ps1.CorePath = "/portable/RetroArch/cores/swanstation_libretro.dll";

        await viewModel.SaveCommand.ExecuteAsync(null);

        // Both profiles persist, keyed by their own emulator id, and RetroArch is the active one.
        var playStationProfiles = _configurations.AllSaved
            .Where(configuration => configuration.SystemId == "playstation")
            .ToList();
        Assert.Contains(playStationProfiles, configuration =>
            configuration.EmulatorId == "duckstation" &&
            configuration.ExecutablePath == "/portable/DuckStation/duckstation");
        Assert.Contains(playStationProfiles, configuration =>
            configuration.EmulatorId == "retroarch" &&
            configuration.CorePath == "/portable/RetroArch/cores/swanstation_libretro.dll");
        Assert.Equal("retroarch", _configurations.ActiveEmulators["playstation"]);

        // Switching back restores the DuckStation draft rather than showing empty fields.
        ps1.SelectedProfile = ps1.AvailableProfiles.Single(profile => profile.EmulatorId == "duckstation");
        Assert.Equal("/portable/DuckStation/duckstation", ps1.ExecutablePath);
    }

    [AvaloniaFact]
    public void DataFolder_IsSurfacedFromMaintenance_WhenProvided()
    {
        var maintenance = new LibraryMaintenanceActions(
            RescanSystem: (_, _) => Task.FromResult(string.Empty),
            RescanAll: _ => Task.FromResult(string.Empty),
            DataDirectory: "/portable/EmuShelf");

        var viewModel = CreateViewModel(maintenance: maintenance);

        Assert.True(viewModel.HasDataDirectory);
        Assert.Equal("/portable/EmuShelf", viewModel.DataDirectory);
    }

    [AvaloniaFact]
    public void DataFolder_IsHidden_WhenMaintenanceOmitsIt()
    {
        var viewModel = CreateViewModel();

        Assert.False(viewModel.HasDataDirectory);
        Assert.Equal(string.Empty, viewModel.DataDirectory);
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
    public async Task RetroArchRows_MigrateAnExistingSharedDirectTargetToFlatpakTogether()
    {
        var emulator = KnownEmulators.All.Single(candidate => candidate.Id == "retroarch");
        const string executable = "/portable/RetroArch/retroarch";
        var configured = KnownSystems.All.ToDictionary(
            system => system.Id,
            system => emulator.Supports(system.Id)
                ? new EmulatorConfiguration(system.Id, executable, emulator.DefaultLaunchArguments)
                {
                    LaunchTarget = new DirectExecutableTarget(executable),
                    EmulatorId = emulator.Id,
                    EmulatorInstallationId = emulator.Id,
                }
                : null,
            StringComparer.Ordinal);
        var viewModel = CreateViewModel(configured: configured);
        var megaDrive = viewModel.Rows.Single(row => row.SystemId == "megadrive");

        megaDrive.TargetKind = "Flatpak";
        megaDrive.FlatpakAppId = "org.libretro.RetroArch";

        var retroArchRows = viewModel.Rows.Where(row => row.EmulatorId == emulator.Id).ToArray();
        Assert.All(retroArchRows, row => Assert.Equal("Flatpak", row.TargetKind));
        Assert.All(retroArchRows, row => Assert.Equal("org.libretro.RetroArch", row.FlatpakAppId));

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.StatusText);
        Assert.Equal(1, _configurations.BatchSaveCalls);
        Assert.All(
            retroArchRows,
            row => Assert.Equal(
                new FlatpakApplicationTarget("org.libretro.RetroArch"),
                _configurations.Saved[row.SystemId].LaunchTarget));
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
    public void RetroArchCores_AreDiscoveredFromTheXdgConfigDirectoryOnLinux()
    {
        // A Linux/SteamOS RetroArch (native or AppImage) keeps cores under the user's XDG config
        // directory, not beside the executable, so the adjacent-only scan would leave the picker
        // empty. Windows keeps only the adjacent scan; macOS uses Application Support (covered below),
        // so the XDG path is Linux-only.
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
            var emulator = KnownEmulators.All.Single(candidate => candidate.Id == "retroarch");
            var system = KnownSystems.All.Single(candidate => candidate.Id == "megadrive");
            var megaDrive = new EmulatorSettingsRowViewModel(
                system, emulator, null, _dialogs, homeDirectory: Path.Combine(root, "home"));
            megaDrive.ExecutablePath = Path.Combine(emulatorDirectory, "retroarch");

            var discovered = megaDrive.AvailableCores.Any(core => core.Name == "genesis_plus_gx_libretro.so");
            Assert.Equal(!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS(), discovered);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void RetroArchCores_AreDiscoveredFromApplicationSupportOnMacOS()
    {
        // macOS RetroArch keeps downloaded cores under ~/Library/Application Support/RetroArch/cores,
        // not beside the `.app` and not under XDG — so a bundle executable path still populates the
        // core picker. (Regression guard: this directory was previously ignored on macOS.)
        var root = Path.Combine(Path.GetTempPath(), "EmuShelfMacCoreDiscovery", Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var coresDirectory = Path.Combine(home, "Library", "Application Support", "RetroArch", "cores");
        Directory.CreateDirectory(coresDirectory);
        File.WriteAllText(Path.Combine(coresDirectory, "mgba_libretro.dylib"), "core");

        try
        {
            var emulator = KnownEmulators.All.Single(candidate => candidate.Id == "retroarch");
            var system = KnownSystems.All.Single(candidate => candidate.Id == "gba");
            var row = new EmulatorSettingsRowViewModel(
                system, emulator, null, _dialogs, homeDirectory: home)
            {
                ExecutablePath = "/Applications/RetroArch.app",
            };

            var discovered = row.AvailableCores.Any(core => core.Name == "mgba_libretro.dylib");
            Assert.Equal(OperatingSystem.IsMacOS(), discovered);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [AvaloniaFact]
    public void RetroArchFlatpakTargetPicker_IsVisibleOnlyOnLinux()
    {
        var emulator = KnownEmulators.All.Single(candidate => candidate.Id == "retroarch");
        var system = KnownSystems.All.Single(candidate => candidate.Id == "megadrive");
        var row = new EmulatorSettingsRowViewModel(system, emulator, null, _dialogs);

        Assert.Equal(OperatingSystem.IsLinux(), row.CanSelectFlatpakTarget);
        Assert.Equal(OperatingSystem.IsLinux(), row.IsLaunchTargetPickerVisible);
    }

    [AvaloniaFact]
    public void RetroArchFlatpakCores_AreDiscoveredFromConfiguredAppIdWithoutExecutablePath()
    {
        var home = Path.Combine(
            Path.GetTempPath(),
            "EmuShelfFlatpakCoreDiscovery",
            Guid.NewGuid().ToString("N"));
        const string firstAppId = "org.example.RetroArch";
        const string forkAppId = "org.example.RetroArchFork";
        var firstCoresDirectory = Path.Combine(
            home, ".var", "app", firstAppId, "config", "retroarch", "cores");
        var forkCoresDirectory = Path.Combine(
            home, ".var", "app", forkAppId, "config", "retroarch", "cores");
        Directory.CreateDirectory(firstCoresDirectory);
        Directory.CreateDirectory(forkCoresDirectory);
        File.WriteAllText(Path.Combine(firstCoresDirectory, "genesis_plus_gx_libretro.so"), "core");
        File.WriteAllText(Path.Combine(forkCoresDirectory, "picodrive_libretro.so"), "core");

        try
        {
            var emulator = KnownEmulators.All.Single(candidate => candidate.Id == "retroarch");
            var system = KnownSystems.All.Single(candidate => candidate.Id == "megadrive");
            var row = new EmulatorSettingsRowViewModel(
                system,
                emulator,
                null,
                _dialogs,
                homeDirectory: home);

            Assert.Equal(string.Empty, row.ExecutablePath);
            Assert.Empty(row.AvailableCores);

            row.FlatpakAppId = firstAppId;
            row.TargetKind = "Flatpak";

            var firstCore = Assert.Single(row.AvailableCores);
            Assert.Equal("genesis_plus_gx_libretro.so", firstCore.Name);
            Assert.Equal(Path.Combine(firstCoresDirectory, firstCore.Name), firstCore.Path);

            row.FlatpakAppId = forkAppId;

            var forkCore = Assert.Single(row.AvailableCores);
            Assert.Equal("picodrive_libretro.so", forkCore.Name);
            Assert.Equal(Path.Combine(forkCoresDirectory, forkCore.Name), forkCore.Path);
        }
        finally
        {
            try { Directory.Delete(home, true); } catch (IOException) { }
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
    public void EmulatorTargets_AreLimitedToTheCurrentPlatformAndLegacyFlatpaksCanMigrate()
    {
        var emulator = KnownEmulators.All.Single(candidate => candidate.Id == "duckstation");
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation");
        var legacyFlatpak = new EmulatorConfiguration(system.Id, null, emulator.DefaultLaunchArguments)
        {
            LaunchTarget = new FlatpakApplicationTarget("org.example.DuckStation"),
        };
        var row = new EmulatorSettingsRowViewModel(system, emulator, legacyFlatpak, _dialogs);

        Assert.Equal(OperatingSystem.IsLinux(), row.CanSelectFlatpakTarget);
        Assert.Equal(!OperatingSystem.IsLinux(), row.IsUnsupportedFlatpakTarget);

        if (OperatingSystem.IsLinux())
            return;

        Assert.False(row.IsLaunchTargetPickerVisible);
        Assert.Equal("EXECUTABLE", row.DirectTargetLabel);
        Assert.Contains("cannot run on Windows", row.UnsupportedFlatpakTargetMessage);

        row.UseDirectTargetCommand.Execute(null);

        Assert.True(row.IsDirectTarget);
        Assert.False(row.IsUnsupportedFlatpakTarget);
        Assert.Null(row.ToConfiguration().LaunchTarget);
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
            (systemId, _) =>
            {
                rescannedSystems.Add(systemId);
                return Task.FromResult($"{systemId} rescan complete");
            },
            _ =>
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
    public async Task RescanAll_SurfacesLivePerConsoleProgress_ThenTheFinalResult()
    {
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            progress =>
            {
                progress.Report("Rescanning PlayStation… 12 found");
                progress.Report("Rescanning GameCube… 4 found");
                return Task.FromResult("Rescan added 3 game(s)");
            });
        var viewModel = CreateViewModel(maintenance);
        var seen = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EmulatorSettingsViewModel.MaintenanceStatusText))
                seen.Add(viewModel.MaintenanceStatusText);
        };

        await viewModel.RescanAllCommand.ExecuteAsync(null);

        // The live per-console counts were surfaced as the scan walked each console…
        Assert.Contains("Rescanning PlayStation… 12 found", seen);
        Assert.Contains("Rescanning GameCube… 4 found", seen);
        // …and the run's outcome is the final line, not a stale progress update.
        Assert.Equal("Rescan added 3 game(s)", viewModel.MaintenanceStatusText);
    }

    [AvaloniaFact]
    public async Task EmulatorRow_ListsAndManagesEveryRememberedFolder()
    {
        var first = Path.Combine(Path.GetTempPath(), "emushelf-roms-first");
        var second = Path.Combine(Path.GetTempPath(), "emushelf-roms-second");
        var replacement = Path.Combine(Path.GetTempPath(), "emushelf-roms-replacement");
        var added = Path.Combine(Path.GetTempPath(), "emushelf-roms-added");
        var folders = new List<LibraryFolder>
        {
            new() { Id = 1, SystemId = "playstation", Path = first },
            new() { Id = 2, SystemId = "playstation", Path = second },
        };
        var nextId = 3L;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult("unused"),
            _ => Task.FromResult("unused"),
            Folders: new LibraryFolderManagementActions(
                systemId => folders.Where(folder => folder.SystemId == systemId).ToArray(),
                (systemId, path) =>
                {
                    folders.Add(new LibraryFolder { Id = nextId++, SystemId = systemId, Path = path });
                    return Task.FromResult("Folder remembered.");
                },
                (systemId, id, path) =>
                {
                    var index = folders.FindIndex(folder => folder.Id == id && folder.SystemId == systemId);
                    folders[index] = folders[index] with { Path = path };
                    return Task.FromResult("Folder changed.");
                },
                (systemId, id) =>
                {
                    folders.RemoveAll(folder => folder.Id == id && folder.SystemId == systemId);
                    return Task.FromResult("Folder forgotten.");
                }));
        var viewModel = CreateViewModel(maintenance);
        var row = viewModel.Rows.Single(candidate => candidate.SystemId == "playstation");

        Assert.Equal([first, second], row.LibraryFolders.Select(folder => folder.Path));
        _dialogs.FolderToReturn = replacement;
        await row.LibraryFolders[0].ChangeCommand.ExecuteAsync(null);
        Assert.Equal(replacement, row.LibraryFolders[0].Path);

        _dialogs.FolderToReturn = added;
        await row.AddLibraryFolderCommand.ExecuteAsync(null);
        Assert.Equal(3, row.LibraryFolders.Count);
        await row.LibraryFolders.Single(folder => folder.Path == second).ForgetCommand.ExecuteAsync(null);
        Assert.DoesNotContain(row.LibraryFolders, folder => folder.Path == second);
    }

    [AvaloniaFact]
    public async Task FolderOperation_BlocksTheWholeSettingsDialogUntilItFinishes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult("unused"),
            _ => Task.FromResult("unused"),
            Folders: new LibraryFolderManagementActions(
                _ => [],
                async (_, _) =>
                {
                    started.SetResult();
                    await release.Task;
                    return "Folder remembered.";
                },
                (_, _, _) => Task.FromResult("unused"),
                (_, _) => Task.FromResult("unused")));
        var viewModel = CreateViewModel(maintenance);
        var row = viewModel.Rows.Single(candidate => candidate.SystemId == "playstation");
        _dialogs.FolderToReturn = Path.Combine(Path.GetTempPath(), "emushelf-busy-folder");

        var operation = row.AddLibraryFolderCommand.ExecuteAsync(null);
        await started.Task;

        Assert.True(viewModel.IsWorking);
        Assert.All(viewModel.Rows, candidate => Assert.True(candidate.IsMaintenanceBlocked));

        release.SetResult();
        await operation;

        Assert.False(viewModel.IsWorking);
        Assert.All(viewModel.Rows, candidate => Assert.False(candidate.IsMaintenanceBlocked));
    }

    [AvaloniaFact]
    public async Task LibraryFolderAvailability_IsNotCheckedInTheConstructor()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var row = new LibraryFolderRowViewModel(
            new LibraryFolder { Id = 1, SystemId = "playstation", Path = "/slow/share" },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ =>
            {
                Interlocked.Increment(ref calls);
                started.Set();
                release.Wait();
                return true;
            });

        Assert.Equal(0, calls);
        Assert.Equal("Checking…", row.AvailabilityText);

        var refresh = row.RefreshAvailabilityAsync();
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        Assert.False(refresh.IsCompleted);
        release.Set();
        await refresh;

        Assert.Equal(1, calls);
        Assert.True(row.Exists);
        Assert.Equal("Available", row.AvailabilityText);
    }

    [AvaloniaFact]
    public async Task GeneralSettings_LoadAndSaveEmptyPlatformVisibility()
    {
        bool? saved = null;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult("unused"),
            _ => Task.FromResult("unused"),
            GetShowEmptyPlatforms: () => true,
            SetShowEmptyPlatforms: value =>
            {
                saved = value;
                return Task.CompletedTask;
            });
        var viewModel = CreateViewModel(maintenance);

        Assert.True(viewModel.ShowEmptyPlatforms);
        viewModel.ShowEmptyPlatforms = false;
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(saved);
    }

    [AvaloniaFact]
    public async Task PlayStation3Row_ExposesTheExplicitRpcs3LibrarySyncOnly()
    {
        var calls = 0;
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult("unused"),
            _ => Task.FromResult("unused"),
            SyncRpcs3Library: () =>
            {
                calls++;
                return Task.FromResult("RPCS3 library sync complete — 1 added");
            });
        var viewModel = CreateViewModel(maintenance);
        var playStation3 = viewModel.Rows.Single(row => row.SystemId == "playstation3");
        var playStation2 = viewModel.Rows.Single(row => row.SystemId == "playstation2");

        Assert.True(playStation3.HasSyncLibrary);
        Assert.False(playStation3.HasFolderManagement);
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
        Assert.Equal("/pcsx2", Row(viewModel, "playstation2").OverrideDirectory);
    }

    [AvaloniaFact]
    public void CloudSaves_SeededPpssppSettings_ShowProviderConfiguration()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(new CloudSaveSyncSettings
        {
            PpssppMemoryStickDirectory = "/portable/ppsspp",
        }));

        Assert.Equal("/portable/ppsspp", Row(viewModel, "psp").OverrideDirectory);
    }

    [AvaloniaFact]
    public void CloudSaves_ShowsOneRowPerRegisteredPlatform()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext());

        Assert.Equal(
            SaveProviderRegistry.SystemIds,
            viewModel.CloudPlatforms.Select(row => row.SystemId).ToArray());
    }

    [AvaloniaFact]
    public async Task CloudSaves_Connect_Success_MarksConnectedAndPassesOverridesBySystemId()
    {
        var calls = new List<(string Remote, string Folder, IReadOnlyDictionary<string, string?> Overrides)>();
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            connect: (remote, folder, overrides, _) =>
            {
                calls.Add((remote, folder, overrides));
                return Task.FromResult(CloudSaveSyncConnectResult.Connected);
            }));
        viewModel.CloudRemoteName = "my-drive";
        viewModel.CloudFolder = "Saves";
        Row(viewModel, "playstation2").OverrideDirectory = "/pcsx2";
        Row(viewModel, "psp").OverrideDirectory = "/ppsspp";

        await viewModel.ConnectCloudCommand.ExecuteAsync(null);

        var call = Assert.Single(calls);
        Assert.Equal("my-drive", call.Remote);
        Assert.Equal("Saves", call.Folder);
        // Keyed, so a new platform cannot shift one emulator's path onto another.
        Assert.Equal("/pcsx2", call.Overrides["playstation2"]);
        Assert.Equal("/ppsspp", call.Overrides["psp"]);
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
    public async Task CloudSaves_SyncNow_WhenEverySaveIsSkipped_SaysSoInsteadOfReportingNothingFound()
    {
        // A pass where saves existed but were all deliberately left behind (card-type/version
        // mismatch) must not read as an empty "nothing to sync" success — the rows say otherwise.
        var report = new SaveSyncReport(
        [
            new SaveUnitSyncResult("pcsx2/Mcd001.ps2", SaveSyncAction.Skipped, "written by a different build"),
            new SaveUnitSyncResult("pcsx2/Mcd002.ps2", SaveSyncAction.Skipped, "written by a different build"),
        ]);
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            syncNow: (_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(report))));

        await viewModel.SyncCloudNowCommand.ExecuteAsync(null);

        Assert.Contains("2 skipped", viewModel.CloudStatusText);
        Assert.DoesNotContain("No enabled saves were found to sync", viewModel.CloudStatusText);
    }

    [AvaloniaFact]
    public async Task CloudSaves_SyncNow_RefreshesTheActivityLogLink()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"emushelf-save-sync-{Guid.NewGuid():N}.log");
        try
        {
            var changed = new List<string?>();
            var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
                syncLogPath: logPath,
                syncNow: async (_, _) =>
                {
                    await File.WriteAllTextAsync(logPath, "Sync completed.");
                    return CloudSaveSyncOutcome.Completed(new SaveSyncReport([]));
                }));
            viewModel.PropertyChanged += (_, eventArgs) => changed.Add(eventArgs.PropertyName);

            Assert.False(viewModel.HasSyncLog);

            await viewModel.SyncCloudNowCommand.ExecuteAsync(null);

            Assert.True(viewModel.HasSyncLog);
            Assert.Equal(new Uri(logPath), viewModel.SyncLogUri);
            Assert.Contains(nameof(viewModel.HasSyncLog), changed);
        }
        finally
        {
            if (File.Exists(logPath))
                File.Delete(logPath);
        }
    }

    [AvaloniaFact]
    public async Task CloudSaves_SyncNow_RefreshesPlatformResultWithoutOverwritingTypedPath()
    {
        IReadOnlyList<CloudSaveSyncPlatformContext> livePlatforms = SaveProviderRegistry.All
            .Select(descriptor => new CloudSaveSyncPlatformContext(
                descriptor.SystemId,
                descriptor.DisplayName,
                descriptor.SaveShapeDescription,
                descriptor.OverridePlaceholder,
                Override: null,
                LastSuccessUtc: null,
                LastError: descriptor.SystemId == "psp" ? "old failure" : null))
            .ToArray();
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            getPlatforms: () => livePlatforms,
            syncNow: (_, _) =>
            {
                livePlatforms = livePlatforms.Select(platform => platform.SystemId == "psp"
                    ? platform with
                    {
                        LastSuccessUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero),
                        LastError = null,
                    }
                    : platform).ToArray();
                return Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])));
            }));
        var psp = Row(viewModel, "psp");
        psp.OverrideDirectory = "/path/the-user-is-still-editing";

        await viewModel.SyncCloudNowCommand.ExecuteAsync(null);

        Assert.DoesNotContain("old failure", psp.LastResultText);
        Assert.Contains("Last synced", psp.LastResultText);
        Assert.Equal("/path/the-user-is-still-editing", psp.OverrideDirectory);
    }

    [AvaloniaFact]
    public void CloudSaves_WithNoOverride_LeavesTheBoxEmptyForTheConfiguredEmulator()
    {
        // An empty box means "use the configured emulator". Pre-filling it would turn a derived
        // path into an explicit override the moment the user pressed Save.
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext());

        Assert.Equal(string.Empty, Row(viewModel, "playstation2").OverrideDirectory);
        Assert.Null(Row(viewModel, "playstation2").NormalizedOverride);
    }

    [AvaloniaFact]
    public void CloudSaves_SavedOverrideIsShownInItsPlatformRow()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            current: new CloudSaveSyncSettings { Pcsx2ConfigDirectory = "/saved/pcsx2" }));

        Assert.Equal("/saved/pcsx2", Row(viewModel, "playstation2").OverrideDirectory);
    }

    [AvaloniaFact]
    public void CloudSaves_ShowsEachPlatformsOwnLastResult()
    {
        var configuration = new CloudSaveSyncSettings()
            .WithSyncFailure("psp", "the remote was unreachable");
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(configuration));

        Assert.Contains("the remote was unreachable", Row(viewModel, "psp").LastResultText);
        Assert.False(Row(viewModel, "playstation2").HasLastResult);
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
    public async Task CloudSaves_PickDirectory_PersistsAndDetectsThatPlatformsPath()
    {
        var persisted = new List<(string SystemId, string? Directory)>();
        _dialogs.FolderToReturn = "/picked/pcsx2";
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            updateOverride: (systemId, directory) => persisted.Add((systemId, directory))));

        await Row(viewModel, "playstation2").PickDirectoryCommand.ExecuteAsync(null);

        Assert.Equal("/picked/pcsx2", Row(viewModel, "playstation2").OverrideDirectory);
        // The change is saved even without reconnecting.
        Assert.Equal(("playstation2", "/picked/pcsx2"), Assert.Single(persisted));
        Assert.Equal("/pcsx2/memcards", Row(viewModel, "playstation2").DetectedDirectory);
    }

    [AvaloniaFact]
    public async Task CloudSaves_DetectionShowsPathAndNonBlockingCompatibilityWarning()
    {
        const string warning = "Filename-based cards require matching game filenames.";
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            getDetection: (systemId, _) => Task.FromResult<SaveProviderDetection?>(
                systemId == "playstation"
                    ? new SaveProviderDetection("/duckstation/memcards", warning)
                    : null)));
        var row = Row(viewModel, "playstation");

        await row.RefreshDetectedDirectoryAsync();

        Assert.Equal("/duckstation/memcards", row.DetectedDirectory);
        Assert.Equal(warning, row.CompatibilityWarning);
        Assert.True(row.HasCompatibilityWarning);
        Assert.False(row.HasDetectionError);
        Assert.True(row.CanReplace);
    }

    [AvaloniaFact]
    public async Task CloudSaves_DetectionCanDisplayEffectiveSaveLocationsInsteadOfConfigurationRoot()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            getDetection: (systemId, _) => Task.FromResult<SaveProviderDetection?>(
                systemId == "gamecube"
                    ? new SaveProviderDetection(
                        @"F:\Dolphin\User",
                        DisplayLocation: @"F:\saves\dolphin\GC\USA • F:\saves\dolphin\SRAM.USA.raw")
                    : null)));
        var row = Row(viewModel, "gamecube");

        await row.RefreshDetectedDirectoryAsync();

        Assert.Equal(
            @"F:\saves\dolphin\GC\USA • F:\saves\dolphin\SRAM.USA.raw",
            row.DetectedDirectory);
    }

    [AvaloniaFact]
    public async Task CloudSaves_DetectionErrorIsVisibleAndDisablesReplaceActions()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            getDetection: (_, _) => throw new InvalidOperationException("Unsupported card layout.")));
        var row = Row(viewModel, "playstation");

        await row.RefreshDetectedDirectoryAsync();

        Assert.Null(row.DetectedDirectory);
        Assert.Contains("Unsupported card layout", row.DetectionErrorText);
        Assert.True(row.HasDetectionError);
        Assert.False(row.CanReplace);
    }

    [AvaloniaFact]
    public async Task CloudSaves_Save_PersistsEveryPlatformsTypedPath()
    {
        var persisted = new Dictionary<string, string?>(StringComparer.Ordinal);
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(
            updateOverride: (systemId, directory) => persisted[systemId] = directory));
        Row(viewModel, "playstation2").OverrideDirectory = " /pcsx2 ";
        Row(viewModel, "psp").OverrideDirectory = " /ppsspp ";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("/pcsx2", persisted["playstation2"]);
        Assert.Equal("/ppsspp", persisted["psp"]);
    }

    [AvaloniaFact]
    public async Task CloudSaves_ReplaceLocal_CallsContextForThatRowsPlatformOnly()
    {
        string? systemId = null;
        SaveSyncDirection? captured = null;
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext(force: (system, direction, _, _) =>
        {
            systemId = system;
            captured = direction;
            return Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])));
        }));

        await Row(viewModel, "psp").ReplaceLocalCommand.ExecuteAsync(null);

        Assert.Equal("psp", systemId);
        Assert.Equal(SaveSyncDirection.Download, captured);
    }

    [AvaloniaFact]
    public async Task CloudSaves_ReplaceCloud_UsesTheRowsOwnPlatformName()
    {
        var viewModel = CreateViewModel(cloudSaves: CreateCloudContext());

        await Row(viewModel, "psp").ReplaceCloudCommand.ExecuteAsync(null);

        // The progress message reads from the registry rather than a hardcoded name map, so a
        // third platform can never be mislabelled as one of the first two.
        Assert.DoesNotContain("PlayStation 2", viewModel.CloudStatusText);
    }

    [AvaloniaFact]
    public async Task ScreenScraper_Connect_ShowsConnected_AndDisconnectClears()
    {
        var connectCalled = false;
        var disconnectCalled = false;
        var context = new ScreenScraperSettingsContext(
            IsConnected: false,
            Account: null,
            ConnectAsync: (_, _, _) =>
            {
                connectCalled = true;
                return Task.FromResult(new ScreenScraperConnectionSummary(ScreenScraperConnectionResult.Connected));
            },
            DisconnectAsync: _ =>
            {
                disconnectCalled = true;
                return Task.CompletedTask;
            });
        var viewModel = CreateViewModel(screenScraper: context);

        Assert.Contains(SettingsSection.ScreenScraper, viewModel.Sections);

        viewModel.ScreenScraperUsername = "bostan";
        viewModel.ScreenScraperPassword = "secret";
        await viewModel.ConnectScreenScraperCommand.ExecuteAsync(null);

        Assert.True(connectCalled);
        Assert.True(viewModel.IsScreenScraperConnected);
        Assert.Equal("bostan", viewModel.ScreenScraperConnectedName);
        Assert.Empty(viewModel.ScreenScraperPassword);

        await viewModel.DisconnectScreenScraperCommand.ExecuteAsync(null);

        Assert.True(disconnectCalled);
        Assert.True(viewModel.IsScreenScraperDisconnected);
    }

    [AvaloniaFact]
    public async Task ScreenScraper_AuthFailure_StaysDisconnected_WithMessage()
    {
        var context = new ScreenScraperSettingsContext(
            false,
            null,
            (_, _, _) => Task.FromResult(
                new ScreenScraperConnectionSummary(ScreenScraperConnectionResult.AuthenticationFailed)),
            _ => Task.CompletedTask);
        var viewModel = CreateViewModel(screenScraper: context);

        viewModel.ScreenScraperUsername = "bostan";
        viewModel.ScreenScraperPassword = "wrong";
        await viewModel.ConnectScreenScraperCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsScreenScraperDisconnected);
        Assert.False(string.IsNullOrEmpty(viewModel.ScreenScraperStatusText));
    }

    [AvaloniaFact]
    public async Task Render_InstallEmulatorsSection_Preview()
    {
        // A representative row for each install state, so the preview shows every affordance at once.
        var service = new PreviewInstallService();
        service.Statuses["duckstation"] = new EmulatorInstallStatus.NotInstalled("0.1-7800");
        service.Statuses["pcsx2"] = new EmulatorInstallStatus.Managed("2.0.0");
        service.Statuses["rpcs3"] = new EmulatorInstallStatus.UpdateAvailable("0.0.30-16800", "0.0.34-17100");
        service.Statuses["dolphin"] = new EmulatorInstallStatus.Unsupported(
            "EmuShelf can't install this emulator yet — use its download page.", "https://dolphin-emu.org/download/");
        service.Statuses["ppsspp"] = new EmulatorInstallStatus.UserProvided("/Applications/PPSSPP.app", null);
        service.Statuses["azahar"] = new EmulatorInstallStatus.NotInstalled(null);
        service.Statuses["retroarch"] = new EmulatorInstallStatus.Unsupported(
            "EmuShelf can't install this emulator yet — use its download page.", "https://www.retroarch.com/?page=platforms");

        var rows = new List<EmulatorInstallRowViewModel>();
        foreach (var definition in KnownEmulators.All)
        {
            var row = new EmulatorInstallRowViewModel(definition.Id, definition.Name, service, NullAppLogger.Instance);
            await row.RefreshAsync();
            rows.Add(row);
        }
        var context = new EmulatorManagerSettingsContext(rows, _ => Task.CompletedTask);

        var snapshotDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var viewModel = CreateViewModel(emulatorManager: context);
            viewModel.SelectedSection = SettingsSection.EmulatorManager;
            var window = new EmulatorSettingsWindow
            {
                DataContext = viewModel,
                RequestedThemeVariant = variant,
            };
            window.Show();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            Assert.Equal(SettingsSection.EmulatorManager, viewModel.SelectedSection);
            Assert.Equal(KnownEmulators.All.Count, viewModel.EmulatorInstallRows.Count);

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (snapshotDirectory is not null)
            {
                Directory.CreateDirectory(snapshotDirectory);
                await using var output = File.Create(Path.Combine(
                    snapshotDirectory, $"install-emulators-{variant}.png"));
                frame!.Save(output, PngBitmapEncoderOptions.Default);
            }
        }
    }

    private sealed class PreviewInstallService : IEmulatorInstallService
    {
        public Dictionary<string, EmulatorInstallStatus> Statuses { get; } = new(StringComparer.Ordinal);

        public Task<EmulatorInstallStatus> GetStatusAsync(string emulatorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Statuses.TryGetValue(emulatorId, out var status)
                ? status
                : new EmulatorInstallStatus.CheckFailed("no status"));

        public Task<EmulatorInstallResult> InstallAsync(string emulatorId, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmulatorInstallResult>(new EmulatorInstallResult.Failed("preview"));

        public Task<EmulatorInstallResult> UpdateAsync(string emulatorId, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmulatorInstallResult>(new EmulatorInstallResult.Failed("preview"));
    }

    private EmulatorSettingsViewModel CreateViewModel(
        LibraryMaintenanceActions? maintenance = null,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        IReadOnlyDictionary<string, EmulatorConfiguration?>? configured = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null,
        EmulatorManagerSettingsContext? emulatorManager = null,
        FakeDialogService? dialogs = null) => new(
        KnownSystems.All,
        KnownEmulators.All,
        configured ?? KnownSystems.All.ToDictionary(
            system => system.Id,
            _ => (EmulatorConfiguration?)null,
            StringComparer.Ordinal),
        _configurations,
        dialogs ?? _dialogs,
        maintenance,
        retroAchievements: retroAchievements,
        cloudSaves: cloudSaves,
        texturePacks: texturePacks,
        screenScraper: screenScraper,
        emulatorManager: emulatorManager);

    private static CloudSaveSyncSettingsContext CreateCloudContext(
        CloudSaveSyncSettings? current = null,
        Func<string, string, IReadOnlyDictionary<string, string?>, CancellationToken, Task<CloudSaveSyncConnectResult>>? connect = null,
        Func<IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>>? syncNow = null,
        Func<string, SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>>? force = null,
        Action<string, string?>? updateOverride = null,
        bool rcloneAvailable = true,
        Func<CancellationToken, Task<bool>>? downloadRclone = null,
        string? syncLogPath = null,
        Func<IReadOnlyList<CloudSaveSyncPlatformContext>>? getPlatforms = null,
        Func<string, CancellationToken, Task<SaveProviderDetection?>>? getDetection = null)
    {
        var configuration = current ?? new CloudSaveSyncSettings();
        var platforms = SaveProviderRegistry.All.Select(descriptor =>
        {
            var location = configuration.NormalizeSaveLocations().GetLocation(descriptor.SystemId);
            return new CloudSaveSyncPlatformContext(
                descriptor.SystemId,
                descriptor.DisplayName,
                descriptor.SaveShapeDescription,
                descriptor.OverridePlaceholder,
                location.DirectoryOverride,
                location.LastSuccessUtc,
                location.LastError);
        }).ToArray();

        return new CloudSaveSyncSettingsContext(
            configuration,
            rcloneAvailable,
            "/app/rclone",
            syncLogPath ?? Path.Combine(Path.GetTempPath(), "emushelf-save-sync-test.log"),
            getPlatforms ?? (() => platforms),
            (systemId, _) => Task.FromResult<string?>(
                systemId == "psp" ? "/ppsspp/PSP/SAVEDATA" : "/pcsx2/memcards"),
            connect ?? ((_, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.Connected)),
            _ => Task.CompletedTask,
            syncNow ?? ((_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])))),
            force ?? ((_, _, _, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([])))),
            updateOverride ?? ((_, _) => { }),
            downloadRclone ?? (_ => Task.FromResult(true)),
            getDetection);
    }

    private static CloudSavePlatformRowViewModel Row(EmulatorSettingsViewModel viewModel, string systemId) =>
        viewModel.CloudPlatforms.Single(row => row.SystemId == systemId);

    private sealed class RecordingConfigurationStore : IEmulatorConfigurationStore
    {
        // Keyed by system id (active profile, last write wins) so single-profile tests read Saved[id];
        // AllSaved keeps every persisted (system, emulator) profile and ActiveEmulators the selections.
        public Dictionary<string, EmulatorConfiguration> Saved { get; } =
            new(StringComparer.Ordinal);
        public List<EmulatorConfiguration> AllSaved { get; } = [];
        public Dictionary<string, string> ActiveEmulators { get; } = new(StringComparer.Ordinal);
        public int BatchSaveCalls { get; private set; }

        public EmulatorConfiguration? Get(string systemId) =>
            Saved.GetValueOrDefault(systemId);

        public void SetActiveEmulator(string systemId, string emulatorId) =>
            ActiveEmulators[systemId] = emulatorId;

        public void Save(EmulatorConfiguration configuration)
        {
            Saved[configuration.SystemId] = configuration;
            AllSaved.Add(configuration);
        }

        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
        {
            BatchSaveCalls++;
            foreach (var configuration in configurations)
                Save(configuration);
        }
    }
}
