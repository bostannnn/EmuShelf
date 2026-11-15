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
