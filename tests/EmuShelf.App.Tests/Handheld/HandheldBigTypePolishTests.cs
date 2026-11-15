using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
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

        // ---- Sounds: the mock's sfx calls ----

        [Fact]
        public void Sounds_FollowTheMock_UnderBigTypeOnly() => BigType(() =>
        {
            var home = new HandheldShellViewModel.SoundState(null, null, null, false, null, null, null, false, false, "a", "x");
            // A wall bumps (today: silent).
            Assert.Equal(HandheldSound.Bump, HandheldShellViewModel.SoundFor(home, home, GamepadAction.NavigateLeft, bumped: true));
            // A letter typed sounds move, Delete back (today: confirm both).
            var typing = home with { Typed = 3 };
            Assert.Equal(HandheldSound.Move, HandheldShellViewModel.SoundFor(typing, typing with { Typed = 4, Content = "y" }, GamepadAction.Confirm, false));
            Assert.Equal(HandheldSound.Back, HandheldShellViewModel.SoundFor(typing, typing with { Typed = 2, Content = "y" }, GamepadAction.Confirm, false));
            // A on a game opens its page to play: the launch's own sound plays, not open.
            var title = new object();
            Assert.Null(HandheldShellViewModel.SoundFor(home, home with { Title = title, TitlePlays = true }, GamepadAction.Confirm, false));
            Assert.Equal(HandheldSound.Open, HandheldShellViewModel.SoundFor(home, home with { Title = title }, GamepadAction.Confirm, false));

            HandheldTokens.Use(HandheldLook.Classic);
            Assert.Null(HandheldShellViewModel.SoundFor(home, home, GamepadAction.NavigateLeft, bumped: true));
            Assert.Equal(HandheldSound.Confirm, HandheldShellViewModel.SoundFor(typing, typing with { Typed = 4, Content = "y" }, GamepadAction.Confirm, false));
            Assert.Equal(HandheldSound.Open, HandheldShellViewModel.SoundFor(home, home with { Title = title, TitlePlays = true }, GamepadAction.Confirm, false));
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

        [Fact]
        public void TheTwoNewSounds_AreTheMocksOwn()
        {
            // bump: blip(180, .05, 'square', .018); launch: four triangles .13 s long, .07 s apart.
            var bump = HandheldSoundSynth.Render(HandheldSound.Bump);
            var launch = HandheldSoundSynth.Render(HandheldSound.Launch);
            Assert.Equal((int)Math.Ceiling(0.06 * HandheldSoundSynth.SampleRate), bump.Length);
            Assert.Equal((int)Math.Ceiling((0.21 + 0.13 + 0.01) * HandheldSoundSynth.SampleRate), launch.Length);
            Assert.True(bump.Max(sample => Math.Abs((int)sample)) < launch.Max(sample => Math.Abs((int)sample)));
            // Today's five are unchanged in length.
            Assert.Equal((int)Math.Ceiling(0.055 * HandheldSoundSynth.SampleRate), HandheldSoundSynth.Render(HandheldSound.Move).Length);
        }

        [AvaloniaFact]
        public Task AMessageOnTheLine_SoundsOpen_AnErrorBump_AndThePressItCameWithStaysQuiet() => BigType(async () =>
        {
            var sounds = new RecordingSounds();
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
            shell.SoundPlayer = sounds;
            shell.ShowStatus("Found 2 new games");
            Assert.Equal([HandheldSound.Open], sounds.Take());
            shell.ShowStatus("Could not start NetherSX2", StatusSeverity.Error);
            Assert.Equal([HandheldSound.Bump], sounds.Take());
            // Work still running is silent.
            shell.ShowStatus("Scanning · 1 of 9 consoles", StatusSeverity.Progress);
            Assert.Empty(sounds.Take());
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
