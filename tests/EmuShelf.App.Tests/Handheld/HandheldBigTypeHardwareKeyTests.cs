using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>M47 B14 leftover 5 under Big Type: a key typed on a hardware keyboard (through the hidden field) moves the ring to that key, as the mock's kbJump.</summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeHardwareKeys
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

        // ---- 5. A hardware keyboard moves the ring to the key it types ----

        [Fact]
        public Task AHardwareKey_MovesTheRingToItsKey_OnNamingAndSearch_AndOffSearchsHits() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3);
            var shell = await CreateAsync(library);
            FocusHome(shell, 15);
            Press(shell, GamepadAction.Confirm);
            var naming = Assert.IsType<HandheldNamingViewModel>(shell.Naming);
            var typing = Assert.IsType<HandheldTypingViewModel>(shell.Typing);
            // The field takes what the keyboard types, as the bound TextBox does.
            typing.Text = "R";
            Assert.Equal("R", naming.FocusedKey.Value);
            typing.Text = "Ra";
            Assert.Equal("A", naming.FocusedKey.Value);
            typing.Text = "Ra ";
            Assert.Equal(HandheldKeyKind.Space, naming.FocusedKey.Kind);
            typing.Text = "Ra";
            Assert.Equal(HandheldKeyKind.Delete, naming.FocusedKey.Kind);
            // A character not on this page leaves the ring where it is.
            typing.Text = "Ra7";
            Assert.Equal(HandheldKeyKind.Delete, naming.FocusedKey.Kind);
            Press(shell, GamepadAction.Menu);
            Assert.Null(shell.Naming);

            shell.SearchDebounce = TimeSpan.Zero;
            await shell.OpenSearchAsync();
            var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            search.Keyboard!.Text = "ps2 gam";
            await search.Pending;
            Press(shell, GamepadAction.NavigateUp);
            Assert.True(search.IsOnHits);
            shell.Typing!.Text = "ps2 game";
            await search.Pending;
            // The same games still match; the pad leaves the hits for the key typed (the mock's kbJump).
            Assert.NotEmpty(search.Hits);
            Assert.False(search.IsOnHits);
            Assert.Equal("E", search.Keyboard.FocusedKey.Value);
            Assert.True(search.Keyboard.FocusedKey.IsFocused);
            shell.Dispose();
        });
    }
}
