using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H5: X swaps the screens. Home is cut into 7×3 pages on the 16:9 screen, the selection, an open folder and
// its scroll survive, and the choice is remembered.
public partial class HandheldShellViewModelTests
{
    /// <summary>Twenty-five tiles: two platform folders, then 23 of the first platform's games pinned.</summary>
    private static async Task<(HandheldShellViewModel Shell, InMemoryHandheldHomeLayoutStore Store)> CreateFullHomeAsync(
        bool swapped = false, Action<bool>? save = null)
    {
        var library = new FakeLibrary().WithPlatform("ps2", 30).WithPlatform("gc", 2);
        var store = new InMemoryHandheldHomeLayoutStore();
        var slots = new List<HandheldSlot> { HandheldSlot.PlatformFolder("ps2"), HandheldSlot.PlatformFolder("gc") };
        slots.AddRange(library.Games["ps2"].Take(23).Select(game => HandheldSlot.Game(game.Id)));
        store.Save(HandheldHomeLayout.FromSlots(slots));
        var shell = new HandheldShellViewModel(library, layoutStore: store, screensSwapped: swapped, saveScreensSwapped: save);
        await shell.InitializeAsync();
        return (shell, store);
    }

    [Fact]
    public void WideGrid_MovesAcrossSevenColumnsAndThreeRows()
    {
        var grid = HandheldHomeGrid.Wide;
        Assert.Equal(21, grid.SlotsPerPage);
        Assert.Equal(2, grid.PageCount(40));
        Assert.Equal(new HandheldHomePosition(1, 1), grid.PositionOf(22));

        // Right off the last column turns the page onto the same row's first column.
        var (next, outcome) = HandheldHomeNavigation.Move(new HandheldHomePosition(0, 13), HandheldDirection.Right, 2, grid);
        Assert.Equal((new HandheldHomePosition(1, 7), HandheldMoveOutcome.Moved), (next, outcome));
        // Down from the third row leaves for the dock.
        Assert.Equal(HandheldMoveOutcome.ExitDown,
            HandheldHomeNavigation.Move(new HandheldHomePosition(0, 15), HandheldDirection.Down, 2, grid).Outcome);
        Assert.Equal(HandheldMoveOutcome.Moved,
            HandheldHomeNavigation.Move(new HandheldHomePosition(0, 8), HandheldDirection.Down, 2, grid).Outcome);
    }

    [Fact]
    public async Task Swap_ReflowsHomeIntoWidePages_KeepingTheSelectedSlot()
    {
        var (shell, _) = await CreateFullHomeAsync();
        Press(shell, GamepadAction.NextPlatform);
        FocusHome(shell, 2); // page 2, slot 2: tile 22 of the saved layout
        Assert.Equal(new HandheldHomePosition(1, 2), shell.HomePosition);
        var focused = shell.CurrentPage!.Slots[2].Slot;

        Press(shell, GamepadAction.Search);

        Assert.True(shell.IsSwapped);
        Assert.Equal((7, 3), (shell.HomeColumns, shell.HomeRows));
        Assert.Equal(1920, shell.LauncherCanvasWidth);
        Assert.Equal(1240, shell.StageCanvasWidth);
        Assert.True(shell.IsStageNarrow);
        // 40 saved slots are two 7×3 pages; the last two slots are padding, drawn empty.
        Assert.Equal(2, shell.PageCount);
        Assert.All(shell.Pages, page => Assert.Equal(21, page.Slots.Count));
        Assert.True(shell.Pages[1].Slots[19].IsEmpty);
        Assert.Equal(new HandheldHomePosition(1, 1), shell.HomePosition);
        Assert.Equal(focused, shell.CurrentPage!.Slots[1].Slot);
        Assert.True(shell.CurrentPage.Slots[1].IsFocused);
        Assert.Equal("Page 2 of 2", shell.PageLabel);

        Press(shell, GamepadAction.Search);
        Assert.False(shell.IsSwapped);
        Assert.Equal(new HandheldHomePosition(1, 2), shell.HomePosition);
        Assert.All(shell.Pages, page => Assert.Equal(20, page.Slots.Count));
    }

    [Fact]
    public async Task Swap_KeepsTheOpenFolder_ItsSelectionAndScroll()
    {
        var (shell, _) = await CreateFullHomeAsync();
        Press(shell, GamepadAction.Confirm);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        var folder = Assert.IsType<HandheldFolderViewModel>(shell.OpenFolder);
        Press(shell, GamepadAction.NavigateRight, 6); // column 6: the window has scrolled to column 3
        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(19, folder.SelectedIndex);
        Assert.Equal(3, folder.FirstVisibleColumn);

        Press(shell, GamepadAction.Search);

        Assert.Same(folder, shell.OpenFolder);
        Assert.Equal(19, folder.SelectedIndex);
        Assert.True(folder.SelectedTile!.IsFocused);
        // Each screen keeps its own scroll, both following the selection: seven columns from column 0 show the
        // selected column 6 on the 16:9 screen, and swapping back finds the narrow screen still at column 3.
        Assert.Equal(7, shell.FolderVisibleColumns);
        Assert.Equal(0, folder.FirstVisibleColumn);
        Assert.Equal((3, 0), (folder.NarrowFirstColumn, folder.WideFirstColumn));
        Assert.Equal(0, shell.HomePosition.Slot);
        Press(shell, GamepadAction.Search);
        Assert.Equal(3, folder.FirstVisibleColumn);
        Press(shell, GamepadAction.Search);

        // B still lands on the folder's own tile.
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.OpenFolder);
        Assert.Equal(new HandheldHomePosition(0, 0), shell.HomePosition);
    }

    [Fact]
    public async Task Swap_IsRemembered_AndRestoredAtStart()
    {
        var saved = new List<bool>();
        var (shell, _) = await CreateFullHomeAsync(save: saved.Add);
        Press(shell, GamepadAction.Search);
        Assert.Equal([true], saved);

        var (restored, _) = await CreateFullHomeAsync(swapped: true);
        Assert.True(restored.IsSwapped);
        Assert.Equal(HandheldHomeGrid.Wide, restored.HomeGrid);
        Assert.Equal(21, restored.CurrentPage!.Slots.Count);
    }

    [Fact]
    public async Task Swap_WaitsForTheHost_AndAppliesWhileTheScreensAreOut()
    {
        var (shell, _) = await CreateFullHomeAsync();
        var release = new TaskCompletionSource();
        bool? swappedDuringSlide = null;
        shell.ScreenSwapHost = async apply =>
        {
            swappedDuringSlide = shell.IsSwapped;
            await release.Task;
            apply();
        };
        Press(shell, GamepadAction.Search);
        // A second X while the slide runs is ignored.
        Press(shell, GamepadAction.Search);
        Assert.False(shell.IsSwapped);
        release.SetResult();
        await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.False(swappedDuringSlide);
        Assert.True(shell.IsSwapped);
    }

    [Fact]
    public async Task WideMoveMode_CanDropPastTheSavedSlots_GrowingHome()
    {
        var (shell, store) = await CreateFullHomeAsync(swapped: true);
        Press(shell, GamepadAction.NextPlatform);
        // Page 2's last slot is index 41, past the 40 saved slots.
        Press(shell, GamepadAction.NavigateRight, 6);
        Press(shell, GamepadAction.NavigateDown, 2);
        Assert.Equal(new HandheldHomePosition(1, 20), shell.HomePosition);
        Press(shell, GamepadAction.PreviousPlatform);
        Press(shell, GamepadAction.NavigateLeft, 6);
        Press(shell, GamepadAction.NavigateUp, 2);
        Assert.Equal(new HandheldHomePosition(0, 0), shell.HomePosition);

        shell.StartMove();
        shell.CarryTo(41);
        shell.Drop();

        Assert.Equal(HandheldSlotKind.PlatformFolder, store.Saved!.SlotAt(41).Kind);
        Assert.Equal(3, store.Saved.Pages.Count); // 41 is on the saved layout's third 5×4 page
        Assert.Equal(3, shell.PageCount);         // 60 slots: three 7×3 pages
    }

    [AvaloniaFact]
    public async Task SwappedScreens_DrawTheWideLauncherAndTheNarrowStage()
    {
        var (shell, _) = await CreateFullHomeAsync();
        Press(shell, GamepadAction.Search);
        var launcher = new HandheldLauncherView { DataContext = shell };
        var stage = new HandheldStageView { DataContext = shell };
        var top = new Window { Content = launcher, Width = 1920, Height = 1080 };
        var bottom = new Window { Content = stage, Width = 1240, Height = 1080 };
        top.Show();
        bottom.Show();
        try
        {
            await SettleAsync();
            Dump(top, "h5-0-launcher-wide");
            Dump(bottom, "h5-1-stage-narrow");
            Assert.Equal(1920, launcher.FindControl<Grid>("Canvas")!.Bounds.Width);
            Assert.True(launcher.FindControl<TextBlock>("PageLabel")!.IsVisible);
            Assert.Equal(1240, stage.FindControl<Grid>("StageCanvas")!.Bounds.Width);
            // The hints go top-left on the narrow screen.
            var hints = stage.FindControl<Border>("HintsShot")!;
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Left, hints.HorizontalAlignment);
            Assert.Equal(Avalonia.Layout.VerticalAlignment.Top, hints.VerticalAlignment);

            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Dump(top, "h5-2-folder-wide");
            Press(shell, GamepadAction.NavigateRight);
            await SettleAsync();
            Dump(bottom, "h5-3-stage-narrow-game");

            Press(shell, GamepadAction.Search);
            await SettleAsync();
            Assert.Equal(1240, launcher.FindControl<Grid>("Canvas")!.Bounds.Width);
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right, hints.HorizontalAlignment);
        }
        finally
        {
            top.Close();
            bottom.Close();
        }
    }
}
