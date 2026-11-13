using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Rendering.Stage;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// A home folder's tile goes to the native Launcher as an empty picture plus its art as parts. Found on the Thor
// (2026-11-13): a folder tile sat blank on home until it was focused. A tile pictured while its art could not be
// captured went over without the art, and with its key marked sent nothing pictured it again.
public partial class HandheldShellViewModelTests
{
    [AvaloniaFact]
    public async Task NativeLauncher_FolderArtThatCannotBePictured_KeepsTheOldArtAndTriesAgain()
    {
        var launcherScreen = new RecordingNativeScreen();
        var previous = HandheldNativeStage.Launcher;
        HandheldNativeStage.Launcher = launcherScreen;
        Window? window = null;
        try
        {
            var library = new FakeLibrary().WithPlatform("ps2", 5).WithPlatform("gc", 5);
            library.CoverMaker = MakeCover;
            var shell = await CreateAsync(library);
            var launcher = new HandheldLauncherView { DataContext = shell };
            window = new Window { Content = launcher, Width = 1240, Height = 1080 };
            window.Show();
            await SettleAsync();

            // The second tile, GameCube's folder, unfocused: pictured with its art.
            const string id = "h:0:1";
            LauncherSprite Sent() => Assert.IsType<LauncherSpriteCommand>(launcherScreen.Retained["sprite:" + id]).Sprite!;
            Assert.NotNull(Sent().Folder);
            var art = launcher.GetVisualDescendants().OfType<HandheldFolderArt>()
                .Single(candidate => candidate.DataContext is HandheldHomeSlotViewModel { Folder.Platform.SystemId: "gc" });

            // Its look changes (focused: its covers peek out) at a moment its art can't be pictured.
            art.IsVisible = false;
            Press(shell, GamepadAction.NavigateRight);
            await SettleAsync();
            Assert.NotNull(shell.CurrentPage!.Slots[1].PeekA);
            var kept = Sent();
            Assert.NotNull(kept.Folder);
            Assert.NotEmpty(kept.Folder!.Parts);

            // Once it can be, it is pictured again with the covers, and its flap peeks.
            art.IsVisible = true;
            await SettleAsync();
            var again = Sent();
            Assert.NotSame(kept, again);
            Assert.NotEmpty(again.Folder!.Parts);
            Assert.Equal(FolderArtState.Peek,
                Assert.IsType<LauncherFolderStateCommand>(launcherScreen.Retained["fstate:" + id]).State);
        }
        finally
        {
            window?.Close();
            HandheldNativeStage.Launcher = previous;
        }
    }
}
