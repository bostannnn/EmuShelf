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

// Found on the Thor (M46 P4 follow-up): an app put in a folder from its Y sheet left its home slot drawn blank on the
// native Launcher, and taken back to home it was not drawn either, until the app was restarted.
public partial class HandheldShellViewModelTests
{
    // The share of a picture's pixels that are (nearly) opaque.
    private static double Opaque(EmuShelf.Rendering.Stage.StageImage image)
    {
        var data = image.Pixels.Data;
        var opaque = 0;
        for (var i = 3; i < data.Length; i += 4)
            if (data[i] > 200)
                opaque++;
        return opaque / (double)(data.Length / 4);
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task AnAppFoldedAndBack_IsRepicturedOnTheNativeLauncher(bool activity, bool wide)
    {
        using var log = new PlayLog();
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        library.ActivityFactory = log.Create;
        var store = new InMemoryHandheldHomeLayoutStore();
        var app = activity ? HandheldSlot.Activity : HandheldSlot.Settings;
        var other = activity ? HandheldSlot.Settings : HandheldSlot.Activity;
        const int at = 22;
        var slots = Enumerable.Repeat(HandheldSlot.Empty, at + 2).ToList();
        (slots[0], slots[1], slots[2], slots[at], slots[at + 1]) =
            (HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Recent, other, app, HandheldSlot.UserFolder("mine"));
        store.Save(HandheldHomeLayout.FromSlots(slots,
            [new HandheldUserFolder("mine", "Weekend", "#3BBD79", [])]));
        var screen = new RecordingNativeScreen();
        var shell = await CreateAsync(library, store);
        var launcher = new HandheldLauncherView(screen, pinnedWide: wide) { DataContext = shell };
        var window = new Window { Content = launcher, Width = wide ? 1920 : 1240, Height = 1080 };
        window.Show();
        try
        {
            await SettleAsync();
            // The shell's own grid (its shape follows the screen it is on): 22 is on its second page either way.
            var columns = shell.CurrentPage!.Columns;
            var perPage = shell.CurrentPage.Slots.Count;
            // The sprite ids follow the view's own shape, which a pinned view need not share with the shell's.
            var viewPerPage = (wide ? shell.WideHome : shell.NarrowHome).Grid.SlotsPerPage;
            string IdOf(int index) => $"h:{index / viewPerPage}:{index % viewPerPage}";
            var id = IdOf(at);
            void Focus(int index)
            {
                while (shell.HomePosition.Page < index / perPage)
                    Press(shell, GamepadAction.NextPlatform);
                var local = index % perPage;
                while (shell.HomePosition.Slot % columns < local % columns)
                    Press(shell, GamepadAction.NavigateRight);
                while (shell.HomePosition.Slot % columns > local % columns)
                    Press(shell, GamepadAction.NavigateLeft);
                while (shell.HomePosition.Slot / columns < local / columns)
                    Press(shell, GamepadAction.NavigateDown);
                Assert.Equal((index / perPage, local), (shell.HomePosition.Page, shell.HomePosition.Slot));
            }
            Focus(at);
            await SettleAsync();
            var before = Assert.Single(screen.Sprites(id), sprite => sprite.Id == id);

            Press(shell, GamepadAction.Actions);
            Assert.Equal(activity ? "Activity" : "Settings", shell.Sheet?.Title);
            RunSheetRow(shell, "Add to folder…");
            RunSheetRow(shell, "Weekend");
            await SettleAsync();
            Assert.Equal(-1, shell.Layout.IndexOf(app));
            var emptied = Assert.Single(screen.Sprites(id), sprite => sprite.Id == id);
            Assert.NotSame(before.Image, emptied.Image);
            Assert.Equal(before.Image.X, emptied.Image.X, 1f);

            Focus(at + 1);
            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            // Back to home: it lands on the first empty slot, on page 1, while page 2 is the one in view (as on the Thor).
            var landing = IdOf(3);
            var emptySlot = Assert.Single(screen.Sprites(landing), sprite => sprite.Id == landing);
            Press(shell, GamepadAction.Actions);
            RunSheetRow(shell, "Move to home");
            Press(shell, GamepadAction.Cancel);
            await SettleAsync();
            Assert.Equal(3, shell.Layout.IndexOf(app));
            var back = Assert.Single(screen.Sprites(landing), sprite => sprite.Id == landing);
            Assert.NotSame(emptySlot.Image, back.Image);
            // Placed where the slot is, not a page off (a stale frame of the Avalonia Launcher's motion put the page
            // shift back on the track after the native Launcher took the screen back).
            Assert.Equal(emptySlot.Image.X, back.Image.X, 1f);
            Assert.Equal(emptySlot.Image.Y, back.Image.Y, 1f);
            // Pictured whole, not caught mid-fade on the page out of view: the tile's teal face is in it.
            Assert.True(Opaque(back.Image) > Opaque(before.Image) * 0.9,
                $"only {Opaque(back.Image):P0} of the tile is opaque, against {Opaque(before.Image):P0} when first pictured");
        }
        finally
        {
            window.Close();
        }
    }
}
