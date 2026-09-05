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
    private static EmulatorSettingsViewModel DesktopSettings(bool closeOnReturn = true)
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
            new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            maintenance,
            fixedEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
    }

    private static GamepadSettingsViewModel Wizard(
        EmulatorSettingsViewModel settings,
        bool hasSecondScreen = true,
        bool secondScreenReady = false,
        Action? requestSecondScreen = null) =>
        new(
            settings,
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem,
            gameCountBySystem: _ => 0,
            closeOnReturnWarning: () => "Shizuku permission not granted · press Y to grant it",
            grantCloseOnReturnPrivilege: () => Task.CompletedTask,
            setup: new SetupWizardOptions(
                HasSecondScreen: hasSecondScreen,
                IsSecondScreenReturnReady: () => secondScreenReady,
                RequestSecondScreenReturn: requestSecondScreen ?? (() => { }),
                DataFolderStatus: "User/EmuShelf"));

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
    public void StartAndBackWalkTheSteps_AndBackOnTheFirstStepLeavesUnfinished()
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

        // LB/RB never jump sections in the wizard.
        Assert.True(vm.Dispatch(GamepadAction.NextPlatform));
        Assert.Equal(SetupStep.GamesAndEmulators, vm.CurrentSetupStep);

        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        Assert.Equal(SetupStep.SecondScreen, vm.CurrentSetupStep);
        Assert.Null(closed);
        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        Assert.False(closed);
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
    }

    [Fact]
    public void OrdinarySettings_OfferRunSetupAgain_OnlyWhenTheHostProvidesIt()
    {
        var settings = DesktopSettings();
        var plain = new GamepadSettingsViewModel(settings);
        Assert.DoesNotContain(plain.Rows, row => row.Key == "general.run-setup");

        var opened = 0;
        var withSetup = new GamepadSettingsViewModel(settings, runSetup: () => { opened++; return Task.CompletedTask; });
        Assert.False(withSetup.IsSetupMode);
        Assert.Contains(withSetup.Rows, row => row.Key == "general.run-setup");
    }
}
