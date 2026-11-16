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
    /// <summary>M47 B14 leftover 6 under Big Type: after a game played from Search (or a launch that failed) Search comes back on what was typed, the hit played focused; a missing file keeps Search open.</summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeSearchReturn
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

        // ---- 6. After a game from Search, back to Search on the same hit ----

        private static async Task WaitFor(Func<bool> done)
        {
            for (var i = 0; i < 400 && !done(); i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(5);
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        private static async Task<(FakeLibrary Library, HandheldShellViewModel Shell, HandheldSearchPanel Search, HandheldGameEntry Game)> OnASearchHitAsync()
        {
            var library = new FakeLibrary().WithPlatform("gc", 4);
            var game = library.Games["gc"][2];
            library.Details[game.Id] = Details();
            var shell = await CreateAsync(library);
            shell.SearchDebounce = TimeSpan.Zero;
            await shell.OpenSearchAsync();
            var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            search.Keyboard!.Text = "gc game";
            await search.Pending;
            Press(shell, GamepadAction.NavigateUp);
            Press(shell, GamepadAction.NavigateRight, 2);
            Assert.Equal(game.Id, search.FocusedHit!.Entry.Id);
            return (library, shell, search, game);
        }

        [AvaloniaFact]
        public Task AfterAGameFromSearch_SearchComesBack_OnWhatWasTyped_WithTheHitFocused() => BigType(async () =>
        {
            var (library, shell, search, game) = await OnASearchHitAsync();
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => shell.Playing is { IsRunning: true });
            Assert.Null(shell.Panel);
            Assert.Equal([game.Id], library.Launched);

            await shell.EndPlayAsync();
            await WaitFor(() => shell.Panel is HandheldSearchPanel { FocusedHit: not null });
            var back = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            Assert.NotSame(search, back);
            Assert.Equal("gc game", back.Query);
            Assert.True(back.IsOnHits);
            Assert.Equal(game.Id, back.FocusedHit!.Entry.Id);
            // The Stage is that game's again (the mock's focusRes), and B goes back to the keys.
            Assert.Equal(game.Title, shell.Stage.Name);
            Press(shell, GamepadAction.Cancel);
            Assert.False(back.IsOnHits);
            shell.Dispose();
        });

        [AvaloniaFact]
        public Task AFailedLaunchFromSearch_ComesBackToSearchToo() => BigType(async () =>
        {
            var (library, shell, _, game) = await OnASearchHitAsync();
            var launch = new TaskCompletionSource<bool>();
            library.PendingLaunch = launch;
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => shell.Playing is not null);
            launch.SetResult(false);
            await WaitFor(() => shell.Panel is HandheldSearchPanel { FocusedHit: not null });
            Assert.Equal(game.Id, Assert.IsType<HandheldSearchPanel>(shell.Panel).FocusedHit!.Entry.Id);
            shell.Dispose();
        });

        [AvaloniaFact]
        public Task AMissingFileFromSearch_KeepsSearchOpen_AndSaysSo() => BigType(async () =>
        {
            var (library, shell, search, game) = await OnASearchHitAsync();
            var at = library.Games["gc"].FindIndex(entry => entry.Id == game.Id);
            library.Games["gc"][at] = game with { IsAvailable = false };
            Press(shell, GamepadAction.Confirm);
            await WaitFor(() => shell.Stage.Line == HandheldShellViewModel.MissingFileText);
            Assert.Same(search, shell.Panel);
            Assert.Null(shell.Playing);
            Assert.Empty(library.Launched);
            shell.Dispose();
        });
    }
}
