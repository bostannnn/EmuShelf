using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>M47 B14's leftovers under Big Type: what earlier steps found and left. Classic is untouched in each.</summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeLeftovers
    {
        private static async Task BigType(Func<Task> test)
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                await test();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        // ---- 1. A dock app and the Start menu: no sweep ----

        [Fact]
        public Task ADockApp_AndTheStartMenu_KeepTheBackdropThatWasShowing_WithNoSweep() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            var companion = new SecondScreenViewModel();
            companion.Dock[0].Label = "Music";
            companion.Dock[0].AccentColor = Color.Parse("#D9443B");
            shell.Companion = companion;
            var console = shell.Stage.Backdrop;
            Assert.NotNull(console);
            Assert.False(console!.Sweep);

            // Down to the dock, right to its first app: the mock's focusDock: the app's name over what was showing.
            Press(shell, GamepadAction.NavigateDown, 4);
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(HandheldStageKind.App, shell.Stage.Kind);
            Assert.Equal("Music", shell.Stage.Name);
            Assert.Equal("Dock", shell.Stage.Label);
            Assert.Equal(console, shell.Stage.Backdrop);
            // Its colour arriving later changes nothing on the Stage.
            companion.Dock[0].AccentColor = Color.Parse("#3B7DD9");
            Assert.Equal(console, shell.Stage.Backdrop);

            // The Start menu over it: still no circle.
            Press(shell, GamepadAction.Menu);
            Assert.True(shell.IsMenuOpen);
            Assert.Equal(console, shell.Stage.Backdrop);
            Assert.DoesNotContain(new[] { shell.Stage.Backdrop }, backdrop => backdrop?.Sweep == true);
            shell.Dispose();
        });

        [Fact]
        public async Task Classic_ADockApp_KeepsItsSweep()
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            var companion = new SecondScreenViewModel();
            companion.Dock[0].Label = "Music";
            companion.Dock[0].AccentColor = Color.Parse("#D9443B");
            shell.Companion = companion;
            Press(shell, GamepadAction.NavigateDown, 4);
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(HandheldShellViewModel.AppSweep(Color.Parse("#D9443B")), shell.Stage.Backdrop);
            shell.Dispose();
        }
    }
}
