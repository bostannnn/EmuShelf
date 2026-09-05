using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.Tests;

public sealed class SetupWizardViewModelTests
{
    [Fact]
    public void StartsOnStorageAccess_WhenTheGrantIsRequiredAndNotHeld()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.Equal(SetupStep.StorageAccess, vm.CurrentStep);
        Assert.Equal("setup.storage.grant", vm.FocusedRow!.Key);
        Assert.True(vm.FocusedRow.IsWarning);
        Assert.False(vm.Rail.IsStartEnabled);
        Assert.Equal("Allow access first", vm.Rail.StartDetail);
    }

    [Fact]
    public void StartsOnDataFolder_WhenNoGrantIsRequired()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = false, RecommendedBaseDirectory = "/data/EmuShelf" };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.Equal(SetupStep.DataFolder, vm.CurrentStep);
        Assert.DoesNotContain(vm.Rail.Steps, step => step.Step == SetupStep.StorageAccess);
        Assert.Equal("setup.folder.recommended", vm.FocusedRow!.Key);
    }

    [Fact]
    public void RailListsTheWholeWizard_WithInAppStepsDimmed()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = true,
            IsStoragePermissionGranted = false,
            ShowSecondScreenReturnStep = true,
        };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.Equal(
            [SetupStep.StorageAccess, SetupStep.DataFolder, SetupStep.SecondScreen, SetupStep.ClosingGames, SetupStep.GamesAndEmulators, SetupStep.Saves],
            vm.Rail.Steps.Select(step => step.Step).ToArray());
        Assert.All(vm.Rail.Steps.Where(step => step.Step >= SetupStep.SecondScreen), step => Assert.True(step.IsDimmed));
        Assert.True(vm.Rail.Steps[0].IsCurrent);
    }

    [Fact]
    public void GrantRow_SendsTheUserToTheGrantSurface()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));

        Assert.Equal(1, bootstrap.GrantRequests);
    }

    [Fact]
    public void GrantLanding_AdvancesToTheFolderStep_AndOffersTheExistingLibraryFirst()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = true,
            IsStoragePermissionGranted = false,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
            ExistingDataFolder = "/storage/emulated/0/User/EmuShelf",
        };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        bootstrap.IsStoragePermissionGranted = true;
        bootstrap.RaisePermissionMaybeChanged();

        Assert.Equal(SetupStep.DataFolder, vm.CurrentStep);
        Assert.Equal("setup.folder.existing", vm.FocusedRow!.Key);
        Assert.Contains("/storage/emulated/0/User/EmuShelf", vm.FocusedRow.Description);
        Assert.Equal("Allowed", vm.Rail.Steps[0].Status);
        Assert.True(vm.Rail.Steps[0].IsDone);
    }

    [Fact]
    public void StartOnStorage_AdvancesOnlyOnceTheGrantIsHeld()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        vm.DispatchGamepadAction(GamepadAction.Menu);
        Assert.Equal(SetupStep.StorageAccess, vm.CurrentStep);

        bootstrap.IsStoragePermissionGranted = true;
        vm.RefreshPermissionState();
        Assert.Equal(SetupStep.DataFolder, vm.CurrentStep);

        // B walks back to the storage step, START forward again.
        vm.DispatchGamepadAction(GamepadAction.Cancel);
        Assert.Equal(SetupStep.StorageAccess, vm.CurrentStep);
        Assert.True(vm.Rail.IsStartEnabled);
        vm.DispatchGamepadAction(GamepadAction.Menu);
        Assert.Equal(SetupStep.DataFolder, vm.CurrentStep);
    }

    [Fact]
    public async Task ExistingLibraryRow_CompletesWithThatFolder()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            ExistingDataFolder = "/storage/emulated/0/User/EmuShelf",
        };
        string? chosen = null;
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, dir => chosen = dir);

        await vm.FocusAndActivateAsync(vm.Rows.Single(row => row.Key == "setup.folder.existing"));

        Assert.Equal("/storage/emulated/0/User/EmuShelf", chosen);
        Assert.Equal("/storage/emulated/0/User/EmuShelf", bootstrap.AdoptedFolder);
    }

    [Fact]
    public async Task RecommendedFolder_CompletesWithItsBaseDirectory()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
            RecommendedResult = DataLocationPickResult.Success("/storage/emulated/0/EmuShelf"),
        };
        string? chosen = null;
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, dir => chosen = dir);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));
        await Task.Yield();

        Assert.Equal("/storage/emulated/0/EmuShelf", chosen);
    }

    [Fact]
    public async Task PickAnotherFolder_CompletesWithTheChosenBaseDirectory()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            PickResult = DataLocationPickResult.Success("/storage/AE6A-1092/EmuShelf"),
        };
        string? chosen = null;
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, dir => chosen = dir);

        await vm.FocusAndActivateAsync(vm.Rows.Single(row => row.Key == "setup.folder.pick"));

        Assert.Equal("/storage/AE6A-1092/EmuShelf", chosen);
    }

    [Fact]
    public async Task RejectedSelection_ShowsTheReason_AndDoesNotComplete()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            PickResult = DataLocationPickResult.Failed("That's an app's private folder."),
        };
        var completed = false;
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => completed = true);

        await vm.FocusAndActivateAsync(vm.Rows.Single(row => row.Key == "setup.folder.pick"));

        Assert.False(completed);
        Assert.Equal("That's an app's private folder.", vm.StatusMessage);
        Assert.True(vm.HasStatus);
        Assert.False(vm.IsBusy);
        Assert.All(vm.Rows.Where(row => row.IsAction), row => Assert.True(row.IsEnabled));
    }

    [Fact]
    public async Task CancelledPick_LeavesTheScreenUnchanged()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = false };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.LocationUnavailable, _ => { });
        var before = vm.StatusMessage;

        await vm.FocusAndActivateAsync(vm.Rows.Single(row => row.Key == "setup.folder.pick"));

        Assert.Equal(before, vm.StatusMessage);
        Assert.Contains("can't be reached", before);
    }

    [Fact]
    public void ForegroundEvent_CompletesTheWizard_WhenThePointerNowResolves()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        string? chosen = null;
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.StoragePermissionMissing, dir => chosen = dir);

        bootstrap.IsStoragePermissionGranted = true;
        bootstrap.Resolution = DataLocationResolution.Resolved("/storage/emulated/0/User/EmuShelf");
        bootstrap.RaisePermissionMaybeChanged();

        Assert.Equal("/storage/emulated/0/User/EmuShelf", chosen);
    }

    [Fact]
    public async Task Completion_HappensExactlyOnce_WhenAPickAndAForegroundResolveRace()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
            RecommendedResult = DataLocationPickResult.Success("/storage/emulated/0/EmuShelf"),
        };
        var completions = 0;
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => completions++);

        await vm.FocusAndActivateAsync(vm.Rows.Single(row => row.Key == "setup.folder.recommended"));
        bootstrap.Resolution = DataLocationResolution.Resolved("/storage/emulated/0/EmuShelf");
        bootstrap.RaisePermissionMaybeChanged();

        Assert.Equal(1, completions);
    }

    [Fact]
    public void DpadMovesFocus_AndUnmappedActionsAreNotConsumed()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = false, RecommendedBaseDirectory = "/x" };
        var vm = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.Equal("setup.folder.recommended", vm.FocusedRow!.Key);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.Equal("setup.folder.pick", vm.FocusedRow!.Key);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.Equal("setup.folder.recommended", vm.FocusedRow!.Key);
        Assert.False(vm.DispatchGamepadAction(GamepadAction.Search));
        Assert.False(vm.DispatchGamepadAction(GamepadAction.ResetRotation));
    }

    private sealed class FakeBootstrap : IDataLocationBootstrap
    {
        public DataLocationResolution Resolution { get; set; } =
            DataLocationResolution.Onboarding(DataLocationOnboardingReason.FirstRun);
        public DataLocationResolution Resolve() => Resolution;
        public bool RequiresStoragePermission { get; set; }
        public bool IsStoragePermissionGranted { get; set; }
        public string? RecommendedBaseDirectory { get; set; }
        public string? ExistingDataFolder { get; set; }
        public string? AdoptedFolder { get; private set; }
        public int GrantRequests { get; private set; }
        public DataLocationPickResult PickResult { get; set; } = DataLocationPickResult.Cancelled();
        public DataLocationPickResult RecommendedResult { get; set; } = DataLocationPickResult.Cancelled();
        public bool ShowSecondScreenReturnStep { get; set; }
        public bool IsSecondScreenReturnEnabled { get; set; }

        public event Action? StoragePermissionMaybeChanged;
        public void RaisePermissionMaybeChanged() => StoragePermissionMaybeChanged?.Invoke();

        public void RequestStoragePermission() => GrantRequests++;
        public void RequestSecondScreenReturn() { }
        public string? FindExistingDataFolder() => ExistingDataFolder;
        public Task<DataLocationPickResult> UseExistingFolderAsync(string baseDirectory)
        {
            AdoptedFolder = baseDirectory;
            return Task.FromResult(DataLocationPickResult.Success(baseDirectory));
        }
        public Task<DataLocationPickResult> UseRecommendedFolderAsync() => Task.FromResult(RecommendedResult);
        public Task<DataLocationPickResult> PickFolderAsync() => Task.FromResult(PickResult);
    }
}
