using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M45 H7c: the device's say on motion (battery saver, reduced motion) reaches the Handheld shell the moment it changes,
/// and the medium's sway holds still with it. (The native Stage's own hold is <c>StageMotionCommand</c>, checked on the Thor.)
/// </summary>
public partial class HandheldShellViewModelTests
{
    private sealed class FakeDeviceStatusService : IDeviceStatusService
    {
        public DeviceStatus Status { get; set; } = new(80, false, true);

        public DeviceStatus GetStatus() => Status;

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();
    }

    /// <summary>
    /// Who asks for stillness: the device (battery saver, the system's reduced motion) or, under Big Type, the app's own
    /// switch (M47 B10). The tests below run once for each, so the held-still forms B5 to B9 test are what either one reaches.
    /// </summary>
    private sealed class StillAsker(FakeDeviceStatusService device, bool app)
    {
        public HandheldShellViewModel? Shell { get; set; }

        public void Ask(bool still, bool raise = true)
        {
            if (app)
            {
                Shell!.ReduceMotion = still;
                return;
            }
            device.Status = device.Status with { HoldsStill = still };
            if (raise)
                device.Raise();
            else
                Shell!.RefreshStatus();
        }
    }

    [AvaloniaFact]
    public Task HoldsStill_FollowsTheDevice_AtOnceWhenItChanges_AndHoldsTheSway() => HoldsStillFollowsAtOnce(app: false);

    private static async Task HoldsStillFollowsAtOnce(bool app)
    {
        var device = new FakeDeviceStatusService();
        var asker = new StillAsker(device, app);
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
        asker.Shell = shell;
        shell.DeviceStatus = device;
        shell.RefreshStatus();
        Assert.False(shell.HoldsStill);
        Assert.False(shell.MediumMotion.HoldsStill);

        // Battery saver comes on (or the app's switch): the shell reads it without waiting for the pill's refresh.
        asker.Ask(true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.HoldsStill);
        Assert.True(shell.MediumMotion.HoldsStill);
        Assert.True(shell.MediumMotion.IsSwayResting);

        asker.Ask(false);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.HoldsStill);
        Assert.False(shell.MediumMotion.HoldsStill);

        if (!app)
        {
            // A replaced source is the one listened to; the old one's changes no longer count.
            var other = new FakeDeviceStatusService { Status = new DeviceStatus(50, true, false, HoldsStill: true) };
            shell.DeviceStatus = other;
            device.Status = device.Status with { HoldsStill = false };
            shell.RefreshStatus();
            Assert.True(shell.HoldsStill);
            Assert.Equal("50%", shell.BatteryText);
        }
        shell.Dispose();
    }

    [AvaloniaFact]
    public Task HoldingStill_StopsTheIdleDrift_AndNoneStartsUntilItIsLifted() => HoldingStillStopsTheDrift(app: false);

    private static async Task HoldingStillStopsTheDrift(bool app)
    {
        var device = new FakeDeviceStatusService();
        var asker = new StillAsker(device, app);
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 4).Played("ps2", 2, 0));
        asker.Shell = shell;
        shell.DeviceStatus = device;
        shell.StartStatusUpdates();
        var home = shell.Stage.Title;
        await shell.OnIdleAsync();
        Assert.True(shell.IsDrifting);

        // Battery saver comes on mid-drift: the Stage goes back to what is selected, as a press would take it.
        asker.Ask(true, raise: false);
        Assert.False(shell.IsDrifting);
        Assert.Equal(home, shell.Stage.Title);
        Assert.False(shell.Stage.IsDrift);

        // And the idle timer running out starts nothing while it holds.
        await shell.OnIdleAsync();
        Assert.False(shell.IsDrifting);

        asker.Ask(false, raise: false);
        await shell.OnIdleAsync();
        Assert.True(shell.IsDrifting);
        shell.Dispose();
    }

    [AvaloniaFact]
    public Task TheNativeStage_IsToldToHoldStill_AsState_TheMomentTheDeviceSaysSo() => TheNativeScreensHoldStillAtOnce(app: false);

    /// <summary>
    /// Both native compositors are told as state the moment it changes: the Stage's <c>StageMotionCommand</c> and, under Big Type,
    /// the Launcher's <c>LauncherMotionCommand</c> (B5's hook), so every move there takes its held-still form with no restart.
    /// </summary>
    private static async Task TheNativeScreensHoldStillAtOnce(bool app)
    {
        var stageScreen = new RecordingNativeScreen();
        var launcherScreen = new RecordingNativeScreen();
        var (previousHost, previousLauncher) = (HandheldNativeStage.Host, HandheldNativeStage.Launcher);
        HandheldNativeStage.Host = stageScreen;
        HandheldNativeStage.Launcher = launcherScreen;
        Window? top = null, bottom = null;
        try
        {
            var device = new FakeDeviceStatusService();
            var asker = new StillAsker(device, app);
            var (shell, _) = await CreateFullHomeAsync();
            asker.Shell = shell;
            shell.DeviceStatus = device;
            shell.RefreshStatus();
            top = new Window { Content = new HandheldStageView { DataContext = shell }, Width = 1920, Height = 1080 };
            top.Show();
            var launcher = new HandheldLauncherView { DataContext = shell };
            bottom = new Window { Content = launcher, Width = 1240, Height = 1080 };
            bottom.Show();
            await SettleAsync();
            // Retained under its own key, so a rebuilt GL context starts from the current say.
            Assert.False(Assert.IsType<StageMotionCommand>(stageScreen.Retained["motion"]).HoldStill);
            var bigType = HandheldTokens.IsBigType;
            if (bigType)
                Assert.False(Assert.IsType<LauncherMotionCommand>(launcherScreen.Retained["motion"]).HoldStill);

            asker.Ask(true);
            Dispatcher.UIThread.RunJobs();
            await SettleAsync();
            Assert.True(Assert.IsType<StageMotionCommand>(stageScreen.Retained["motion"]).HoldStill);
            if (bigType)
            {
                Assert.True(Assert.IsType<LauncherMotionCommand>(launcherScreen.Retained["motion"]).HoldStill);
                // The Avalonia Launcher too: its root holds still (the lift's transition goes, the pulses rest).
                Assert.Contains("still", launcher.FindControl<Panel>("Root")!.Classes);
            }

            asker.Ask(false);
            Dispatcher.UIThread.RunJobs();
            await SettleAsync();
            Assert.False(Assert.IsType<StageMotionCommand>(stageScreen.Retained["motion"]).HoldStill);
            if (bigType)
            {
                Assert.False(Assert.IsType<LauncherMotionCommand>(launcherScreen.Retained["motion"]).HoldStill);
                Assert.DoesNotContain("still", launcher.FindControl<Panel>("Root")!.Classes);
            }
        }
        finally
        {
            top?.Close();
            bottom?.Close();
            HandheldNativeStage.Host = previousHost;
            HandheldNativeStage.Launcher = previousLauncher;
        }
    }

    /// <summary>
    /// M47 B10: the app's own reduced-motion switch, under Big Type, with the system's off. Each test is the device's above,
    /// asked by the switch instead; the moves' held-still forms themselves are B5 to B9's fixed-moment tests.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class AppReduceMotion
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

        [AvaloniaFact]
        public Task HoldsStill_FollowsTheSwitch_AtOnce() => BigType(() => HoldsStillFollowsAtOnce(app: true));

        [AvaloniaFact]
        public Task TheSwitch_StopsTheIdleDrift() => BigType(() => HoldingStillStopsTheDrift(app: true));

        [AvaloniaFact]
        public Task TheSwitch_ReachesBothNativeScreens_AndTheAvaloniaLauncher() => BigType(() => TheNativeScreensHoldStillAtOnce(app: true));

        [AvaloniaFact]
        public Task TheDevice_ReachesBothNativeScreens_UnderBigType() => BigType(() => TheNativeScreensHoldStillAtOnce(app: false));

        [AvaloniaFact]
        public async Task EitherOneIsEnough_AndBothOff_TheMovesRun()
        {
            await BigType(async () =>
            {
                var device = new FakeDeviceStatusService();
                var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
                shell.DeviceStatus = device;
                shell.RefreshStatus();
                Assert.False(shell.HoldsStill);

                shell.ReduceMotion = true;
                device.Status = device.Status with { HoldsStill = true };
                shell.RefreshStatus();
                Assert.True(shell.HoldsStill);
                // The system lets go; the switch still holds.
                device.Status = device.Status with { HoldsStill = false };
                shell.RefreshStatus();
                Assert.True(shell.HoldsStill);
                // The switch lets go with the system on; the system still holds.
                device.Status = device.Status with { HoldsStill = true };
                shell.RefreshStatus();
                shell.ReduceMotion = false;
                Assert.True(shell.HoldsStill);
                // Both off: the moves run.
                device.Status = device.Status with { HoldsStill = false };
                shell.RefreshStatus();
                Assert.False(shell.HoldsStill);
                Assert.False(shell.MediumMotion.HoldsStill);
                shell.Dispose();
            });
        }

        [AvaloniaFact]
        public async Task ALaunch_TakesItsHeldStillForm_AndTheCompanionFollowsTheSwitch()
        {
            await BigType(async () =>
            {
                var library = new FakeLibrary().WithPlatform("gc", 3);
                var game = library.Games["gc"][0];
                library.Details[game.Id] = Details();
                var launch = new TaskCompletionSource<bool>();
                library.PendingLaunch = launch;
                var shell = await CreateAsync(library);
                shell.Companion = new SecondScreenViewModel();
                shell.ReduceMotion = true;
                Press(shell, GamepadAction.Confirm); // open the folder
                for (var i = 0; i < 100 && shell.OpenFolder is not { IsLoaded: true }; i++)
                    await Task.Delay(5);
                Press(shell, GamepadAction.Confirm); // A plays (B8)
                for (var i = 0; i < 200 && shell.Playing is not { IsTakenOver: true }; i++)
                    await Task.Delay(5);
                // Held still, the takeover is there at once (no TakeoverAt wait) and the companion is told.
                Assert.True(shell.Playing!.IsTakenOver);
                Assert.True(shell.CompanionScreen!.HoldsStill);

                // Turned off mid-game: the companion's parts move again from here on.
                shell.ReduceMotion = false;
                Assert.False(shell.CompanionScreen.HoldsStill);
                launch.SetResult(true);
                shell.Dispose();
            });
        }
    }
}
