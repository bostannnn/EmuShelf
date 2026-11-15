using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>M47 B11's smaller polish (part 3), under Big Type only; today's look keeps what it did.</summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypePolish
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

        // ---- The idle drift: a direction wakes it and moves ----

        [AvaloniaFact]
        public Task IdleDrift_ADirectionWakesItAndMoves_AnyOtherButtonOnlyWakes() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 3).Played("ps2", 1);
            var shell = await CreateAsync(library);
            shell.StartStatusUpdates();
            var start = shell.Stage.Title;

            await shell.OnIdleAsync();
            Assert.True(shell.IsDrifting);
            Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateRight));
            shell.SettleStage();
            Assert.False(shell.IsDrifting);
            Assert.Equal("GC System", shell.Stage.Title);

            // A only wakes: nothing opens, nothing starts.
            await shell.OnIdleAsync();
            Assert.True(shell.IsDrifting);
            Assert.True(shell.DispatchGamepadAction(GamepadAction.Confirm));
            Assert.False(shell.IsDrifting);
            Assert.Null(shell.OpenFolder);
            Assert.Equal("GC System", shell.Stage.Title);
            Assert.Equal("PS2 System", start);
            shell.Dispose();
        });

        // ---- No backdrop yet: never the plain grey ----

        [AvaloniaFact]
        public Task NoBackdropYet_TheNativeStageIsNotSentThePlainGrey_TodaysLookStillIs() => BigType(async () =>
        {
            var bigType = await BackdropsSentWithNone();
            Assert.DoesNotContain(bigType, command => command.Animated is null);
            HandheldTokens.Use(HandheldLook.Classic);
            var classic = await BackdropsSentWithNone();
            Assert.Contains(classic, command => command.Animated is null);
        });

        // The backdrop commands the native Stage gets when the Stage has no backdrop of its own (as at a start, before the first focus).
        private static async Task<System.Collections.Generic.List<EmuShelf.Rendering.Stage.StageBackdropCommand>> BackdropsSentWithNone()
        {
            var screen = new RecordingNativeScreen();
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
            var view = new EmuShelf.App.Views.HandheldStageView(screen, pinnedNarrow: null) { DataContext = shell };
            var window = new Avalonia.Controls.Window { Content = view, Width = 1920, Height = 1080 };
            window.Show();
            try
            {
                shell.Stage.Backdrop = null;
                for (var i = 0; i < 4; i++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20);
                }
                screen.Posted.Clear();
                shell.Stage.Backdrop = null;
                // Hidden and shown again (a swap, the screen coming back): everything is sent again.
                view.IsVisible = false;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                view.IsVisible = true;
                for (var i = 0; i < 4; i++)
                {
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    await Task.Delay(20);
                }
                return screen.Posted.OfType<EmuShelf.Rendering.Stage.StageBackdropCommand>().ToList();
            }
            finally
            {
                window.Close();
                shell.Dispose();
            }
        }

        [AvaloniaFact]
        public async Task IdleDrift_TodaysLook_ADirectionOnlyWakesIt()
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 3).Played("ps2", 1);
            var shell = await CreateAsync(library);
            shell.StartStatusUpdates();
            await shell.OnIdleAsync();
            Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateRight));
            shell.SettleStage();
            Assert.False(shell.IsDrifting);
            Assert.Equal("PS2 System", shell.Stage.Title);
            shell.Dispose();
        }
    }
}
