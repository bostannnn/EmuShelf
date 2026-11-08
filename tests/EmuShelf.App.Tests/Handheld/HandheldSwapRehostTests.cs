using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Rendering.Stage;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H5: the swap as the Thor does it. The views move between two windows (detached in one pass, attached in a
// later one) while the native screens are fed pictures; what the native screens hold afterwards must be laid out
// for the new canvases, not pictured from a detached view.
public partial class HandheldShellViewModelTests
{
    /// <summary>A native screen that keeps the latest command per key, as NativeSceneSurface does.</summary>
    private sealed class RecordingNativeScreen : IHandheldNativeStage
    {
        public Dictionary<string, StageCommand> Retained { get; } = new(StringComparer.Ordinal);
        public List<StageCommand> Posted { get; } = [];
        public bool Active { get; private set; }

        public void SetActive(bool active) => Active = active;

        public void Post(StageCommand command)
        {
            Posted.Add(command);
            if (command.RetainKey is not { } key)
                return;
            if (command.Removes)
                Retained.Remove(key);
            else
                Retained[key] = command;
        }

        public event Action? Failed { add { } remove { } }

        public IEnumerable<LauncherSprite> Sprites(string prefix) => Retained.Values.OfType<LauncherSpriteCommand>()
            .Select(command => command.Sprite!).Where(sprite => sprite.Id.StartsWith(prefix, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Swap_ReHostedLikeTheThor_FeedsTheNativeScreensTheNewLayout()
    {
        var stageScreen = new RecordingNativeScreen();
        var launcherScreen = new RecordingNativeScreen();
        var (previousHost, previousLauncher) = (HandheldNativeStage.Host, HandheldNativeStage.Launcher);
        HandheldNativeStage.Host = stageScreen;
        HandheldNativeStage.Launcher = launcherScreen;
        Window? top = null, bottom = null;
        try
        {
            var (shell, _) = await CreateFullHomeAsync();
            var stage = new HandheldStageView { DataContext = shell };
            var launcher = new HandheldLauncherView { DataContext = shell };
            top = new Window { Content = stage, Width = 1920, Height = 1080 };
            bottom = new Window { Content = launcher, Width = 1240, Height = 1080 };
            top.Show();
            bottom.Show();
            await SettleAsync();
            Assert.Equal(1240f, Assert.IsType<LauncherLayoutCommand>(launcherScreen.Retained["layout"]).CanvasWidth);

            // HandheldScreenHost's order: make the change, detach both views, attach them on a later pass.
            shell.ScreenSwapHost = async apply =>
            {
                apply();
                top.Content = null;
                bottom.Content = null;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                top.Content = launcher;
                bottom.Content = stage;
            };
            Press(shell, GamepadAction.Search);
            await SettleAsync();
            Assert.True(shell.IsSwapped);

            // The Launcher's native screen: a 1920 canvas, home clipped to it, the first row's seventh tile far right.
            var layout = Assert.IsType<LauncherLayoutCommand>(launcherScreen.Retained["layout"]);
            Assert.Equal(1920f, layout.CanvasWidth);
            Assert.Equal(1920f, layout.HomeClip.Width, 1f);
            var background = Assert.Single(launcherScreen.Sprites("bg"));
            Assert.Equal(1920f, background.Image.Width);
            var seventh = Assert.Single(launcherScreen.Sprites("h:0:6"));
            Assert.InRange(seventh.PivotX, 1500f, 1800f);
            var firstOfSecondRow = Assert.Single(launcherScreen.Sprites("h:0:7"));
            Assert.InRange(firstOfSecondRow.PivotX, 100f, 400f);
            Assert.All(launcherScreen.Sprites("h:0:"), sprite => Assert.InRange(sprite.Image.Width, 150f, 400f));
            Assert.Empty(launcherScreen.Sprites("h:2:"));

            // The Stage's native screen: a 1240 canvas, the hints top-left, the plate centred on it.
            Assert.Equal(1240f, Assert.IsType<StageCanvasCommand>(stageScreen.Retained["canvas"]).Width);
            var hints = Assert.IsType<StageChromeCommand>(stageScreen.Retained["chrome:hints"]).Image!;
            Assert.InRange(hints.X, 0f, 100f);
            Assert.InRange(hints.Y, 0f, 100f);
            var status = Assert.IsType<StageChromeCommand>(stageScreen.Retained["chrome:status"]).Image!;
            Assert.InRange(status.X + status.Width, 1100f, 1240f);
            var centre = stageScreen.Posted.OfType<StageCentreCommand>().Last();
            var plate = centre.Plate!;
            Assert.Equal(620f, plate.X + (plate.Width / 2), 2f);

            // And back.
            Press(shell, GamepadAction.Search);
            await SettleAsync();
            Assert.Equal(1240f, Assert.IsType<LauncherLayoutCommand>(launcherScreen.Retained["layout"]).CanvasWidth);
            Assert.InRange(Assert.Single(launcherScreen.Sprites("h:0:4")).PivotX, 900f, 1240f);
            Assert.Equal(1920f, Assert.IsType<StageCanvasCommand>(stageScreen.Retained["canvas"]).Width);
            var platesBack = stageScreen.Posted.OfType<StageCentreCommand>().Last().Plate!;
            Assert.Equal(960f, platesBack.X + (platesBack.Width / 2), 2f);
        }
        finally
        {
            top?.Close();
            bottom?.Close();
            HandheldNativeStage.Host = previousHost;
            HandheldNativeStage.Launcher = previousLauncher;
        }
    }

    [AvaloniaFact]
    public async Task Swap_WithAPairOnEachDisplay_OnlyChangesWhichIsInFront()
    {
        var stageWide = new RecordingNativeScreen();
        var stageNarrow = new RecordingNativeScreen();
        var launcherWide = new RecordingNativeScreen();
        var launcherNarrow = new RecordingNativeScreen();
        Window? top = null, bottom = null;
        var (shell, _) = await CreateFullHomeAsync();
        var topStage = new HandheldStageView(stageWide, pinnedNarrow: false) { DataContext = shell };
        var topLauncher = new HandheldLauncherView(launcherWide, pinnedWide: true) { DataContext = shell };
        var bottomStage = new HandheldStageView(stageNarrow, pinnedNarrow: true) { DataContext = shell };
        var bottomLauncher = new HandheldLauncherView(launcherNarrow, pinnedWide: false) { DataContext = shell };
        top = new Window { Content = new Panel { Children = { topStage, topLauncher } }, Width = 1920, Height = 1080 };
        bottom = new Window { Content = new Panel { Children = { bottomStage, bottomLauncher } }, Width = 1240, Height = 1080 };
        try
        {
            top.Show();
            bottom.Show();
            await SettleAsync();
            // The pair for the other arrangement is built once, out of sight, then paused (HandheldScreenHost's warm-up).
            topLauncher.IsVisible = bottomStage.IsVisible = false;
            await SettleAsync();
            var narrowFirstPage = shell.NarrowHome.Pages[0];
            var wideFirstPage = shell.WideHome.Pages[0];
            var postsWhileHidden = (launcherWide.Posted.Count, stageNarrow.Posted.Count);

            // Browsing touches only the screens in front.
            Press(shell, GamepadAction.NavigateRight, 2);
            await SettleAsync();
            Assert.Equal(postsWhileHidden, (launcherWide.Posted.Count, stageNarrow.Posted.Count));

            // The swap: the change, then the other pair in front. Nothing is rebuilt.
            var caughtUp = 0;
            topLauncher.NativeCaughtUp += () => caughtUp++;
            bottomStage.NativeCaughtUp += () => caughtUp++;
            shell.ScreenSwapHost = apply =>
            {
                apply();
                topLauncher.IsVisible = bottomStage.IsVisible = true;
                topStage.IsVisible = bottomLauncher.IsVisible = false;
                return Task.CompletedTask;
            };
            Press(shell, GamepadAction.Search);
            await SettleAsync();

            Assert.Equal(2, caughtUp);
            Assert.Same(narrowFirstPage, shell.NarrowHome.Pages[0]);
            Assert.Same(wideFirstPage, shell.WideHome.Pages[0]);
            var layout = Assert.IsType<LauncherLayoutCommand>(launcherWide.Retained["layout"]);
            Assert.Equal(1920f, layout.CanvasWidth);
            Assert.InRange(Assert.Single(launcherWide.Sprites("h:0:6")).PivotX, 1500f, 1800f);
            // The selection the narrow Launcher had (slot 2) is the one the wide Launcher frames, already in place as
            // the screen comes in (not gliding from where it was when it was hidden).
            var focus = Assert.IsType<LauncherFocusCommand>(launcherWide.Retained["focus"]);
            Assert.Equal("h:0:2", focus.SpriteId);
            Assert.True(focus.Instant);
            Assert.Equal(1240f, Assert.IsType<StageCanvasCommand>(stageNarrow.Retained["canvas"]).Width);
            var hints = Assert.IsType<StageChromeCommand>(stageNarrow.Retained["chrome:hints"]).Image!;
            Assert.InRange(hints.X, 0f, 100f);
            Assert.InRange(hints.Y, 0f, 100f);
            var plate = stageNarrow.Posted.OfType<StageCentreCommand>().Last().Plate!;
            Assert.Equal(620f, plate.X + (plate.Width / 2), 2f);

            // Moving on the 16:9 screen now updates its Launcher, not the hidden narrow one.
            var narrowPosts = launcherNarrow.Posted.Count;
            Press(shell, GamepadAction.NavigateRight, 4);
            await SettleAsync();
            Assert.Equal(narrowPosts, launcherNarrow.Posted.Count);
            Assert.Equal("h:0:6", Assert.IsType<LauncherFocusCommand>(launcherWide.Retained["focus"]).SpriteId);
        }
        finally
        {
            top.Close();
            bottom.Close();
        }
    }
}
