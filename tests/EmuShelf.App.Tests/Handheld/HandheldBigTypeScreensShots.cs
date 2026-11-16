using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.SecondScreen;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M47 B15: the four screens the owner picked from proposals-screens.html (16 November 2026) as frames to look at, each through its states,
// beside the design set (which shows each once): All apps as the Shelf (in a game, moving, a pin, changing a dock slot, from home with
// the Stage naming the app), every achievement as the Wall (opened, a locked one, only the locked, a hidden one, revealed, a refresh's
// line, an empty filter), the photo viewer as the Reel (opened, the next, the video, the type hidden) and the idle artwork as the
// Porthole (the first game, the next, a game with no artwork, the screens swapped). Rendered only on request, as the design set is, into
// EMUSHELF_DESIGN_SNAPSHOT_DIR with EMUSHELF_B15_SHOTS=1 and EMUSHELF_DESIGN_LOOK=BigType; a normal test run skips it. Held still
// (Reduce motion on), so each frame is the state at rest.
public partial class HandheldShellViewModelTests
{
    private static bool B15Shots => Environment.GetEnvironmentVariable("EMUSHELF_B15_SHOTS") is { Length: > 0 };

    [AvaloniaFact]
    public async Task BigTypeScreensShots_Companion()
    {
        if (DesignOutput is null || !B15Shots || DesignLook != HandheldLook.BigType)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;
        shell.ReduceMotion = true;
        var companion = shell.CompanionScreen!;
        var model = rig.Data.Companion;
        model.IsGameRunning = true;
        var view = new HandheldCompanionView(false) { DataContext = companion };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            async Task Shot(string name, string what) => await DesignDumpAsync((window, $"bt15-{name}-bottom", what));

            // ---- 2 Shelf ----
            model.DrawerTitle = "All apps";
            model.Overlay = SecondScreenOverlayKind.Drawer;
            Assert.True(companion.IsDrawerLayer);
            await Shot("shelf-open", "Shelf (All apps, in a game): opened on the first app, its name in big type, 1 / 18, the ring, A Open · Y Pin to dock · B Close");
            companion.Dispatch(GamepadAction.NavigateDown);
            companion.Dispatch(GamepadAction.NavigateRight);
            await Shot("shelf-moved", "Shelf: down and right, the focused icon grown, its neighbours stepped away, its name in big type");
            companion.Dispatch(GamepadAction.Actions);
            await Shot("shelf-pinned", "Shelf: Y pinned it, the line over the name says where (or that the dock is full)");
            for (var i = 0; i < 4; i++)
                companion.Dispatch(GamepadAction.NavigateDown);
            await Shot("shelf-scrolled", "Shelf: down past the fourth row, the rows scrolled by whole rows");
            model.Overlay = SecondScreenOverlayKind.None;
            model.DrawerTitle = "Choose an app for slot 2";
            model.CanClearSlot = true;
            model.Overlay = SecondScreenOverlayKind.Drawer;
            await Shot("shelf-slot", "Shelf: changing dock slot 2 (Y on a slot): the title over the name, A Pin here · Y Clear slot · B Close");
            model.Overlay = SecondScreenOverlayKind.None;
            model.CanClearSlot = false;
            model.DrawerTitle = "All apps";

            // ---- 4 Wall ----
            companion.StepTab(1);
            companion.Dispatch(GamepadAction.Confirm);
            Assert.True(companion.IsAchievementsLayer);
            await Shot("wall-open", "Wall (every achievement): 5 / 12, Achievements · 60 pts, All / Locked / Unlocked, the first badge named in big type");
            for (var i = 0; i < 6; i++)
                companion.Dispatch(GamepadAction.NavigateRight);
            await Shot("wall-locked", "Wall: a locked badge focused (grey and dimmed), its name, what it asks, its points");
            companion.Dispatch(GamepadAction.NextPlatform);
            await Shot("wall-filter-locked", "Wall: RB, only the locked ones");
            companion.Dispatch(GamepadAction.NextPlatform);
            await Shot("wall-filter-unlocked", "Wall: RB again, only the unlocked ones");
            companion.Dispatch(GamepadAction.PreviousPlatform);
            companion.Dispatch(GamepadAction.PreviousPlatform);
            model.AchievementsStatus = "Loading achievement details…";
            await Shot("wall-refreshing", "Wall: what a refresh is doing, on the line over the figure");
            model.AchievementsStatus = null;
            companion.Dispatch(GamepadAction.Cancel);

            // ---- 9 Reel ----
            companion.StepTab(1);
            companion.Dispatch(GamepadAction.Confirm);
            Assert.True(companion.IsPhotosLayer);
            await Shot("reel-open", "Reel (the photo viewer): the first picture a fifth smaller, the next at the edge, 1 / 4, Screenshot, the game's title");
            companion.Dispatch(GamepadAction.NavigateRight);
            await Shot("reel-next", "Reel: right, the row slid, 2 / 4, both neighbours at the edges");
            companion.Dispatch(GamepadAction.NavigateRight);
            await Shot("reel-video", "Reel: the video, its poster under the play mark, A Play muted");
            model.TapMedia();
            await Shot("reel-bare", "Reel: a tap hid the figure, the lines and the hints; the pictures stay");
            model.TapMedia();
            companion.Dispatch(GamepadAction.Cancel);
        }
        finally
        {
            window.Close();
            shell.ReduceMotion = false;
        }
    }

    [AvaloniaFact]
    public async Task BigTypeScreensShots_AllAppsFromHome()
    {
        if (DesignOutput is null || !B15Shots || DesignLook != HandheldLook.BigType)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;
        shell.ReduceMotion = true;
        var companion = shell.CompanionScreen!;
        var model = rig.Data.Companion;
        var view = new HandheldCompanionView(false) { DataContext = companion };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            model.DrawerTitle = "All apps";
            model.Overlay = SecondScreenOverlayKind.Drawer;
            Assert.True(shell.IsAllAppsOnStage);
            await DesignDumpAsync((window, "bt15-shelf-home-bottom", "Shelf opened from home's dock: on the Launcher's colour, no hints at the foot"),
                (rig.StageWindow, "bt15-shelf-home-top", "Stage while All apps is open from home: All apps over the focused app's name, where it is in the dock, its place of all, the hints"));
            Press(shell, GamepadAction.NavigateRight);
            Press(shell, GamepadAction.NavigateDown);
            await DesignDumpAsync((window, "bt15-shelf-home-moved-bottom", "Shelf from home: moved"),
                (rig.StageWindow, "bt15-shelf-home-moved-top", "Stage: the next app's name, Not in the dock / In the dock, slot n"));
            model.Overlay = SecondScreenOverlayKind.None;
        }
        finally
        {
            window.Close();
            shell.ReduceMotion = false;
        }
    }

    [AvaloniaFact]
    public async Task BigTypeScreensShots_Porthole()
    {
        if (DesignOutput is null || !B15Shots || DesignLook != HandheldLook.BigType)
            return;
        foreach (var swapped in new[] { false, true })
        {
            using var rig = await DesignRigAsync(swapped: swapped);
            var shell = rig.Shell;
            shell.ReduceMotion = true;
            shell.StartStatusUpdates();
            try
            {
                // Held still the drift does not start (as today), so the steps are taken with motion on and each frame waits for rest.
                shell.ReduceMotion = false;
                await shell.OnIdleAsync();
                Assert.True(shell.IsDrifting);
                await DesignPairAsync(rig, "bt15-porthole-first", "Porthole: the newest game, its console's backdrop, Recently played over its name, its artwork in the round window, no hints");
                await shell.OnIdleAsync();
                await DesignPairAsync(rig, "bt15-porthole-no-art", "Porthole: a game with no artwork, shown as when browsing (its name over its console's backdrop, no window)");
                await shell.OnIdleAsync();
                await DesignPairAsync(rig, "bt15-porthole-next", "Porthole: the next game");
                Press(shell, GamepadAction.Confirm);
                Assert.False(shell.IsDrifting);
                await DesignPairAsync(rig, "bt15-porthole-woken", "Porthole: a press woke it, the window gone, the focused item back");
            }
            finally
            {
                shell.StopStatusUpdates();
                shell.ReduceMotion = false;
            }
        }
    }
}
