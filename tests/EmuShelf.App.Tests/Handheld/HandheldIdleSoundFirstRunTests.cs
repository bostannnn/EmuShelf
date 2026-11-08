using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Systems;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H6d: the idle drift, the interface sounds, the empty library and the setup wizard in Handheld mode.
public partial class HandheldShellViewModelTests
{
    private sealed partial class FakeLibrary
    {
        /// <summary>Builds the setup wizard the shell opens (Settings in setup mode); null leaves it closed.</summary>
        public Func<GamepadSettingsViewModel?>? SetupFactory { get; set; }

        public Task<GamepadSettingsViewModel?> CreateSetupAsync() => Task.FromResult(SetupFactory?.Invoke());

        /// <summary>Marks games as played, newest first, so the Recent folder and the idle drift have them.</summary>
        public FakeLibrary Played(string systemId, params int[] indexes)
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < indexes.Length; i++)
            {
                var game = Games[systemId][indexes[i]];
                Games[systemId][indexes[i]] = game with { LastPlayedAt = now.AddHours(-(i + 1)), PlayCount = 1 };
            }
            return this;
        }
    }

    private sealed class RecordingSounds : IHandheldSoundPlayer
    {
        public List<HandheldSound> Played { get; } = [];

        public void Play(HandheldSound sound) => Played.Add(sound);

        /// <summary>What has played since the last call.</summary>
        public HandheldSound[] Take()
        {
            var played = Played.ToArray();
            Played.Clear();
            return played;
        }
    }

    // ---- The idle drift ----

    [AvaloniaFact]
    public async Task IdleDrift_GoesOnceRoundTheRecentGamesAsTheirHero_ThenRestsOnTheNewest()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 4).Played("ps2", 2, 0);
        var shell = await CreateAsync(library);
        shell.StartStatusUpdates();
        var home = shell.Stage.Title;

        await shell.OnIdleAsync();

        Assert.True(shell.IsDrifting);
        Assert.Equal(HandheldStageKind.Hero, shell.Stage.Kind);
        Assert.True(shell.Stage.IsDrift);
        Assert.Equal("ps2 game 002", shell.Stage.Title);
        // No fan art here: the cover on its tint, with the plate saying why it is on screen.
        Assert.StartsWith("Recently played", shell.Stage.Subtitle);
        Assert.Empty(shell.Hints);

        await shell.OnIdleAsync();
        Assert.Equal("ps2 game 000", shell.Stage.Title);
        await shell.OnIdleAsync();
        Assert.Equal("ps2 game 002", shell.Stage.Title);
        // Once round and back to the newest: it rests there.
        await shell.OnIdleAsync();
        Assert.Equal("ps2 game 002", shell.Stage.Title);
        Assert.True(shell.IsDrifting);
        Assert.Equal(3, HandheldShellViewModel.DriftSteps(2));
        Assert.Equal(1, HandheldShellViewModel.DriftSteps(1));

        shell.Dispose();
        Assert.Equal("PS2 System", home);
    }

    [AvaloniaFact]
    public async Task IdleDrift_TheFirstPressOnlyWakesIt_AndTheNextOneActs()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3).Played("ps2", 1);
        var shell = await CreateAsync(library);
        shell.StartStatusUpdates();
        await shell.OnIdleAsync();
        Assert.True(shell.IsDrifting);
        var released = library.Released;

        // A wakes the Stage and does nothing else: no folder opens.
        Assert.True(shell.DispatchGamepadAction(GamepadAction.Confirm));
        Assert.False(shell.IsDrifting);
        Assert.Null(shell.OpenFolder);
        Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
        Assert.Equal("PS2 System", shell.Stage.Title);
        Assert.False(shell.Stage.IsDrift);
        Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Open" });
        // The drift's games are let go of.
        Assert.True(library.Released > released);

        Press(shell, GamepadAction.Confirm);
        Assert.NotNull(shell.OpenFolder);
        shell.Dispose();
    }

    [AvaloniaFact]
    public async Task IdleDrift_ATouchOnEitherScreenWakesIt_AndIsLetGo()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2).Played("ps2", 0));
        shell.StartStatusUpdates();
        Assert.False(shell.WakeOnTouch());

        await shell.OnIdleAsync();
        Assert.True(shell.IsDrifting);
        Assert.True(shell.WakeOnTouch());
        Assert.False(shell.IsDrifting);
        Assert.False(shell.WakeOnTouch());
        shell.Dispose();
    }

    [AvaloniaFact]
    public async Task IdleDrift_WaitsWhileSomethingIsOpen_OrNothingWasPlayed()
    {
        var unplayed = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
        unplayed.StartStatusUpdates();
        await unplayed.OnIdleAsync();
        Assert.False(unplayed.IsDrifting);
        unplayed.Dispose();

        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2).Played("ps2", 0));
        shell.StartStatusUpdates();
        Press(shell, GamepadAction.Actions); // the folder's sheet
        Assert.NotNull(shell.Sheet);
        await shell.OnIdleAsync();
        Assert.False(shell.IsDrifting);

        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Menu);
        await shell.OnIdleAsync();
        Assert.False(shell.IsDrifting);
        Press(shell, GamepadAction.Cancel);

        // Off screen (the status updates stopped): it never starts.
        shell.StopStatusUpdates();
        await shell.OnIdleAsync();
        Assert.False(shell.IsDrifting);
        shell.Dispose();
    }

    // ---- Sounds ----

    [AvaloniaFact]
    public async Task Sounds_FollowWhatEachPressDid_AndTheSwitchSilencesThem()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 2);
        var shell = await CreateAsync(library);
        var sounds = new RecordingSounds();
        var saved = new List<bool>();
        shell.SoundPlayer = sounds;
        shell.SaveSounds = saved.Add;

        // Into the wall at the first slot: nothing.
        Press(shell, GamepadAction.NavigateLeft);
        Assert.Empty(sounds.Take());
        Press(shell, GamepadAction.NavigateRight);
        Assert.Equal([HandheldSound.Move], sounds.Take());
        // A folder opens, and closes.
        Press(shell, GamepadAction.Confirm);
        Assert.Equal([HandheldSound.Open], sounds.Take());
        Press(shell, GamepadAction.Cancel);
        Assert.Equal([HandheldSound.Close], sounds.Take());
        // A sheet opens; B backs out of it.
        Press(shell, GamepadAction.Actions);
        Assert.Equal([HandheldSound.Open], sounds.Take());
        Press(shell, GamepadAction.Cancel);
        Assert.Equal([HandheldSound.Back], sounds.Take());
        // B with nothing to back out of: nothing.
        Press(shell, GamepadAction.Cancel);
        Assert.Empty(sounds.Take());

        // The Start menu's Sounds row shows the switch and turns it off.
        Press(shell, GamepadAction.Menu);
        Assert.Equal([HandheldSound.Open], sounds.Take());
        var row = shell.MenuItems.Single(item => item.Label == "Sounds");
        Assert.Equal("On", row.Note);
        Press(shell, GamepadAction.NavigateDown, shell.MenuItems.IndexOf(row));
        Assert.All(sounds.Take(), sound => Assert.Equal(HandheldSound.Move, sound));
        Press(shell, GamepadAction.Confirm);
        Assert.False(shell.SoundsEnabled);
        Assert.Equal("Off", row.Note);
        Assert.Equal([false], saved);
        Assert.Empty(sounds.Take());
        Press(shell, GamepadAction.NavigateRight);
        Assert.Empty(sounds.Take());

        // And on again: the press that turned it on is heard.
        Press(shell, GamepadAction.Menu);
        Press(shell, GamepadAction.NavigateDown, shell.MenuItems.IndexOf(row));
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.SoundsEnabled);
        Assert.Equal([HandheldSound.Confirm], sounds.Take());
        shell.Dispose();
    }

    [Fact]
    public void SoundFor_ATouchSoundsLikeTheButtonItStoodFor()
    {
        var sheet = new object();
        var before = new HandheldShellViewModel.SoundState(null, null, sheet, false, null, null, null, false, false, "a", "x");
        var closed = before with { Sheet = null };
        // A tap on a sheet's scrim is B; a tap on its row is A.
        Assert.Equal(HandheldSound.Back, HandheldShellViewModel.SoundFor(before, closed, GamepadAction.Cancel, bumped: false));
        Assert.Equal(HandheldSound.Confirm, HandheldShellViewModel.SoundFor(before, closed, null, bumped: false));
        // A tap that only moves the selector, and one that changed a value.
        var home = before with { Sheet = null };
        Assert.Equal(HandheldSound.Move, HandheldShellViewModel.SoundFor(home, home with { Selection = "b" }, null, bumped: false));
        Assert.Equal(HandheldSound.Confirm, HandheldShellViewModel.SoundFor(home, home with { Content = "y" }, null, bumped: false));
        Assert.Null(HandheldShellViewModel.SoundFor(home, home, null, bumped: false));
        // A held direction into a wall is silent even if something else changed.
        Assert.Null(HandheldShellViewModel.SoundFor(home, home with { Selection = "b" }, GamepadAction.NavigateLeft, bumped: true));
    }

    // ---- The empty library ----

    [AvaloniaFact]
    public async Task AnEmptyLibrary_StartsOnTheSlotThatAddsGames_AndAOnAnEmptySlotAddsGames()
    {
        var library = new FakeLibrary();
        var shell = new HandheldShellViewModel(library);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.True(shell.IsLibraryEmpty);
        var page = shell.CurrentPage!;
        // Settings alone, then the first empty slot, which says where games come from; the selector starts there.
        Assert.True(page.Slots[0].IsSettings);
        Assert.True(page.Slots[1].IsAddGames);
        Assert.Equal(1, page.Slots.Count(slot => slot.IsAddGames));
        Assert.Equal(1, shell.HomePosition.Slot);
        Assert.Equal(HandheldStageKind.EmptySlot, shell.Stage.Kind);
        Assert.Equal("Add games", shell.Stage.Title);
        Assert.Equal(HandheldGlyph.Add, shell.Stage.Glyph);
        Assert.Contains(shell.Hints, hint => hint is { Button: "A", Label: "Add games" });

        // A adds games (the folder picker), not a folder: there is nothing to put in one. Any empty slot does.
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();
        Assert.Equal(1, library.FolderPicks);
        Assert.Null(shell.Naming);
        Press(shell, GamepadAction.NavigateRight, 2);
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();
        Assert.Equal(2, library.FolderPicks);

        // Games arrive: the slot is an ordinary empty slot again and makes folders.
        library.WithPlatform("ps2", 2);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(shell.IsLibraryEmpty);
        Assert.DoesNotContain(shell.CurrentPage!.Slots, slot => slot.IsAddGames);
        shell.Dispose();
    }

    // ---- First run: the setup wizard across the two screens ----

    private static GamepadSettingsViewModel CreateSetupProjection()
    {
        var settings = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(system => system.Id, _ => (EmulatorConfiguration?)null, StringComparer.Ordinal),
            new NoConfigurations(),
            new FakeDialogService(),
            fixedEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
        return new GamepadSettingsViewModel(settings, androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem,
            setup: new SetupWizardOptions(
                HasSecondScreen: true,
                IsSecondScreenReturnReady: () => true,
                RequestSecondScreenReturn: () => { },
                DataFolderStatus: "User/EmuShelf"));
    }

    [AvaloniaFact]
    public async Task TheSetupWizard_IsSettingsInSetupMode_WalkedAcrossBothScreens_AndFinishCompletesIt()
    {
        var library = new FakeLibrary();
        GamepadSettingsViewModel? wizard = null;
        library.SetupFactory = () => wizard = CreateSetupProjection();
        var shell = new HandheldShellViewModel(library);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        await shell.OpenSetupAsync();

        var settings = Assert.IsType<HandheldSettingsViewModel>(shell.Settings);
        Assert.True(settings.IsSetup);
        Assert.True(shell.IsLauncherOverlayOpen);
        Assert.Equal("Setup", settings.Heading);
        // The Launcher lists the wizard's steps where Settings lists sections; it opens on the first one left to answer.
        Assert.Equal(["Storage access", "Data folder", "Second screen", "Games & emulators"],
            settings.Sections.Select(section => section.Title));
        Assert.True(settings.Sections[2].IsSelected);
        // The Stage shows where the wizard is and what the focused row is.
        Assert.Equal(HandheldStageKind.Setting, shell.Stage.Kind);
        Assert.StartsWith("Step 3 of 4", shell.Stage.SettingEyebrow);
        Assert.Contains(shell.Hints, hint => hint is { Button: "B", Label: "Back" });
        Assert.Contains(shell.Hints, hint => hint.Label == "Continue" && hint.Icon == HandheldGlyph.Menu);

        // Start walks on; the last step finishes.
        Press(shell, GamepadAction.Menu);
        Assert.True(settings.Sections[3].IsSelected);
        Assert.Equal("Finish", settings.StartLabel);
        Assert.Contains(shell.Hints, hint => hint.Label == "Finish");
        // B goes back a step, and a tap on a step shows it.
        Press(shell, GamepadAction.Cancel);
        Assert.True(settings.Sections[2].IsSelected);
        settings.TapSection(settings.Sections[3]);
        Assert.True(settings.Sections[3].IsSelected);

        settings.TapStart();
        for (var i = 0; i < 20 && shell.Settings is not null; i++)
            await Task.Delay(10);
        Assert.Null(shell.Settings);
        Assert.Equal([true], library.SettingsClosed);
        Assert.True(wizard!.SetupCompleted);
        shell.Dispose();
    }

    [AvaloniaFact]
    public async Task TheSetupWizard_DrawsOnBothScreensCanvases()
    {
        foreach (var wideLauncher in new[] { false, true })
        {
            var library = new FakeLibrary();
            library.SetupFactory = CreateSetupProjection;
            var shell = new HandheldShellViewModel(library);
            await shell.InitializeAsync(TestContext.Current.CancellationToken);
            var launcher = new HandheldLauncherView(null, pinnedWide: wideLauncher) { DataContext = shell };
            var stage = new HandheldStageView(null, pinnedNarrow: wideLauncher) { DataContext = shell };
            var (launcherWidth, stageWidth) = wideLauncher ? (1920, 1240) : (1240, 1920);
            var launcherWindow = new Window { Content = launcher, Width = launcherWidth, Height = 1080 };
            var stageWindow = new Window { Content = stage, Width = stageWidth, Height = 1080 };
            launcherWindow.Show();
            stageWindow.Show();
            var name = wideLauncher ? "wide" : "narrow";
            try
            {
                // The empty library's home first: Settings, and the slot that adds games.
                await SettleAsync();
                Assert.True(launcher.FindControl<Border>("SettingsLayer")!.IsVisible is false);
                Dump(launcherWindow, $"h6d-{name}-0-empty-launcher");
                Dump(stageWindow, $"h6d-{name}-0-empty-stage");

                await shell.OpenSetupAsync();
                await SettleAsync();
                Assert.True(launcher.FindControl<Border>("SettingsStart")!.IsVisible);
                Dump(launcherWindow, $"h6d-{name}-1-setup-launcher");
                Dump(stageWindow, $"h6d-{name}-1-setup-stage");

                Press(shell, GamepadAction.Menu);
                await SettleAsync();
                Dump(launcherWindow, $"h6d-{name}-2-games-launcher");
                Dump(stageWindow, $"h6d-{name}-2-games-stage");
            }
            finally
            {
                launcherWindow.Close();
                stageWindow.Close();
                shell.Dispose();
            }
        }
    }

    [AvaloniaFact]
    public async Task TheDriftAndADockApp_DrawOnTheStage()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3).Played("ps2", 1, 2);
        library.CoverMaker = MakeCover;
        // The first recent game has fan art (and no logo, so its title is set in type); the second has none.
        library.ArtMaker = (game, kind) => kind == HandheldArtKind.Fanart && game.Id % 1000 == 1 ? MakeCover(game) : null;
        var shell = await CreateAsync(library);
        var companion = new SecondScreenViewModel();
        companion.Dock[0].Label = "Music";
        companion.Dock[0].AccentColor = Color.Parse("#D9443B");
        shell.Companion = companion;
        shell.StartStatusUpdates();
        var stage = new HandheldStageView(null, pinnedNarrow: false) { DataContext = shell };
        var stageWindow = new Window { Content = stage, Width = 1920, Height = 1080 };
        stageWindow.Show();
        try
        {
            await shell.OnIdleAsync();
            await SettleAsync();
            Assert.True(shell.Stage.IsHeroArt);
            Assert.True(stage.FindControl<Control>("HeroContent")!.IsVisible);
            Dump(stageWindow, "h6d-3-drift-fanart-stage");

            await shell.OnIdleAsync();
            await SettleAsync();
            Assert.True(shell.Stage.IsHeroFallback);
            Dump(stageWindow, "h6d-4-drift-cover-stage");

            // Woken, then down to the dock's first app: its icon on the sweep in its colour.
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.NavigateDown, 4);
            Press(shell, GamepadAction.NavigateRight);
            await SettleAsync();
            Assert.Equal(HandheldStageKind.App, shell.Stage.Kind);
            Assert.Equal("Music", shell.Stage.Title);
            Assert.Equal(HandheldShellViewModel.AppSweep(Color.Parse("#D9443B")), shell.Stage.Backdrop);
            Dump(stageWindow, "h6d-5-dock-app-stage");

            // The colour arriving later reaches a focused slot.
            companion.Dock[0].AccentColor = Color.Parse("#3B7DD9");
            Assert.Equal(HandheldShellViewModel.AppSweep(Color.Parse("#3B7DD9")), shell.Stage.Backdrop);
        }
        finally
        {
            stageWindow.Close();
            shell.Dispose();
        }
    }
}

public sealed class HandheldSoundAndColourTests
{
    [Theory]
    [InlineData(HandheldSound.Move)]
    [InlineData(HandheldSound.Open)]
    [InlineData(HandheldSound.Close)]
    [InlineData(HandheldSound.Confirm)]
    [InlineData(HandheldSound.Back)]
    public void EverySound_IsShortSoftAndEndsInSilence(HandheldSound sound)
    {
        var samples = HandheldSoundSynth.Render(sound);
        var seconds = samples.Length / (double)HandheldSoundSynth.SampleRate;
        Assert.InRange(seconds, 0.04, 0.25);
        var peak = samples.Max(sample => Math.Abs((int)sample)) / (double)short.MaxValue;
        Assert.InRange(peak, 0.1, 0.5);
        // The last few milliseconds are silent, so a replay never clicks.
        Assert.All(samples.TakeLast(HandheldSoundSynth.SampleRate / 500), sample => Assert.InRange((int)sample, -40, 40));
    }

    [Fact]
    public void TheFiveSounds_AreDifferent()
    {
        var rendered = Enum.GetValues<HandheldSound>().Select(sound => string.Join(',', HandheldSoundSynth.Render(sound).Take(4000))).ToList();
        Assert.Equal(rendered.Count, rendered.Distinct().Count());
    }

    private static uint[] Icon(Func<int, int, uint> pixel, int size = 32)
    {
        var pixels = new uint[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
                pixels[(y * size) + x] = pixel(x, y);
        }
        return pixels;
    }

    private static double Hue(uint argb) => Color.FromUInt32(argb).ToHsl().H;

    [Fact]
    public void IconColour_IgnoresWhiteBlackAndGrey_AndTheStrongestHueWins()
    {
        // A white icon with a red mark: red, not white.
        var red = HandheldIconColor.Pick(Icon((x, y) => x is > 10 and < 20 && y is > 10 and < 20 ? 0xFFE0302Au : 0xFFFFFFFFu));
        Assert.InRange(Hue(red), 0, 10);
        // Mostly blue with a little orange: blue.
        var blue = HandheldIconColor.Pick(Icon((x, _) => x < 26 ? 0xFF2F6BE0u : 0xFFF09A2Au));
        Assert.InRange(Hue(blue), 210, 230);
        // Greys and black only, or nothing opaque at all: the slate.
        Assert.Equal(HandheldIconColor.Slate, HandheldIconColor.Pick(Icon((x, _) => x < 16 ? 0xFF808080u : 0xFF101010u)));
        Assert.Equal(HandheldIconColor.Slate, HandheldIconColor.Pick(Icon((_, _) => 0x00FF0000u)));
        // A very light colour is kept light enough for a sweep behind a white card.
        var pale = Color.FromUInt32(HandheldIconColor.Pick(Icon((_, _) => 0xFFFFC8D2u)));
        Assert.InRange(pale.ToHsl().L, 0.34, 0.63);
    }

    [Fact]
    public void AndroidStart_AFreshInstallOnADualScreenHandheldStartsInHandheldMode()
    {
        var fresh = new AppSettings();
        Assert.True(AndroidStartMode.IsFreshInstall(fresh));
        Assert.Equal(InterfaceMode.Handheld, AndroidStartMode.Resolve(fresh, hasPresentationDisplay: true));
        Assert.Equal(InterfaceMode.Gamepad, AndroidStartMode.Resolve(fresh, hasPresentationDisplay: false));
        // An install that walked the wizard in Gamepad mode (the mode was never switched) stays in Gamepad mode.
        var walked = fresh with { SetupCompletedVersion = 1 };
        Assert.Equal(InterfaceMode.Gamepad, AndroidStartMode.Resolve(walked, hasPresentationDisplay: true));
        Assert.Equal(InterfaceMode.Gamepad, AndroidStartMode.Resolve(fresh with { InterfaceMode = InterfaceMode.Gamepad }, true));
        // A saved Handheld choice needs the second screen.
        var handheld = walked with { InterfaceMode = InterfaceMode.Handheld };
        Assert.Equal(InterfaceMode.Handheld, AndroidStartMode.Resolve(handheld, true));
        Assert.Equal(InterfaceMode.Gamepad, AndroidStartMode.Resolve(handheld, false));
        // The sounds are on unless switched off.
        Assert.True(fresh.HandheldSounds);
    }
}
