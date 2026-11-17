using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.SecondScreen;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B15: the four screens the owner picked from proposals-screens.html on 16 November 2026, Big Type only: All apps as the Shelf
    /// (and named on the Stage when opened from home), every achievement as the Wall, the photo viewer as the Reel, the idle artwork as the
    /// Porthole. Every action today's screens have is reached the same way; today's look is unchanged.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeScreens
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

        private static string Hints(System.Collections.Generic.IEnumerable<HandheldHint> hints) =>
            string.Join(" ", hints.Select(h => $"{h.Button}:{h.Label}"));

        [AvaloniaFact]
        public Task Shelf_InAGame_NamesTheFocusedApp_MovesAsToday_AndItsHintsAreButtons() => BigType(() =>
        {
            var model = CompanionModel();
            var pinned = 0;
            model.PinToDockRequested = _ => new DockPinResult(DockPinOutcome.Pinned, pinned++ + 4);
            var companion = StartGame(model);
            model.Overlay = SecondScreenOverlayKind.Drawer;
            Assert.True(companion.IsDrawerLayer);
            Assert.False(companion.ShowsClassicDrawer);
            Assert.Equal("All apps", companion.ShelfKicker);
            Assert.Equal("Alpha", companion.ShelfName);
            Assert.Equal("1 / 12", companion.ShelfCount);
            Assert.Equal("A:Open Y:Pin to dock B:Close", Hints(companion.ScreenHints));
            Assert.Same(model.IsGameRunning ? companion.InGameBrush ?? companion.BrowsingGround : companion.BrowsingGround, companion.LayerGround);

            // Today's moves on today's five columns; a press says which side the next name comes from.
            companion.Dispatch(GamepadAction.NavigateRight);
            Assert.Equal(1, companion.DrawerIndex);
            Assert.Equal("App 0", companion.ShelfName);
            Assert.Equal(BigTypeScreenSide.Right, companion.ScreenSide);
            companion.Dispatch(GamepadAction.NavigateDown);
            Assert.Equal(6, companion.DrawerIndex);
            Assert.Equal(BigTypeScreenSide.Up, companion.ScreenSide);
            companion.Dispatch(GamepadAction.NavigateLeft);
            Assert.Equal(BigTypeScreenSide.Left, companion.ScreenSide);
            Assert.True(companion.ScreenQuick); // three presses in a row: a run, the short form

            // Y pins it: the line over the name says where.
            companion.Dispatch(GamepadAction.Actions);
            Assert.Equal("App 4 is in the dock, slot 5", companion.ShelfKicker);

            // A tap on a hint is the press it names: B closes.
            companion.TapHint(companion.ScreenHints.Single(h => h.Button == "B"));
            Assert.Equal(SecondScreenOverlayKind.None, model.Overlay);
            Assert.False(companion.IsDrawerLayer);
            Assert.Empty(companion.ScreenHints);
            return Task.CompletedTask;
        });

        [AvaloniaFact]
        public Task Shelf_ChangingADockSlot_KeepsPinHereAndClearSlot() => BigType(() =>
        {
            var model = CompanionModel();
            var cleared = 0;
            model.SlotCleared += () => cleared++;
            var companion = StartGame(model);
            model.DrawerTitle = "Choose an app for slot 2";
            model.CanClearSlot = true;
            model.Overlay = SecondScreenOverlayKind.Drawer;
            Assert.Equal("Choose an app for slot 2", companion.ShelfKicker);
            Assert.Equal("A:Pin here Y:Clear slot B:Close", Hints(companion.ScreenHints));
            companion.TapHint(companion.ScreenHints.Single(h => h.Button == "Y"));
            Assert.Equal(1, cleared);
            return Task.CompletedTask;
        });

        [AvaloniaFact]
        public Task Shelf_FromHome_NamesTheFocusedAppOnTheStage_WithTheHintsThere() => BigType(async () =>
        {
            using var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            var model = CompanionModel();
            shell.Companion = model;
            var companion = shell.CompanionScreen!;
            Assert.Same(shell.LauncherGroundBrush, companion.BrowsingGround);
            model.Overlay = SecondScreenOverlayKind.Drawer;
            Assert.True(shell.IsAllAppsOnStage);
            Assert.Equal(HandheldStageKind.AllApps, shell.Stage.Kind);
            Assert.Equal("All apps", shell.Stage.Label);
            Assert.Equal("Alpha", shell.Stage.Name);
            Assert.Equal("Not in the dock", shell.Stage.Line);
            Assert.Equal("1 of 12  ·  everything on this device", shell.Stage.Facts);
            Assert.Equal("A:Open Y:Pin to dock B:Close", Hints(shell.Hints));
            // The bottom screen's foot has none: they are on the Stage.
            Assert.Empty(companion.ScreenHints);
            Assert.Same(companion.BrowsingGround, companion.LayerGround);

            // The pad goes to All apps; the Stage's name follows, coming from the side pressed.
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal("App 0", shell.Stage.Name);
            Assert.Equal(HandheldDirection.Right, shell.LastMoveDirection);
            Press(shell, GamepadAction.NavigateDown);
            Assert.Equal("App 5", shell.Stage.Name);
            Assert.Equal("7 of 12  ·  everything on this device", shell.Stage.Facts);

            // A tap on the Stage's B closes All apps; the Stage goes back to what is focused.
            shell.TapStageHint(shell.Hints.Single(h => h.Button == "B"));
            Assert.Equal(SecondScreenOverlayKind.None, model.Overlay);
            Assert.False(shell.IsAllAppsOnStage);
            Assert.NotEqual("App 5", shell.Stage.Name);
        });

        [AvaloniaFact]
        public async Task Shelf_FromHome_TodaysLook_KeepsItsStage()
        {
            using var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3));
            var model = CompanionModel();
            shell.Companion = model;
            model.Overlay = SecondScreenOverlayKind.Drawer;
            Assert.False(shell.IsAllAppsOnStage);
            Assert.True(shell.CompanionScreen!.ShowsClassicDrawer);
            Assert.NotEqual("Alpha", shell.Stage.Title);
        }

        [AvaloniaFact]
        public Task Wall_KeepsTheFigureTheFiltersTheFocusedBadge_AndRefreshesLine() => BigType(() =>
        {
            var model = CompanionModel();
            model.CanRefresh = true;
            var companion = StartGame(model);
            companion.SelectTab(HandheldCompanionTab.Achievements);
            companion.OpenPanel();
            Assert.True(companion.IsAchievementsLayer);
            Assert.False(companion.ShowsClassicGrid);
            Assert.NotEqual(0, companion.LayerChangedSince);
            Assert.Equal(20, companion.WallItems.Count);
            Assert.Equal("8 / 20", companion.ProgressCount);
            Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, "Achievements  ·  {0:N0} pts", 180), companion.WallKicker);
            Assert.Equal("Achievement 1", companion.WallName);
            Assert.Equal("Do thing 1", companion.WallDescription);
            Assert.Equal(model.SelectedAchievementMeta, companion.WallMeta);
            Assert.Equal("Y:Refresh B:Close", Hints(companion.ScreenHints));

            // RB: only the locked; the wall holds the twelve, the name follows the focus.
            companion.Dispatch(GamepadAction.NextPlatform);
            Assert.Equal(12, companion.WallItems.Count);
            Assert.All(companion.WallItems, row => Assert.False(row.IsUnlocked));
            Assert.Equal(model.SelectedAchievement!.Title, companion.WallName);

            // A refresh's line takes the kicker's place and gives it back.
            model.AchievementsStatus = "Loading achievement details…";
            Assert.Equal("Loading achievement details…", companion.WallKicker);
            model.AchievementsStatus = null;
            Assert.StartsWith("Achievements", companion.WallKicker);

            // A tap on Y refreshes as Y does; on B it closes.
            var refreshed = 0;
            model.AchievementsRefreshed += () => refreshed++;
            companion.TapHint(companion.ScreenHints.Single(h => h.Button == "Y"));
            Assert.Equal(1, refreshed);
            companion.TapHint(companion.ScreenHints.Single(h => h.Button == "B"));
            Assert.False(companion.IsAchievementsLayer);
            return Task.CompletedTask;
        });

        [AvaloniaFact]
        public Task Reel_PagesAsToday_SaysWhichAndWhat_AndPlaysTheVideo() => BigType(() =>
        {
            var model = CompanionModel();
            var companion = StartGame(model);
            companion.SelectTab(HandheldCompanionTab.Photos);
            companion.OpenPanel();
            Assert.True(companion.IsPhotosLayer);
            Assert.False(companion.ShowsClassicViewer);
            Assert.Equal(4, companion.ReelPictures.Count);
            // These items have no file: the viewer's line says so (today's status, on the kind's line), and gives it back.
            Assert.Equal(model.MediaStatus, companion.ReelKind);
            model.MediaStatus = null;
            Assert.True(companion.ReelPictures[2].IsVideo);
            Assert.Equal("1 / 4", companion.ReelFigure);
            Assert.Equal("Screenshot", companion.ReelKind);
            Assert.Equal("Game", companion.ReelTitle);

            companion.Dispatch(GamepadAction.NavigateLeft); // going round at the start, as today
            model.MediaStatus = null;
            Assert.Equal("4 / 4", companion.ReelFigure);
            Assert.Equal("Title screen", companion.ReelKind);
            companion.Dispatch(GamepadAction.NavigateLeft);
            model.MediaStatus = null;
            Assert.Equal("3 / 4", companion.ReelFigure);
            Assert.Equal("Gameplay video", companion.ReelKind);
            Assert.Contains(companion.ScreenHints, h => h is { Button: "A", Label: "Play muted" });

            // The video playing (the controller hands it a path): the line says so, the native video sits over the picture.
            model.PlayingVideoPath = "/video.mp4";
            Assert.Equal("Playing muted", companion.ReelKind);
            Assert.True(companion.ShowsBigTypeVideo);
            Assert.Contains(companion.ScreenHints, h => h is { Button: "A", Label: "Stop" });
            companion.Dispatch(GamepadAction.Confirm);
            Assert.False(companion.ShowsBigTypeVideo);

            // B closes; the Photos tab lands on the picture it was on (today's).
            companion.Dispatch(GamepadAction.Cancel);
            Assert.False(companion.IsPhotosLayer);
            Assert.Equal(HandheldCompanionTab.Photos, companion.Tab);
            Assert.Equal(2, companion.PhotoIndex);
            return Task.CompletedTask;
        });

        [AvaloniaFact]
        public Task Layers_DrawnByTheBigTypeScreens_AndTodaysAreHidden() => BigType(async () =>
        {
            var model = CompanionModel();
            var companion = StartGame(model);
            var view = new HandheldCompanionView(false) { DataContext = companion };
            var window = new Window { Content = view, Width = 1240, Height = 1080 };
            window.Show();
            try
            {
                model.Overlay = SecondScreenOverlayKind.Drawer;
                await SettleAsync();
                Assert.True(view.FindDescendantOfType<HandheldShelfGrid>()!.IsEffectivelyVisible);
                Assert.False(view.FindControl<Control>("DrawerLayer")!.IsVisible);
                model.Overlay = SecondScreenOverlayKind.None;
                companion.SelectTab(HandheldCompanionTab.Achievements);
                companion.OpenPanel();
                await SettleAsync();
                Assert.True(view.FindDescendantOfType<HandheldBadgeWall>()!.IsEffectivelyVisible);
                Assert.False(view.FindControl<Control>("GridLayer")!.IsVisible);
            }
            finally
            {
                window.Close();
            }
        });

        [AvaloniaFact]
        public Task Porthole_TheArtworkInARoundWindow_TheNameBesideIt_NoLogo_AndAPressDropsIt() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3).Played("ps2", 1, 2);
            library.ArtMaker = (game, kind) => game.Id % 1000 == 1 ? MakeCover(game) : null;
            using var shell = await CreateAsync(library);
            shell.StartStatusUpdates();

            await shell.OnIdleAsync();
            Assert.True(shell.IsDrifting);
            Assert.True(shell.Stage.IsDrift);
            // The artwork in the window, not as the hero; the name in big type beside it (no logo picture, though this game has one).
            Assert.NotNull(shell.Stage.PortholeArt);
            Assert.Null(shell.Stage.Fanart);
            Assert.Null(shell.Stage.Logo);
            Assert.False(shell.Stage.IsHeroArt);
            Assert.True(shell.Stage.ShowsBigTypeName);
            Assert.Equal("Recently played", shell.Stage.Label);
            Assert.Equal(HandheldTokens.PortNameMaxWidth, shell.Stage.NameBoxWidth);
            Assert.Contains("  ·  last played ", shell.Stage.Facts);
            Assert.Empty(shell.Hints);
            var first = shell.Stage.PortholeArt;

            // The next game has no artwork: shown as when browsing, no window.
            await shell.OnIdleAsync();
            Assert.True(shell.IsDrifting);
            Assert.Null(shell.Stage.PortholeArt);
            Assert.True(shell.Stage.ShowsBigTypeName);

            // A press wakes it: the window is gone, the press does nothing else (today's rule).
            await shell.OnIdleAsync();
            Assert.NotNull(shell.Stage.PortholeArt);
            Assert.NotSame(first, shell.Stage.PortholeArt);
            Assert.True(shell.DispatchGamepadAction(GamepadAction.Confirm));
            Assert.False(shell.IsDrifting);
            Assert.Null(shell.Stage.PortholeArt);
            Assert.Null(shell.OpenFolder);
            Assert.NotEqual(HandheldTokens.PortNameMaxWidth, shell.Stage.NameBoxWidth);
        });

        [AvaloniaFact]
        public Task Porthole_DrawnByTheStage_NeverDrawsArtworkTheShellLetGo() => BigType(async () =>
        {
            // Found on the Thor (2026-11-17): the window drew a picture the shell had already disposed. The window now draws its own
            // copy; walk the drift with the Stage drawing (art, none, art, the newest again, a press) and draw every frame between.
            var library = new FakeLibrary().WithPlatform("ps2", 4).Played("ps2", 1, 2, 3);
            library.ArtMaker = (game, kind) => kind == HandheldArtKind.Fanart && game.Id % 1000 != 2 ? MakeCover(game) : null;
            using var shell = await CreateAsync(library);
            shell.StartStatusUpdates();
            var stage = new HandheldStageView(null, pinnedNarrow: false) { DataContext = shell };
            var window = new Window { Content = stage, Width = 1920, Height = 1080 };
            window.Show();
            try
            {
                for (var step = 0; step < 5; step++)
                {
                    await shell.OnIdleAsync();
                    for (var frame = 0; frame < 6; frame++)
                    {
                        await SettleAsync();
                        _ = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(480, 270)).Also(bitmap => bitmap.Render(stage));
                    }
                }
                Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateLeft));
                for (var frame = 0; frame < 6; frame++)
                    await SettleAsync();
                Assert.Null(shell.Stage.PortholeArt);
            }
            finally
            {
                window.Close();
            }
        });

        [AvaloniaFact]
        public async Task Porthole_TodaysLook_KeepsTheFanArtHero()
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3).Played("ps2", 1);
            library.ArtMaker = (game, kind) => kind == HandheldArtKind.Fanart ? MakeCover(game) : null;
            using var shell = await CreateAsync(library);
            shell.StartStatusUpdates();
            await shell.OnIdleAsync();
            Assert.True(shell.Stage.IsHeroArt);
            Assert.Null(shell.Stage.PortholeArt);
        }
    }
}

internal static class B15TestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
