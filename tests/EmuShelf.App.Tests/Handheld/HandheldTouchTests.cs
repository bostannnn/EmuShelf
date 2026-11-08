using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H4's touch exit: home can be browsed and arranged by touch alone. These drive the real Launcher view with
// pointer events only (no pad), so the view's hit-testing is covered as well as the shell.
public partial class HandheldShellViewModelTests
{
    private sealed class TouchRig(Window window, HandheldLauncherView view, HandheldShellViewModel shell)
    {
        public HandheldShellViewModel Shell { get; } = shell;

        public async Task SettleAsync()
        {
            for (var i = 0; i < 4; i++)
            {
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
        }

        private Point Centre(Control control)
        {
            var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
            Assert.NotNull(centre);
            return centre.Value;
        }

        private Control Find(Func<Control, bool> match) =>
            view.GetVisualDescendants().OfType<Control>().First(control => control.IsEffectivelyVisible && control.Bounds.Width > 0 && match(control));

        public Control Named(string name) => Find(control => control.Name == name);

        public Control Showing<T>(Func<T, bool> match) where T : class =>
            Find(control => control.DataContext is T item && match(item) && control is Border or Panel);

        public Control HomeSlot(int index) =>
            Find(control => control is Panel && control.Classes.Contains("home-slot") &&
                control.DataContext is HandheldHomeSlotViewModel slot && slot.Index == index &&
                ReferenceEquals(Shell.CurrentPage?.Slots.ElementAtOrDefault(index), slot));

        public async Task TapAsync(Control control) => await TapAtAsync(Centre(control));

        public async Task TapAtAsync(Point point)
        {
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            // Let the motion a tap starts (a folder zooming open, a sheet) finish, as a finger would wait for it:
            // the zoom is a compositor animation, so the page is only where it lands once it has landed.
            await Task.Delay(450);
            await SettleAsync();
        }

        public async Task SwipeAsync(Control from, double dx)
        {
            var start = Centre(from);
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            window.MouseMove(start + new Point(dx / 2, 0), RawInputModifiers.LeftMouseButton);
            window.MouseUp(start + new Point(dx, 0), MouseButton.Left, RawInputModifiers.None);
            await Task.Delay(450);
            await SettleAsync();
        }

        public async Task LongPressAsync(Control control)
        {
            var at = Centre(control);
            window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            await Task.Delay(700);
            await SettleAsync();
            window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            await Task.Delay(450);
            await SettleAsync();
        }

        /// <summary>A long press on <paramref name="from"/>, then a drag to <paramref name="to"/> and a release.</summary>
        public async Task LongPressDragAsync(Control from, Func<Control> to)
        {
            var start = Centre(from);
            window.MouseDown(start, MouseButton.Left, RawInputModifiers.None);
            await Task.Delay(700);
            await SettleAsync();
            Assert.True(Shell.IsMoving);
            var end = Centre(to());
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            await SettleAsync();
            window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
            await SettleAsync();
        }
    }

    private static async Task<TouchRig> TouchAsync(FakeLibrary library, InMemoryHandheldHomeLayoutStore? store = null)
    {
        var shell = await CreateAsync(library, store);
        var view = new HandheldLauncherView { DataContext = shell };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        var rig = new TouchRig(window, view, shell);
        await rig.SettleAsync();
        return rig;
    }

    [AvaloniaFact]
    public async Task Touch_BrowsesHome_OpensAFolder_SortsIt_AndGoesBack()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 20).WithPlatform("gc", 3);
        var rig = await TouchAsync(library);
        var shell = rig.Shell;

        // A tap selects; a second tap opens.
        await rig.TapAsync(rig.HomeSlot(1));
        Assert.Equal(1, shell.HomePosition.Slot);
        Assert.False(shell.IsFolderOpen);
        await rig.TapAsync(rig.HomeSlot(1));
        Assert.Equal("GC System", shell.OpenFolder!.Name);

        await rig.TapAsync(rig.Named("BackShot"));
        Assert.False(shell.IsFolderOpen);

        // A swipe turns the page; the edge hint turns it back.
        await rig.SwipeAsync(rig.HomeSlot(7), -300);
        Assert.Equal(1, shell.HomePosition.Page);
        await rig.TapAsync(rig.Named("EdgeLeft"));
        Assert.Equal(0, shell.HomePosition.Page);

        // In a folder: a swipe scrolls a screen of columns, the sort chip sorts, a tile opens on its second tap.
        await rig.TapAsync(rig.HomeSlot(0));
        await rig.TapAsync(rig.HomeSlot(0));
        var folder = shell.OpenFolder!;
        Assert.Equal(0, folder.FirstVisibleColumn);
        await rig.SwipeAsync(rig.Named("FolderTiles"), -300);
        Assert.True(folder.FirstVisibleColumn > 0);
        await rig.TapAsync(rig.Named("SortShot"));
        Assert.Equal(HandheldFolderSort.Title, folder.Sort);
        // A tile in the second column on screen (the sort kept the selected game in view).
        var tile = folder.VisibleTiles.First(t => t.Index == ((folder.FirstVisibleColumn + 1) * HandheldFolderNavigation.Rows) + 1);
        await rig.TapAsync(rig.Showing<HandheldGameTileViewModel>(t => ReferenceEquals(t, tile)));
        Assert.Equal(tile.Index, folder.SelectedIndex);
        await rig.TapAsync(rig.Showing<HandheldGameTileViewModel>(t => ReferenceEquals(t, tile)));
        Assert.True(shell.IsTitleOpen);
    }

    [AvaloniaFact]
    public async Task Touch_PlaysFromTheTitlePage_ThroughTheScreenChooser()
    {
        var library = new FakeLibrary().WithPlatform("gc", 2);
        library.Details[library.Games["gc"][0].Id] = HandheldTitleDetails.Default with
        {
            Screen = LaunchScreenDecision.Prompt,
            HasSecondScreen = true,
        };
        var rig = await TouchAsync(library);
        var shell = rig.Shell;
        await rig.TapAsync(rig.HomeSlot(0));
        await rig.TapAsync(rig.HomeSlot(0));
        var tile = shell.OpenFolder!.VisibleTiles[0];
        await rig.TapAsync(rig.Showing<HandheldGameTileViewModel>(t => ReferenceEquals(t, tile)));
        Assert.True(shell.IsTitleOpen);

        await rig.TapAsync(rig.Showing<HandheldTitleButtonViewModel>(b => b.Kind == HandheldTitleButtonKind.Play));
        Assert.True(shell.TitlePage!.IsScreenChooser);
        await rig.TapAsync(rig.Named("RememberShot"));
        Assert.True(shell.TitlePage.RememberScreen);
        await rig.TapAsync(rig.Named("ScreenBottomShot"));

        Assert.Equal([GameLaunchScreen.External], library.LaunchScreens);
        Assert.Equal([("gc", GameLaunchScreen.External)], library.Remembered);
    }

    [AvaloniaFact]
    public async Task Touch_ArrangesHome_PinMoveAndAFolderMadeOnTheKeyboard()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var rig = await TouchAsync(library, store);
        var shell = rig.Shell;

        // Long-press the platform folder and drag it two slots along.
        await rig.LongPressDragAsync(rig.HomeSlot(0), () => rig.HomeSlot(3));
        Assert.False(shell.IsMoving);
        Assert.Equal(HandheldSlot.PlatformFolder("ps2"), store.Saved!.SlotAt(3));

        // Tap an empty slot twice: name a folder by tapping keys, then Done.
        await rig.TapAsync(rig.HomeSlot(7));
        await rig.TapAsync(rig.HomeSlot(7));
        Assert.True(shell.IsNaming);
        foreach (var letter in new[] { "o", "k" })
            await rig.TapAsync(rig.Showing<HandheldKeyViewModel>(key => key.Label == letter));
        await rig.TapAsync(rig.Showing<HandheldColorChipViewModel>(chip => chip.Color == HandheldHomeLayout.FolderColors[4]));
        await rig.TapAsync(rig.Showing<HandheldKeyViewModel>(key => key.Kind == HandheldKeyKind.Done));
        var folder = Assert.Single(store.Saved!.Folders);
        Assert.Equal("Ok", folder.Name);
        Assert.Equal(HandheldHomeLayout.FolderColors[4], folder.Color);

        // The menu from the dock, closed by a tap on its scrim.
        await rig.TapAsync(rig.Showing<HandheldDockItemViewModel>(item => item.IsMenu));
        Assert.True(shell.IsMenuOpen);
        await rig.TapAtAsync(new Point(40, 40));
        Assert.False(shell.IsMenuOpen);
    }

    [AvaloniaFact]
    public async Task Touch_ALongPressWithoutDragging_OpensTheActionsSheet_SoPinningWorksByTouch()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var store = new InMemoryHandheldHomeLayoutStore();
        var rig = await TouchAsync(library, store);
        var shell = rig.Shell;

        // A long press on a home tile, let go in place: its sheet, home unchanged.
        await rig.LongPressAsync(rig.HomeSlot(0));
        Assert.False(shell.IsMoving);
        Assert.Equal("PS2 System", shell.Sheet!.Title);
        await rig.TapAtAsync(new Avalonia.Point(40, 40));
        Assert.Null(shell.Sheet);

        // A long press on an empty slot: its sheet.
        await rig.LongPressAsync(rig.HomeSlot(9));
        Assert.Equal("Empty slot", shell.Sheet!.Title);
        await rig.TapAtAsync(new Avalonia.Point(40, 40));

        // Inside a folder, a long press on a game opens its sheet; a tap on a row runs it.
        await rig.TapAsync(rig.HomeSlot(0));
        await rig.TapAsync(rig.HomeSlot(0));
        var tile = shell.OpenFolder!.VisibleTiles[1];
        await rig.LongPressAsync(rig.Showing<HandheldGameTileViewModel>(t => ReferenceEquals(t, tile)));
        Assert.False(shell.IsTitleOpen);
        Assert.Equal(tile.Entry.Title, shell.Sheet!.Title);
        await rig.TapAsync(rig.Showing<HandheldSheetItemViewModel>(item => item.Label == "Pin to home"));
        Assert.True(store.Saved!.IsPinned(tile.Entry.Id));
    }
}
