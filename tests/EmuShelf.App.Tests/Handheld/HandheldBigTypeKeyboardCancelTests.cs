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
    /// <summary>M47 B14 leftover 4 under Big Type: with nothing typed the keyboard's Delete reads Cancel and leaves, so a keyboard can be left by touch alone; Classic's Delete is unchanged.</summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeKeyboardCancel
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

        // ---- 4. The keyboard by touch alone: Delete reads Cancel with nothing typed, and leaves ----

        private static HandheldKeyViewModel KeyOf(System.Collections.Generic.IEnumerable<HandheldKeyRowViewModel> rows, HandheldKeyKind kind) =>
            rows.SelectMany(row => row.Keys).Single(key => key.Kind == kind);

        [Fact]
        public Task Naming_DeleteReadsCancel_WithNothingTyped_AndATapLeaves() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            FocusHome(shell, 15);
            Press(shell, GamepadAction.Confirm);
            var naming = Assert.IsType<HandheldNamingViewModel>(shell.Naming);
            var delete = KeyOf(naming.KeyRows, HandheldKeyKind.Delete);
            Assert.Equal("Cancel", delete.Label);
            shell.TapTypingKey(naming.KeyRows.SelectMany(row => row.Keys).First(key => key.Value == "R"));
            Assert.Equal("Delete", delete.Label);
            shell.TapTypingKey(delete);
            Assert.Equal(string.Empty, naming.Text);
            Assert.Equal("Cancel", delete.Label);
            Assert.Same(naming, shell.Naming);
            // The next tap leaves without making a folder, as B does.
            shell.TapTypingKey(delete);
            Assert.Null(shell.Naming);
            Assert.True(shell.CurrentPage!.Slots[15].IsEmpty);
            shell.Dispose();
        });

        [Fact]
        public Task Search_DeleteReadsCancel_WithNothingTyped_AndATapCloses() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            shell.SearchDebounce = TimeSpan.Zero;
            await shell.OpenSearchAsync();
            var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            var delete = KeyOf(search.Keyboard!.KeyRows, HandheldKeyKind.Delete);
            Assert.Equal("Cancel", delete.Label);
            search.Keyboard.Text = "ps";
            Assert.Equal("Delete", delete.Label);
            shell.TapTypingKey(delete);
            shell.TapTypingKey(delete);
            Assert.Same(search, shell.Panel);
            Assert.Equal("Cancel", delete.Label);
            shell.TapTypingKey(delete);
            Assert.Null(shell.Panel);
            shell.Dispose();
        });

        [Fact]
        public async Task Classic_DeleteStaysDelete_AndDoesNotLeave()
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            FocusHome(shell, 15);
            Press(shell, GamepadAction.Confirm);
            var naming = Assert.IsType<HandheldNamingViewModel>(shell.Naming);
            var delete = KeyOf(naming.KeyRows, HandheldKeyKind.Delete);
            Assert.Equal("Delete", delete.Label);
            shell.PressNamingKey(delete);
            Assert.Same(naming, shell.Naming);
            Assert.Equal("Delete", delete.Label);
            shell.Dispose();
        }
    }
}
