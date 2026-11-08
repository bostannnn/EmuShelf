using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H6b: the in-game companion in Handheld mode. Today's companion (SecondScreenViewModel) keeps every feature; the
// Handheld face lays it out as one panel at a time over the dock, rests after a few seconds alone, and the D-pad reaches
// all of it.
public partial class HandheldShellViewModelTests
{
    internal static AchievementRowViewModel CompanionRow(int id, bool earned, int daysAgo = 0) =>
        new(new RetroAchievementsAchievement(id, $"Achievement {id}", $"Do thing {id}", Points: 5 * id, BadgeName: string.Empty,
                DisplayOrder: id, DateEarned: earned ? DateTimeOffset.UtcNow.AddDays(-daysAgo) : null, DateEarnedHardcore: null),
            badges: null, loadBadge: false);

    /// <summary>A companion with a 20-badge set (8 unlocked), three photos and a video, and two pinned apps.</summary>
    internal static SecondScreenViewModel CompanionModel()
    {
        var model = new SecondScreenViewModel();
        model.SetAchievements(Enumerable.Range(1, 20).Select(id => CompanionRow(id, earned: id <= 8, daysAgo: 10 - id)).ToList());
        model.AchievementsSummary = "8 / 20 · 180 pts";
        model.SetMedia(new CompanionMediaSet(1, "Game", [
            new CompanionMediaItem("Screenshot", GameMediaKind.Screenshot, null),
            new CompanionMediaItem("Screenshot", GameMediaKind.Screenshot, null),
            new CompanionMediaItem("Gameplay video", GameMediaKind.Video, null),
            new CompanionMediaItem("Title screen", GameMediaKind.TitleScreen, null),
        ]));
        model.Dock[0].Label = "Browser";
        model.Dock[2].Label = "Music";
        model.Apps.Add(new SecondScreenAppViewModel("a/b", "Alpha", null));
        for (var i = 0; i < 11; i++)
            model.Apps.Add(new SecondScreenAppViewModel($"app/{i}", $"App {i}", null));
        return model;
    }

    private static HandheldCompanionViewModel StartGame(SecondScreenViewModel model, HandheldGameEntry? entry = null)
    {
        var companion = new HandheldCompanionViewModel(model);
        companion.SetNowPlaying(entry ?? new HandheldGameEntry(1, "Ico", "ps2", "ps2", null, 3, TimeSpan.FromMinutes(95), true,
            AchievementText: "8 / 20"), "PlayStation 2");
        model.IsGameRunning = true;
        // The controller loads the running game's set as it starts (Handheld mode keeps it on the panel).
        model.Overlay = SecondScreenOverlayKind.Achievements;
        return companion;
    }

    [Fact]
    public void AGameStarting_BringsTheCompanionUpOnItsPanel_WithTheGamesRow()
    {
        var model = CompanionModel();
        var companion = StartGame(model);

        Assert.True(companion.OwnsPad);
        Assert.Equal(HandheldCompanionLayer.None, companion.Layer);
        Assert.Equal(HandheldCompanionTab.Achievements, companion.Tab);
        Assert.True(companion.IsPanelFocused);
        Assert.False(companion.IsResting);
        Assert.True(companion.IsRestPending);
        Assert.Equal("Ico", companion.NowPlayingTitle);
        Assert.Equal("PlayStation 2 · 1 h 35 min played", companion.NowPlayingLine);
        // The panel: the ring's numbers, and the three most recent unlocks newest first.
        Assert.Equal("8 / 20", companion.ProgressCount);
        Assert.Equal("40%", companion.ProgressPercent);
        Assert.Equal(string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0:N0} of {1:N0} points", 180, 1050), companion.ProgressPoints);
        Assert.Equal(["8", "7", "6"], companion.RecentUnlocks.Select(row => row.AchievementId));
        Assert.Equal("See all", companion.PanelHint);
    }

    [Fact]
    public void AGameWithoutASet_OpensOnPhotos()
    {
        var model = CompanionModel();
        model.ClearAchievements();
        var companion = StartGame(model, new HandheldGameEntry(1, "Ico", "ps2", "ps2", null, 0, TimeSpan.Zero, true));

        Assert.Equal(HandheldCompanionTab.Photos, companion.Tab);
        Assert.Equal("PlayStation 2 · first time playing", companion.NowPlayingLine);
        Assert.Equal(4, companion.PhotoDots.Count);
        Assert.Equal("Screenshot · 1 of 4", companion.PhotoCaption);
        // The Achievements tab says why it is empty.
        Press(companion, GamepadAction.PreviousPlatform);
        Assert.False(companion.HasAchievementSet);
        Assert.Contains("isn't linked", companion.AchievementsMessage, StringComparison.Ordinal);
    }

    private static void Press(HandheldCompanionViewModel companion, GamepadAction action, int times = 1)
    {
        for (var i = 0; i < times; i++)
            Assert.True(companion.Dispatch(action));
    }

    [Fact]
    public void Resting_TheFirstPressOnlyWakesIt()
    {
        var model = CompanionModel();
        var companion = StartGame(model);
        companion.Rest();
        Assert.True(companion.IsResting);

        Press(companion, GamepadAction.NavigateDown);

        Assert.False(companion.IsResting);
        Assert.Equal(HandheldCompanionZone.Panel, companion.Zone);
        Press(companion, GamepadAction.NavigateDown);
        Assert.Equal(HandheldCompanionZone.Dock, companion.Zone);

        // A layer holds the light: the countdown does not run, and a timer that fires anyway does not dim it.
        Press(companion, GamepadAction.NavigateUp);
        Press(companion, GamepadAction.Confirm);
        Assert.True(companion.IsAchievementsLayer);
        Assert.False(companion.IsRestPending);
        companion.Rest();
        Assert.False(companion.IsResting);
    }

    [Fact]
    public void ThePad_ReachesTheDock_AndYChangesASlot()
    {
        var model = CompanionModel();
        var activated = new List<int>();
        var edited = new List<int>();
        var drawers = 0;
        model.SlotActivated = activated.Add;
        model.SlotEditRequested = edited.Add;
        model.DrawerToggled = () => drawers++;
        var companion = StartGame(model);

        Press(companion, GamepadAction.NavigateDown);
        Assert.True(companion.DockItems[1].IsFocused);
        Assert.False(companion.IsPanelFocused);
        Press(companion, GamepadAction.Confirm);
        Assert.Equal([0], activated);

        Press(companion, GamepadAction.NavigateRight, 2);
        Press(companion, GamepadAction.Actions);
        Assert.Equal([2], edited);

        // Left to the first button is All apps; past it, the dock ends.
        Press(companion, GamepadAction.NavigateLeft, 4);
        Assert.Equal(0, companion.DockIndex);
        Assert.True(companion.DockItems[0].IsAllApps);
        Press(companion, GamepadAction.Confirm);
        Assert.Equal(1, drawers);
        // Y on All apps does nothing.
        Press(companion, GamepadAction.Actions);
        Assert.Equal([2], edited);
    }

    [Fact]
    public void TabsAndPhotos_LbRbSwitch_AndLeftRightPageInPlace()
    {
        var model = CompanionModel();
        var companion = StartGame(model);

        // Right from Achievements goes to Photos; then photos page in place, stopping at the ends.
        Press(companion, GamepadAction.NavigateRight);
        Assert.Equal(HandheldCompanionTab.Photos, companion.Tab);
        Press(companion, GamepadAction.NavigateRight, 2);
        Assert.Equal(2, companion.PhotoIndex);
        Assert.True(companion.IsPhotoVideo);
        Assert.StartsWith("Gameplay video", companion.PhotoCaption, StringComparison.Ordinal);
        Press(companion, GamepadAction.NavigateRight, 3);
        Assert.Equal(3, companion.PhotoIndex);
        Assert.True(companion.PhotoDots[3].IsCurrent);
        Press(companion, GamepadAction.PreviousPlatform);
        Assert.Equal(HandheldCompanionTab.Achievements, companion.Tab);
        Press(companion, GamepadAction.NextPlatform);
        Assert.Equal(HandheldCompanionTab.Photos, companion.Tab);
        Assert.Equal(3, companion.PhotoIndex);

        // A opens the viewer at that photo; B closes it, and the panel follows the viewer to where it ended.
        Press(companion, GamepadAction.Confirm);
        Assert.True(companion.IsPhotosLayer);
        Assert.Equal(3, model.MediaIndex);
        Press(companion, GamepadAction.NavigateRight);
        Assert.Equal(0, model.MediaIndex);
        Press(companion, GamepadAction.Cancel);
        Assert.False(companion.HasLayer);
        Assert.Equal(0, companion.PhotoIndex);
    }

    [Fact]
    public void EveryBadge_FiltersOnLbRb_AndBComesBackToThePanel()
    {
        var model = CompanionModel();
        var companion = StartGame(model);

        Press(companion, GamepadAction.Confirm);
        Assert.True(companion.IsAchievementsLayer);
        Assert.Equal(7, model.AchievementColumnCount);
        Assert.Equal(3, model.AchievementRows.Count);
        Press(companion, GamepadAction.NavigateRight, 2);
        Press(companion, GamepadAction.NavigateDown);
        Assert.Equal("10", model.SelectedAchievement!.AchievementId);

        // Locked: the badges re-flow, the selection stays on its badge (it is locked).
        Press(companion, GamepadAction.NextPlatform);
        Assert.Equal(AchievementDisplayFilter.Locked, model.AchievementFilter);
        Assert.Equal(12, model.VisibleAchievements.Count);
        Assert.Equal("10", model.SelectedAchievement!.AchievementId);
        Assert.Equal("Locked  12", companion.LockedFilterText);
        // Unlocked: it moves to the first one shown.
        Press(companion, GamepadAction.NextPlatform);
        Assert.Equal("1", model.SelectedAchievement!.AchievementId);
        Press(companion, GamepadAction.NextPlatform);
        Assert.Equal(AchievementDisplayFilter.Unlocked, model.AchievementFilter);

        Press(companion, GamepadAction.Cancel);
        Assert.False(companion.HasLayer);
        Assert.Equal(AchievementDisplayFilter.All, model.AchievementFilter);
        // The set stays loaded for the panel.
        Assert.Equal(SecondScreenOverlayKind.Achievements, model.Overlay);
        Assert.True(companion.IsPanelFocused);
    }

    [Fact]
    public void BadgeGrid_DownFromAColumnTheLastRowDoesNotReach_LandsOnItsLastBadge()
    {
        var model = CompanionModel();
        var companion = StartGame(model);
        Press(companion, GamepadAction.Confirm);
        // Row 2 holds badges 15–20 (six of seven): down from the seventh column of row 1 goes to badge 20.
        Press(companion, GamepadAction.NavigateRight, 6);
        Press(companion, GamepadAction.NavigateDown);
        Assert.Equal("14", model.SelectedAchievement!.AchievementId);
        Press(companion, GamepadAction.NavigateDown);
        Assert.Equal("20", model.SelectedAchievement!.AchievementId);
        Assert.False(model.MoveAchievementSelection(GamepadAction.NavigateDown));
    }

    [Fact]
    public void AllApps_TheDPadWalksItsGrid_AndAPinsOrOpens()
    {
        var model = CompanionModel();
        var launched = new List<string>();
        var cleared = 0;
        model.AppLaunched = app => launched.Add(app.Component);
        model.SlotCleared = () => cleared++;
        var companion = new HandheldCompanionViewModel(model);
        Assert.False(companion.OwnsPad);

        // Opened over the Launcher (browsing): light, and it has the pad.
        model.DrawerTitle = "All apps";
        model.Overlay = SecondScreenOverlayKind.Drawer;
        Assert.True(companion.OwnsPad);
        Assert.True(companion.IsDrawerLayer);
        Assert.True(companion.IsDrawerLight);
        Assert.True(companion.DrawerApps[0].IsFocused);
        Press(companion, GamepadAction.NavigateDown);
        Press(companion, GamepadAction.NavigateRight);
        Assert.Equal(6, companion.DrawerIndex);
        // Twelve apps in rows of five: down from the fourth column of the second row lands on the last app.
        Press(companion, GamepadAction.NavigateRight, 2);
        Press(companion, GamepadAction.NavigateDown);
        Assert.Equal(11, companion.DrawerIndex);
        Press(companion, GamepadAction.Confirm);
        Assert.Equal(["app/10"], launched);
        Assert.Equal(["A", "B"], companion.DrawerHints.Select(hint => hint.Button));

        // For a slot: Y clears it.
        model.CanClearSlot = true;
        Assert.Equal(["A", "Y", "B"], companion.DrawerHints.Select(hint => hint.Button));
        Press(companion, GamepadAction.Actions);
        Assert.Equal(1, cleared);
        Press(companion, GamepadAction.Cancel);
        Assert.Equal(SecondScreenOverlayKind.None, model.Overlay);
        Assert.False(companion.OwnsPad);

        // On the 16:9 screen it is eight wide.
        companion.IsOnWideScreen = true;
        model.Overlay = SecondScreenOverlayKind.Drawer;
        Assert.Equal(0, companion.DrawerIndex);
        Press(companion, GamepadAction.NavigateDown);
        Assert.Equal(8, companion.DrawerIndex);
        Assert.Equal(11, model.AchievementColumnCount);
    }

    [Fact]
    public void TheTitlePagesBadgeGrid_OverTheLauncher_ClosesBackToIt()
    {
        var model = CompanionModel();
        var closed = 0;
        model.OverlayClosed = () => closed++;
        model.CanRefresh = true;
        var companion = new HandheldCompanionViewModel(model);

        model.Overlay = SecondScreenOverlayKind.Achievements;

        Assert.True(companion.IsAchievementsLayer);
        Assert.True(companion.OwnsPad);
        Assert.Equal(["Y", "B"], companion.GridHints.Select(hint => hint.Button).Where(button => button != "A"));
        Press(companion, GamepadAction.Cancel);
        Assert.Equal(SecondScreenOverlayKind.None, model.Overlay);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Touch_FirstTapSelectsADockButton_ASecondOpensIt()
    {
        var model = CompanionModel();
        var activated = new List<int>();
        model.SlotActivated = activated.Add;
        var companion = StartGame(model);

        companion.TapDockItem(companion.DockItems[3]);
        Assert.Equal(3, companion.DockIndex);
        Assert.Empty(activated);
        companion.TapDockItem(companion.DockItems[3]);
        Assert.Equal([2], activated);

        companion.SelectTab(HandheldCompanionTab.Photos);
        Assert.Equal(HandheldCompanionZone.Panel, companion.Zone);
        companion.SwipePanel(1);
        Assert.Equal(1, companion.PhotoIndex);
    }

    [Fact]
    public void TheGameEnding_PutsTheCompanionAway()
    {
        var model = CompanionModel();
        var companion = StartGame(model);
        Press(companion, GamepadAction.Confirm);

        model.IsGameRunning = false;
        model.Overlay = SecondScreenOverlayKind.None;

        Assert.False(companion.OwnsPad);
        Assert.False(companion.HasLayer);
        Assert.False(companion.IsRestPending);
        Assert.False(companion.Back());
    }

    [Fact]
    public async Task InHandheldMode_TheShellHandsThePadToTheCompanion_WhileAGameRuns()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        var shell = await CreateAsync(library);
        var model = CompanionModel();
        shell.Companion = model;
        var companion = Assert.IsType<HandheldCompanionViewModel>(shell.CompanionScreen);

        // Play from the Title page: the companion gets the game's row before the emulator starts.
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("ps2 game 000", companion.NowPlayingTitle);
        Assert.Equal("ps2 · first time playing", companion.NowPlayingLine);

        model.IsGameRunning = true;
        Assert.True(shell.CompanionOwnsPad);
        var zone = shell.Zone;
        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(HandheldCompanionZone.Dock, companion.Zone);
        Assert.Equal(zone, shell.Zone);
        // Back is the companion's too, and never leaves the app while a game runs.
        Assert.True(shell.DispatchBack());

        model.IsGameRunning = false;
        Assert.False(shell.CompanionOwnsPad);
        Press(shell, GamepadAction.NavigateDown);
        Assert.Equal(1, shell.OpenFolder!.SelectedIndex);
    }

    [Fact]
    public async Task BackFromAGame_LandsOnItsTile_WithRecentUpdated_SoTwoPressesResume()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 3);
        library.RecordsPlays = true;
        var games = library.Games["ps2"];
        games[0] = games[0] with { LastPlayedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        games[1] = games[1] with { LastPlayedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.Recent, HandheldSlot.PlatformFolder("ps2")]));
        var shell = new HandheldShellViewModel(library, layoutStore: store);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);

        // Recent holds game 0 then game 1; play game 1 from it.
        Press(shell, GamepadAction.Confirm);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal([games[0].Id, games[1].Id], shell.OpenFolder!.Items.Select(tile => tile.Entry.Id));
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Confirm);
        await Task.Delay(20, TestContext.Current.CancellationToken);

        // Back on the grid, Recent re-read with it first, and the selection still on it.
        Assert.False(shell.IsTitleOpen);
        var played = library.Games["ps2"][1].Id;
        Assert.Equal(played, shell.OpenFolder!.Items[0].Entry.Id);
        Assert.Equal(played, shell.OpenFolder.SelectedTile!.Entry.Id);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal(played, shell.TitlePage!.Entry.Id);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal([played, played], library.Launched);
    }
}
