using EmuShelf.App.ViewModels;
using Xunit;

namespace EmuShelf.App.Tests;

/// <summary>
/// The companion's "a game is playing on the other screen" dim standby. IsStandby is what the view binds to
/// wash the idle surface near-black with a faint logo, and it must lift the moment anything is shown over it
/// (the achievements/app-drawer overlay) and drop back when they close — the requested behaviour for the
/// achievement viewer.
/// </summary>
public class SecondScreenStandbyTests
{
    [Fact]
    public void NotStandby_WhileBrowsing()
    {
        var vm = new SecondScreenViewModel();

        Assert.False(vm.IsStandby);
    }

    [Fact]
    public void Standby_WhenGameRunning_AndNothingOver()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true };

        Assert.True(vm.IsStandby);
    }

    [Fact]
    public void OpeningAchievements_LiftsTheDim_ClosingRestoresIt()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true };
        Assert.True(vm.IsStandby);

        vm.Overlay = SecondScreenOverlayKind.Achievements;
        Assert.False(vm.IsStandby);

        vm.Overlay = SecondScreenOverlayKind.None;
        Assert.True(vm.IsStandby);
    }

    [Fact]
    public void OpeningTheDrawer_LiftsTheDim()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true, Overlay = SecondScreenOverlayKind.Drawer };

        Assert.False(vm.IsStandby);
    }

    [Fact]
    public void IsStandby_RaisesChange_WhenGameStartsAndStops()
    {
        var vm = new SecondScreenViewModel();
        var changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SecondScreenViewModel.IsStandby))
                changes++;
        };

        vm.IsGameRunning = true;
        vm.IsGameRunning = false;

        Assert.Equal(2, changes);
    }
}
