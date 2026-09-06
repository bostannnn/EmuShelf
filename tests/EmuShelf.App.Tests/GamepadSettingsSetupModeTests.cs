using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Launching;

using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>The couch Settings projection walked as the in-app half of the Android setup wizard.</summary>
public sealed class GamepadSettingsSetupModeTests
{
    private static EmulatorSettingsViewModel DesktopSettings(bool closeOnReturn = true, IEmulatorConfigurationStore? store = null)
    {
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            GetCloseEmulatorOnReturn: closeOnReturn ? () => true : null,
            SetCloseEmulatorOnReturn: closeOnReturn ? _ => Task.CompletedTask : null);
        return new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(system => system.Id, _ => (EmulatorConfiguration?)null, StringComparer.Ordinal),
            store ?? new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            maintenance,
            fixedEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
    }

    private static GamepadSettingsViewModel Wizard(
        EmulatorSettingsViewModel settings,
        bool hasSecondScreen = true,
        bool secondScreenReady = false,
        Action? requestSecondScreen = null,
        Func<bool>? storageGranted = null,
        Action? requestStorage = null) =>
        new(
            settings,
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem,
            gameCountBySystem: _ => 0,
            closeOnReturnWarning: () => "Shizuku permission not allowed yet · press Y to allow it",
            grantCloseOnReturnPrivilege: () => Task.CompletedTask,
            setup: new SetupWizardOptions(
                HasSecondScreen: hasSecondScreen,
                IsSecondScreenReturnReady: () => secondScreenReady,
                RequestSecondScreenReturn: requestSecondScreen ?? (() => { }),
                DataFolderStatus: "User/EmuShelf",
                IsStoragePermissionGranted: storageGranted,
                RequestStoragePermission: requestStorage));

    [Fact]
    public void ListsEveryStepForThisDevice_StartingOnTheFirstLiveOne()
    {
        var vm = Wizard(DesktopSettings());

        Assert.True(vm.IsSetupMode);
        Assert.Equal(
            [SetupStep.StorageAccess, SetupStep.DataFolder, SetupStep.SecondScreen, SetupStep.ClosingGames, SetupStep.GamesAndEmulators],
            vm.SetupRail.Steps.Select(step => step.Step).ToArray());
        Assert.Equal(SetupStep.SecondScreen, vm.CurrentSetupStep);
        Assert.Equal("Playing on the second screen", vm.SectionTitle);
        Assert.Equal("Allowed", vm.SetupRail.Steps[0].Status);
        Assert.Equal("User/EmuShelf", vm.SetupRail.Steps[1].Status);
        Assert.Equal("Off", vm.SetupRail.Steps[2].Status);
        Assert.True(vm.SetupRail.Steps[2].IsWarning);
        Assert.Equal("Continue", vm.SetupRail.StartLabel);
        Assert.Equal("Next: Closing games", vm.SetupRail.StartDetail);
        // No "Save and close" row: START is the way forward.
        Assert.DoesNotContain(vm.Rows, row => row.IsSaveRow);
    }

    [Fact]
    public void SkipsStepsThatDoNotApply()
    {
        var vm = Wizard(DesktopSettings(closeOnReturn: false), hasSecondScreen: false);

        Assert.Equal([SetupStep.StorageAccess, SetupStep.DataFolder, SetupStep.GamesAndEmulators],
            vm.SetupRail.Steps.Select(step => step.Step).ToArray());
        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);
        Assert.Equal("Finish", vm.SetupRail.StartLabel);
    }

    [Fact]
    public void SecondScreenRow_OpensTheAccessibilityPage_AndReadsOnAfterReturn()
    {
        var ready = false;
        var requests = 0;
        var vm = Wizard(DesktopSettings(), secondScreenReady: false, requestSecondScreen: () => requests++);
        // The probe is re-read through the options delegate on RefreshDeviceState.
        var settings = DesktopSettings();
        vm = new GamepadSettingsViewModel(
            settings,
            setup: new SetupWizardOptions(true, () => ready, () => requests++, "User/EmuShelf"));

        Assert.Equal("setup.second-screen.return", vm.FocusedRow!.Key);
        Assert.True(vm.FocusedRow.IsAction);
        Assert.True(vm.Dispatch(GamepadAction.Confirm));
        Assert.Equal(1, requests);

        ready = true;
        vm.RefreshDeviceState();

        Assert.True(vm.Rows.Single(row => row.Key == "setup.second-screen.return").IsInformation);
        Assert.Equal("On", vm.SetupRail.Steps[2].Status);
        Assert.True(vm.SetupRail.Steps[2].IsDone);
    }

    [Fact]
    public async Task StartAndBackWalkTheSteps_AndBackOnTheFirstStepLeavesUnfinished()
    {
        var vm = Wizard(DesktopSettings());
        bool? closed = null;
        vm.CloseRequested += saved => closed = saved;

        Assert.True(vm.Dispatch(GamepadAction.Menu));
        Assert.Equal(SetupStep.ClosingGames, vm.CurrentSetupStep);
        Assert.Equal("Closing games", vm.SectionTitle);
        Assert.Equal("emulators.close-on-return", vm.FocusedRow!.Key);
        Assert.Equal("Needs Shizuku", vm.SetupRail.Steps[3].Status);

        Assert.True(vm.Dispatch(GamepadAction.Menu));
        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);
        Assert.Equal(SettingsSection.Emulators, vm.SelectedSection);
        Assert.DoesNotContain(vm.Rows, row => row.Key == "emulators.close-on-return");
        Assert.Contains(vm.Rows, row => row.IsSummary);
        Assert.Equal("Finish", vm.SetupRail.StartLabel);

        // LB/RB never jump steps in the wizard — in either column. The rail is the one the legend does
        // not cover, so a shoulder press there used to walk the steps anyway.
        Assert.True(vm.Dispatch(GamepadAction.NextPlatform));
        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);
        Assert.True(vm.Dispatch(GamepadAction.NavigateLeft));
        Assert.True(vm.IsRailFocused);
        Assert.True(vm.Dispatch(GamepadAction.NextPlatform));
        Assert.True(vm.Dispatch(GamepadAction.PreviousPlatform));
        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);
        Assert.True(vm.Dispatch(GamepadAction.NavigateRight));

        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        Assert.Equal(SetupStep.SecondScreen, vm.CurrentSetupStep);
        // The two pre-boot steps stay reachable so their answers can be seen. Neither offers an action:
        // changing the data folder from here would restart the process and lose the wizard's answers.
        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        Assert.Equal(SetupStep.DataFolder, vm.CurrentSetupStep);
        Assert.Contains(vm.Rows, row => row.Key == "setup.folder.current");
        Assert.DoesNotContain(vm.Rows, row => row.Key == "general.change-data-folder");
        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        Assert.Equal(SetupStep.StorageAccess, vm.CurrentSetupStep);
        Assert.Null(closed);

        // B on the first step leaves the wizard. It saves on the way out, so the answers already given
        // survive, but SetupCompleted stays false so the host offers the wizard again next launch.
        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        for (var attempt = 0; attempt < 100 && closed is null; attempt++)
            await Task.Delay(20);
        Assert.NotNull(closed);
        Assert.False(vm.SetupCompleted);
    }

    [Fact]
    public void LeftEntersTheRail_WhereUpDownWalkTheSteps_AndRightReturns()
    {
        var vm = Wizard(DesktopSettings());

        Assert.True(vm.Dispatch(GamepadAction.NavigateLeft));
        Assert.True(vm.IsRailFocused);
        Assert.True(vm.SetupRail.IsRailFocused);

        Assert.True(vm.Dispatch(GamepadAction.NavigateDown));
        Assert.Equal(SetupStep.ClosingGames, vm.CurrentSetupStep);
        Assert.True(vm.IsRailFocused);
        Assert.True(vm.Dispatch(GamepadAction.NavigateDown));
        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);
        Assert.True(vm.Dispatch(GamepadAction.NavigateUp));
        Assert.Equal(SetupStep.ClosingGames, vm.CurrentSetupStep);
        // Up walks all the way to the top of the rail.
        Assert.True(vm.Dispatch(GamepadAction.NavigateUp));
        Assert.True(vm.Dispatch(GamepadAction.NavigateUp));
        Assert.Equal(SetupStep.DataFolder, vm.CurrentSetupStep);
        Assert.True(vm.Dispatch(GamepadAction.NavigateUp));
        Assert.Equal(SetupStep.StorageAccess, vm.CurrentSetupStep);
        Assert.True(vm.Dispatch(GamepadAction.NavigateDown));
        Assert.True(vm.Dispatch(GamepadAction.NavigateDown));
        Assert.True(vm.Dispatch(GamepadAction.NavigateDown));

        Assert.True(vm.Dispatch(GamepadAction.NavigateRight));
        Assert.False(vm.IsRailFocused);
        Assert.Equal("emulators.close-on-return", vm.FocusedRow!.Key);
    }

    [Fact]
    public void EmptyLibrary_OpensTheFirstSystem_SoAddFolderIsOnScreen()
    {
        var vm = Wizard(DesktopSettings(closeOnReturn: false), hasSecondScreen: false);

        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);
        var first = vm.Rows.First(row => row.IsSummary);
        Assert.True(first.IsExpanded);
        // Its per-platform rows are on screen beneath it (the test settings expose rescan, a real
        // Android library exposes the folder rows + "Add game folder").
        Assert.Contains(vm.Rows, row => row.IsGrouped && row.SystemId == first.SystemId);
    }

    [Fact]
    public async Task FinishOnTheLastStep_SavesAndCloses()
    {
        var vm = Wizard(DesktopSettings(closeOnReturn: false), hasSecondScreen: false);
        bool? closed = null;
        vm.CloseRequested += saved => closed = saved;

        Assert.True(vm.Dispatch(GamepadAction.Menu));
        // Finish runs the ordinary (async) save; it reports back through CloseRequested(saved: true).
        for (var attempt = 0; attempt < 100 && closed is null; attempt++)
            await Task.Delay(20);

        Assert.True(closed);
        // Only this path records the wizard as walked; leaving early also saves but must not.
        Assert.True(vm.SetupCompleted);
    }

    [Fact]
    public async Task StorageAccessStep_ReportsTheLiveGrant_AndCanReopenAndroidsPage()
    {
        var granted = false;
        var requests = 0;
        var vm = Wizard(
            DesktopSettings(closeOnReturn: false),
            hasSecondScreen: false,
            storageGranted: () => granted,
            requestStorage: () => requests++);

        Assert.True(vm.Dispatch(GamepadAction.NavigateLeft));
        Assert.True(vm.Dispatch(GamepadAction.NavigateUp));
        Assert.True(vm.Dispatch(GamepadAction.NavigateUp));
        Assert.Equal(SetupStep.StorageAccess, vm.CurrentSetupStep);

        // Revoked while the app was running: the step says so and offers the way back, instead of
        // asserting the answer the pre-boot page was given before the restart.
        var row = vm.Rows.Single(rowspec => rowspec.Key == "setup.storage.grant");
        Assert.True(row.IsAction);
        Assert.True(row.IsWarning);
        Assert.Equal("Not allowed", vm.SetupRail.Steps[0].Status);
        Assert.True(vm.SetupRail.Steps[0].IsWarning);
        Assert.False(vm.SetupRail.Steps[0].IsDone);

        await vm.FocusAndActivateAsync(row);
        Assert.Equal(1, requests);

        granted = true;
        vm.RefreshDeviceState();

        Assert.True(vm.Rows.Single(rowspec => rowspec.Key == "setup.storage.grant").IsInformation);
        Assert.Equal("Allowed", vm.SetupRail.Steps[0].Status);
        Assert.True(vm.SetupRail.Steps[0].IsDone);
    }

    [Fact]
    public async Task OrdinarySettings_OfferRunSetupAgain_OnlyWhenTheHostProvidesIt()
    {
        var settings = DesktopSettings();
        var plain = new GamepadSettingsViewModel(settings);
        Assert.DoesNotContain(plain.Rows, row => row.Key == "general.run-setup");

        var opened = 0;
        var withSetup = new GamepadSettingsViewModel(settings, runSetup: () => { opened++; return Task.CompletedTask; });
        Assert.False(withSetup.IsSetupMode);
        var run = Assert.Single(withSetup.Rows, row => row.Key == "general.run-setup");

        // Activating it has to reach the host's delegate; the row existing proves nothing on its own.
        await withSetup.FocusAndActivateAsync(run);
        Assert.Equal(1, opened);
    }
    [Fact]
    public async Task FailedFinish_StaysOpenAndDoesNotMarkSetupComplete_ThenCanRetry()
    {
        var store = new FailingStore();
        var settings = DesktopSettings(closeOnReturn: false, store: store);
        var vm = Wizard(settings, hasSecondScreen: false);
        var closes = new List<bool>();
        vm.CloseRequested += closes.Add;

        vm.Dispatch(GamepadAction.Menu);
        await settings.SaveCommand.ExecutionTask!;

        Assert.Empty(closes);
        Assert.False(vm.SetupCompleted);
        Assert.Contains("Could not save settings", vm.StatusText);

        store.Fail = false;
        vm.Dispatch(GamepadAction.Menu);
        await settings.SaveCommand.ExecutionTask!;
        Assert.Equal([true], closes);
        Assert.True(vm.SetupCompleted);
    }

    [Fact]
    public async Task FailedBackSave_StaysOpenAndKeepsAnswers()
    {
        var settings = DesktopSettings(store: new FailingStore());
        var vm = Wizard(settings);
        var closes = new List<bool>();
        vm.CloseRequested += closes.Add;
        settings.CloseEmulatorOnReturn = false;
        vm.Dispatch(GamepadAction.Cancel);
        vm.Dispatch(GamepadAction.Cancel);
        Assert.Equal(SetupStep.StorageAccess, vm.CurrentSetupStep);
        vm.Dispatch(GamepadAction.Cancel);
        await settings.SaveCommand.ExecutionTask!;

        Assert.Empty(closes);
        Assert.False(vm.SetupCompleted);
        Assert.False(settings.CloseEmulatorOnReturn);
        Assert.Contains("Could not save settings", vm.StatusText);
    }

    [Fact]
    public void BusyWizard_CannotAdvanceFinishOrExit_FromContentOrRail()
    {
        var settings = DesktopSettings(closeOnReturn: false);
        var vm = Wizard(settings, hasSecondScreen: false);
        var closed = false;
        vm.CloseRequested += _ => closed = true;
        settings.IsSaving = true;

        vm.Dispatch(GamepadAction.Menu);
        vm.Dispatch(GamepadAction.Cancel);
        vm.Dispatch(GamepadAction.NavigateLeft);
        vm.Dispatch(GamepadAction.NavigateUp);
        vm.Dispatch(GamepadAction.Menu);

        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);
        Assert.False(vm.SetupRail.IsStartEnabled);
        Assert.False(vm.SetupCompleted);
        Assert.False(closed);
        Assert.Null(settings.SaveCommand.ExecutionTask);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ForegroundRefresh_PreservesStepAndUnsavedAnswers(int advances)
    {
        var settings = DesktopSettings();
        var vm = Wizard(settings);
        for (var i = 0; i < advances; i++)
            vm.Dispatch(GamepadAction.Menu);
        var step = vm.CurrentSetupStep;
        settings.CloseEmulatorOnReturn = false;
        var closed = false;
        vm.CloseRequested += _ => closed = true;

        vm.RefreshDeviceState();
        vm.RefreshDeviceState();

        Assert.Equal(step, vm.CurrentSetupStep);
        Assert.False(settings.CloseEmulatorOnReturn);
        Assert.False(closed);
        Assert.False(vm.SetupCompleted);
    }

    [Fact]
    public void SetupSaveFailure_IsVisibleEvenInTheSavesSection()
    {
        var settings = DesktopSettings();
        var vm = Wizard(settings);
        vm.SelectedSection = SettingsSection.Saves;
        settings.CloudStatusText = "Connected";
        settings.StatusText = "Could not save settings: disk full";

        Assert.Equal(settings.StatusText, vm.StatusText);
    }

    private sealed class FailingStore : IEmulatorConfigurationStore
    {
        public bool Fail { get; set; } = true;
        public EmulatorConfiguration? Get(string systemId) => null;
        public void Save(EmulatorConfiguration configuration) { }
        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations)
        {
            if (Fail)
                throw new IOException("Test save failure");
        }
    }

}
