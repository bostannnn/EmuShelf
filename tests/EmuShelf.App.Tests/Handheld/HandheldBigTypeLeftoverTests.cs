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
            // Empty slots on the way down use Lift-off (B13). The dock app and menu
            // keep the backdrop at the dock entrance, even after the app icon colour arrives.
            Press(shell, GamepadAction.NavigateDown, 4);
            var dockBackdrop = shell.Stage.Backdrop;
            Assert.NotNull(dockBackdrop);
            Assert.Equal(new[] { "lift-off" }, dockBackdrop.Animated);
            Assert.False(dockBackdrop.Sweep);
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(HandheldStageKind.App, shell.Stage.Kind);
            Assert.Equal("Music", shell.Stage.Name);
            Assert.Equal("Dock", shell.Stage.Label);
            Assert.Equal(dockBackdrop, shell.Stage.Backdrop);
            // Its colour arriving later changes nothing on the Stage.
            companion.Dock[0].AccentColor = Color.Parse("#3B7DD9");
            Assert.Equal(dockBackdrop, shell.Stage.Backdrop);

            // The Start menu over it: still no circle.
            Press(shell, GamepadAction.Menu);
            Assert.True(shell.IsMenuOpen);
            Assert.Equal(dockBackdrop, shell.Stage.Backdrop);
            Assert.DoesNotContain(new[] { shell.Stage.Backdrop }, backdrop => backdrop?.Sweep == true);
            shell.Dispose();
        });

        // ---- 2. A panel's picture where the case stands ----

        [Avalonia.Headless.XUnit.AvaloniaFact]
        public Task AnAchievementsBadge_IsThePictureBesideTheName() => BigType(async () =>
        {
            var badge = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"emushelf-badge-{Guid.NewGuid():N}.png");
            await System.IO.File.WriteAllBytesAsync(badge, Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
            try
            {
                using var panel = new HandheldAchievementsPanel(Achievements(new FakeAchievements { BadgePath = badge }));
                for (var i = 0; i < 200 && panel.Card.Image is null; i++)
                    await Task.Delay(10);
                Assert.NotNull(panel.Card.Image);
                // The mock's panels: the focused row's picture stands where the case does, beside the name.
                Assert.Same(panel.Card.Image, panel.BigTypeCover);
                Assert.True(panel.Card.IsSquareImage);
            }
            finally
            {
                System.IO.File.Delete(badge);
            }
        });

        [Fact]
        public Task WhileTyping_NoPictureIsSet() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            await shell.OpenSearchAsync();
            var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            // Typing: nothing beside the name.
            Assert.Null(search.BigTypeCover);
            Assert.Null(shell.Stage.ListCover);
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
