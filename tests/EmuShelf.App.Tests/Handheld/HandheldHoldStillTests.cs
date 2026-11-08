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

    [AvaloniaFact]
    public async Task HoldsStill_FollowsTheDevice_AtOnceWhenItChanges_AndHoldsTheSway()
    {
        var device = new FakeDeviceStatusService();
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
        shell.DeviceStatus = device;
        shell.RefreshStatus();
        Assert.False(shell.HoldsStill);
        Assert.False(shell.MediumMotion.HoldsStill);

        // Battery saver comes on: the service says so, and the shell reads it without waiting for the pill's refresh.
        device.Status = device.Status with { HoldsStill = true };
        device.Raise();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.HoldsStill);
        Assert.True(shell.MediumMotion.HoldsStill);
        Assert.True(shell.MediumMotion.IsSwayResting);

        device.Status = device.Status with { HoldsStill = false };
        device.Raise();
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.HoldsStill);
        Assert.False(shell.MediumMotion.HoldsStill);

        // A replaced source is the one listened to; the old one's changes no longer count.
        var other = new FakeDeviceStatusService { Status = new DeviceStatus(50, true, false, HoldsStill: true) };
        shell.DeviceStatus = other;
        device.Status = device.Status with { HoldsStill = false };
        shell.RefreshStatus();
        Assert.True(shell.HoldsStill);
        Assert.Equal("50%", shell.BatteryText);
        shell.Dispose();
    }

    [AvaloniaFact]
    public async Task HoldingStill_StopsTheIdleDrift_AndNoneStartsUntilItIsLifted()
    {
        var device = new FakeDeviceStatusService();
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 4).Played("ps2", 2, 0));
        shell.DeviceStatus = device;
        shell.StartStatusUpdates();
        var home = shell.Stage.Title;
        await shell.OnIdleAsync();
        Assert.True(shell.IsDrifting);

        // Battery saver comes on mid-drift: the Stage goes back to what is selected, as a press would take it.
        device.Status = device.Status with { HoldsStill = true };
        shell.RefreshStatus();
        Assert.False(shell.IsDrifting);
        Assert.Equal(home, shell.Stage.Title);
        Assert.False(shell.Stage.IsDrift);

        // And the idle timer running out starts nothing while it holds.
        await shell.OnIdleAsync();
        Assert.False(shell.IsDrifting);

        device.Status = device.Status with { HoldsStill = false };
        shell.RefreshStatus();
        await shell.OnIdleAsync();
        Assert.True(shell.IsDrifting);
        shell.Dispose();
    }

    [AvaloniaFact]
    public async Task TheNativeStage_IsToldToHoldStill_AsState_TheMomentTheDeviceSaysSo()
    {
        var stageScreen = new RecordingNativeScreen();
        var previousHost = HandheldNativeStage.Host;
        HandheldNativeStage.Host = stageScreen;
        Window? top = null;
        try
        {
            var device = new FakeDeviceStatusService();
            var (shell, _) = await CreateFullHomeAsync();
            shell.DeviceStatus = device;
            shell.RefreshStatus();
            top = new Window { Content = new HandheldStageView { DataContext = shell }, Width = 1920, Height = 1080 };
            top.Show();
            await SettleAsync();
            // Retained under its own key, so a rebuilt GL context starts from the device's current say.
            Assert.False(Assert.IsType<StageMotionCommand>(stageScreen.Retained["motion"]).HoldStill);

            device.Status = device.Status with { HoldsStill = true };
            device.Raise();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Assert.IsType<StageMotionCommand>(stageScreen.Retained["motion"]).HoldStill);

            device.Status = device.Status with { HoldsStill = false };
            device.Raise();
            Dispatcher.UIThread.RunJobs();
            Assert.False(Assert.IsType<StageMotionCommand>(stageScreen.Retained["motion"]).HoldStill);
        }
        finally
        {
            top?.Close();
            HandheldNativeStage.Host = previousHost;
        }
    }
}
