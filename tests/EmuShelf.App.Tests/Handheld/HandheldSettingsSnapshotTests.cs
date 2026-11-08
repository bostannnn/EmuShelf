using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H6a: the real Launcher and Stage hosted headlessly with Settings, on both screens' canvases (the 1240-wide one and
// the 16:9 one, M45 H5), since both Thor displays are FLAG_SECURE. Set EMUSHELF_SNAPSHOT_DIR to get the frames as PNGs.
public partial class HandheldShellViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsAndItsTile_DrawOnBothScreensCanvases(bool wideLauncher)
    {
        var library = new FakeLibrary().WithPlatform("ps2", 4).WithPlatform("gc", 2);
        library.SettingsFactory = CreateSettingsProjection;
        var shell = new HandheldShellViewModel(library);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);
        // The Launcher on the 16:9 screen goes with the Stage on the narrow one (the screens swapped), and the other way round.
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
            // The tile on home (Recent, ps2, gc, then Settings), and its card on the Stage.
            FocusHome(shell, 3);
            await SettleAsync();
            Assert.True(shell.CurrentPage!.Slots[3].IsSettings);
            Dump(launcherWindow, $"h6a-{name}-0-tile-launcher");
            Dump(stageWindow, $"h6a-{name}-0-tile-stage");

            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Assert.True(launcher.FindControl<Border>("SettingsLayer")!.IsVisible);
            Assert.Equal(launcherWidth, launcher.FindControl<Grid>("SettingsCanvas")!.Width);
            Dump(launcherWindow, $"h6a-{name}-1-settings-launcher");
            Dump(stageWindow, $"h6a-{name}-1-settings-stage");

            // Emulators, a platform opened: summaries, a choice row, the platform's own rows.
            Press(shell, GamepadAction.NextPlatform);
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateDown);
            await SettleAsync();
            Dump(launcherWindow, $"h6a-{name}-2-emulators-launcher");
            Dump(stageWindow, $"h6a-{name}-2-emulators-stage");

            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Assert.True(launcher.FindControl<Border>("SettingsChoiceSheet")!.IsVisible);
            Dump(launcherWindow, $"h6a-{name}-3-choice-launcher");
            Dump(stageWindow, $"h6a-{name}-3-choice-stage");
            Press(shell, GamepadAction.Cancel);

            // Achievements: the account's text row on the keyboard.
            Press(shell, GamepadAction.NextPlatform);
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            while (shell.Settings!.Projection.FocusedRow!.Kind != GamepadSettingsRowKind.Text)
                Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateRight, 2);
            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Assert.True(launcher.FindControl<Grid>("SettingsKeyboard")!.IsVisible);
            Assert.False(launcher.FindControl<Grid>("SettingsBody")!.IsVisible);
            Dump(launcherWindow, $"h6a-{name}-4-keyboard-launcher");
            Dump(stageWindow, $"h6a-{name}-4-keyboard-stage");
            Press(shell, GamepadAction.Search);

            // Left to the sections; the Stage describes the section.
            Press(shell, GamepadAction.NavigateUp, 3);
            Press(shell, GamepadAction.NavigateLeft);
            await SettleAsync();
            Assert.True(shell.Settings.Projection.IsRailFocused);
            Dump(launcherWindow, $"h6a-{name}-5-sections-launcher");
            Dump(stageWindow, $"h6a-{name}-5-sections-stage");

            Press(shell, GamepadAction.Cancel);
            await SettleAsync();
            Assert.False(launcher.FindControl<Border>("SettingsLayer")!.IsVisible);
        }
        finally
        {
            launcherWindow.Close();
            stageWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheSettingsTile_InYourFolder_DrawsItsFaceFirst()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 4);
        var store = new InMemoryHandheldHomeLayoutStore();
        var games = library.Games["ps2"].Select(game => game.Id).Take(3).ToList();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Recent, HandheldSlot.UserFolder("mine")],
            [new HandheldUserFolder("mine", "Weekend", "#3BBD79", games, HoldsSettings: true)]));
        var shell = await CreateAsync(library, store);
        var view = new HandheldLauncherView { DataContext = shell };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            FocusHome(shell, 2);
            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Assert.True(shell.OpenFolder!.Items[0].IsSettings);
            Dump(window, "h6a-folder-with-settings");
        }
        finally
        {
            window.Close();
        }
    }
}
