using System;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.Tests;

public sealed class OnboardingViewModelTests
{
    [Fact]
    public void FolderActionsDisabled_UntilRequiredGrantIsHeld()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.False(vm.CanChooseFolder);

        bootstrap.IsStoragePermissionGranted = true;
        vm.RefreshPermissionState();

        Assert.True(vm.IsPermissionGranted);
        Assert.True(vm.CanChooseFolder);
    }

    [Fact]
    public void ForegroundEvent_RefreshesPermissionState()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.StoragePermissionMissing, _ => { });
        Assert.False(vm.CanChooseFolder);

        bootstrap.IsStoragePermissionGranted = true;
        bootstrap.RaisePermissionMaybeChanged();

        Assert.True(vm.CanChooseFolder);
    }

    [Fact]
    public void GrantCommand_SendsUserToTheGrantSurface()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        vm.GrantPermissionCommand.Execute(null);

        Assert.Equal(1, bootstrap.GrantRequests);
    }

    [Fact]
    public async Task RecommendedFolder_CompletesWithItsBaseDirectory()
    {
        string? chosen = null;
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            IsStoragePermissionGranted = true,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
            RecommendedResult = DataLocationPickResult.Success("/storage/emulated/0/EmuShelf"),
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, dir => chosen = dir);

        await vm.UseRecommendedCommand.ExecuteAsync(null);

        Assert.Equal("/storage/emulated/0/EmuShelf", chosen);
    }

    [Fact]
    public async Task ChooseDifferent_CompletesWithTheChosenBaseDirectory()
    {
        string? chosen = null;
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            IsStoragePermissionGranted = true,
            PickResult = DataLocationPickResult.Success("/storage/AE6A-1092/EmuShelf"),
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, dir => chosen = dir);

        await vm.ChooseDifferentCommand.ExecuteAsync(null);

        Assert.Equal("/storage/AE6A-1092/EmuShelf", chosen);
    }

    [Fact]
    public async Task RejectedSelection_ShowsTheReason_AndDoesNotComplete()
    {
        var completed = false;
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            IsStoragePermissionGranted = true,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
            RecommendedResult = DataLocationPickResult.Failed("EmuShelf can't write there yet."),
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => completed = true);

        await vm.UseRecommendedCommand.ExecuteAsync(null);

        Assert.False(completed);
        Assert.Equal("EmuShelf can't write there yet.", vm.StatusMessage);
    }

    [Fact]
    public async Task CancelledPick_LeavesTheInstructionMessageUnchanged()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            IsStoragePermissionGranted = true,
            PickResult = DataLocationPickResult.Cancelled(),
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });
        var before = vm.StatusMessage;

        await vm.ChooseDifferentCommand.ExecuteAsync(null);

        Assert.Equal(before, vm.StatusMessage);
    }

    [Fact]
    public void GamepadFocus_StartsOnGrant_WhenGrantIsOutstanding()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.True(vm.IsGrantFocused);
        Assert.False(vm.IsRecommendedFocused);
        Assert.False(vm.IsChooseFocused);
    }

    [Fact]
    public void GamepadFocus_StartsOnRecommended_WhenGranted()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            IsStoragePermissionGranted = true,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.True(vm.IsRecommendedFocused);
        Assert.False(vm.IsChooseFocused);
    }

    [Fact]
    public void GamepadNavigate_MovesBetweenRecommendedAndChoose()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            IsStoragePermissionGranted = true,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });
        Assert.True(vm.IsRecommendedFocused);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.True(vm.IsChooseFocused);
        Assert.False(vm.IsRecommendedFocused);

        vm.DispatchGamepadAction(GamepadAction.NavigateUp);
        Assert.True(vm.IsRecommendedFocused);
    }

    [Fact]
    public void GamepadConfirm_OnGrant_RequestsTheGrant()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = true, IsStoragePermissionGranted = false };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));

        Assert.Equal(1, bootstrap.GrantRequests);
    }

    [Fact]
    public void GamepadConfirm_OnRecommended_CompletesWithRecommendedFolder()
    {
        string? chosen = null;
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = false,
            IsStoragePermissionGranted = true,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
            RecommendedResult = DataLocationPickResult.Success("/storage/emulated/0/EmuShelf"),
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, dir => chosen = dir);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));

        Assert.Equal("/storage/emulated/0/EmuShelf", chosen);
    }

    [Fact]
    public void GrantLanding_MovesFocusToRecommended_AndDropsTheGrantStep()
    {
        var bootstrap = new FakeBootstrap
        {
            RequiresStoragePermission = true,
            IsStoragePermissionGranted = false,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
        };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });
        Assert.True(vm.IsGrantFocused);

        bootstrap.IsStoragePermissionGranted = true;
        bootstrap.RaisePermissionMaybeChanged();

        Assert.False(vm.IsGrantStepActive);
        Assert.True(vm.IsRecommendedFocused);
        Assert.False(vm.IsGrantFocused);
    }

    [Fact]
    public void GamepadUnmappedActions_AreNotConsumed()
    {
        var bootstrap = new FakeBootstrap { RequiresStoragePermission = false, IsStoragePermissionGranted = true };
        var vm = new OnboardingViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });

        Assert.False(vm.DispatchGamepadAction(GamepadAction.Menu));
        Assert.False(vm.DispatchGamepadAction(GamepadAction.NextPlatform));
    }

    private sealed class FakeBootstrap : IDataLocationBootstrap
    {
        public string? ResolvedBaseDirectory => null;
        public DataLocationOnboardingReason OnboardingReason => DataLocationOnboardingReason.FirstRun;
        public bool RequiresStoragePermission { get; set; }
        public bool IsStoragePermissionGranted { get; set; }
        public string? RecommendedBaseDirectory { get; set; }
        public int GrantRequests { get; private set; }
        public DataLocationPickResult PickResult { get; set; } = DataLocationPickResult.Cancelled();
        public DataLocationPickResult RecommendedResult { get; set; } = DataLocationPickResult.Cancelled();

        public event Action? StoragePermissionMaybeChanged;
        public void RaisePermissionMaybeChanged() => StoragePermissionMaybeChanged?.Invoke();

        public void RequestStoragePermission() => GrantRequests++;
        public Task<DataLocationPickResult> UseRecommendedFolderAsync() => Task.FromResult(RecommendedResult);
        public Task<DataLocationPickResult> PickFolderAsync() => Task.FromResult(PickResult);
    }
}
