using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// The real Launcher view hosted headlessly through H4's states (a pin and a ribbon on home, move mode, the
// actions sheet, naming). The bottom screen is FLAG_SECURE on the Thor, so this is how its look is checked on
// macOS. Set EMUSHELF_SNAPSHOT_DIR to get the frames as PNGs.
public partial class HandheldShellViewModelTests
{
    private static async Task SettleAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(30);
        }
    }

    private static void Dump(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        using (window.CaptureRenderedFrame()) { }
        using var frame = window.CaptureRenderedFrame();
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        frame!.Save(stream, PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public async Task Launcher_DrawsTheArrangingStates()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 4).WithPlatform("gc", 2);
        var store = new InMemoryHandheldHomeLayoutStore();
        var (a, b) = (library.Games["ps2"][0].Id, library.Games["ps2"][1].Id);
        store.Save(HandheldHomeLayout.FromSlots(
            [HandheldSlot.Recent, HandheldSlot.PlatformFolder("ps2"), HandheldSlot.PlatformFolder("gc", isNew: true),
             HandheldSlot.UserFolder("mine"), HandheldSlot.Game(a), HandheldSlot.Game(b)],
            [new HandheldUserFolder("mine", "Couch co-op", "#3BBD79", [a])]));
        var shell = new HandheldShellViewModel(library, layoutStore: store);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);
        var view = new HandheldLauncherView { DataContext = shell };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await SettleAsync();
            Dump(window, "h4-0-home");
            Assert.True(shell.CurrentPage!.Slots[2].IsNew);
            Assert.True(shell.CurrentPage.Slots[4].IsGame);

            FocusHome(shell, 5);
            Press(shell, GamepadAction.Actions);
            await SettleAsync();
            Dump(window, "h4-1-sheet");
            Assert.True(view.FindControl<Border>("SheetLayer")!.IsVisible);

            RunSheetRow(shell, "Move");
            // Slot 5 starts the second row: up to Recent's row, then right onto the other pin (slot 4).
            Press(shell, GamepadAction.NavigateUp);
            Press(shell, GamepadAction.NavigateRight, 4);
            Assert.True(shell.CurrentPage!.Slots[4].IsDropTarget);
            await SettleAsync();
            Dump(window, "h4-2-move-merge");
            Press(shell, GamepadAction.NavigateDown);
            await SettleAsync();
            Dump(window, "h4-3-move-place");

            Press(shell, GamepadAction.NavigateUp);
            Press(shell, GamepadAction.Confirm);
            shell.Naming!.Text = "Weekend";
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.NavigateRight, 3);
            Assert.True(shell.Naming.KeyRows[1].Keys[3].IsFocused);
            await SettleAsync();
            Dump(window, "h4-4-naming");
            Assert.True(view.FindControl<Border>("NamingLayer")!.IsVisible);

            Press(shell, GamepadAction.Menu);
            await SettleAsync();
            Dump(window, "h4-5-made");
            Assert.Equal(2, shell.Layout.Folders.Count);
        }
        finally
        {
            window.Close();
        }
    }
}
