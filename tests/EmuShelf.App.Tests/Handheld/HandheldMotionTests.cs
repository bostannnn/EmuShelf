using EmuShelf.Core.Handheld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Services;
using EmuShelf.Core.Settings;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>A clock that only moves when the test says so.</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero).AddTicks(_ticks);

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}

public class HandheldMotionTests
{
    private static int StepUntilSettled(HandheldSpring spring, double frameSeconds = 1d / 60d, int maxFrames = 600)
    {
        for (var frame = 1; frame <= maxFrames; frame++)
        {
            spring.Step(frameSeconds);
            if (spring.IsSettled)
                return frame;
        }
        return -1;
    }

    [Fact]
    public void SelectorSpring_SettlesOnItsTarget_WithBarelyAnyOvershoot()
    {
        var spring = new HandheldSpring(HandheldMotion.Selector);
        spring.Snap(0);
        spring.Retarget(100);
        var peak = 0d;
        for (var i = 0; i < 120 && !spring.IsSettled; i++)
        {
            spring.Step(1d / 60d);
            peak = Math.Max(peak, spring.Value);
        }

        Assert.True(spring.IsSettled);
        Assert.Equal(100, spring.Value);
        Assert.InRange(peak, 100, 101.5);
        Assert.InRange(HandheldMotion.Selector.DampingRatio, 0.85, 0.95);
    }

    [Fact]
    public void Retargeting_KeepsTheSpeed_SoAFastSecondPressGlides()
    {
        var spring = new HandheldSpring(HandheldMotion.Selector);
        spring.Snap(0);
        spring.Retarget(100);
        for (var i = 0; i < 4; i++)
            spring.Step(1d / 60d);
        var position = spring.Value;
        var speed = spring.Velocity;
        Assert.True(speed > 0);

        spring.Retarget(200);

        // Nothing jumps and nothing restarts from rest: the next frame carries on from the same place and pace.
        Assert.Equal(position, spring.Value);
        Assert.Equal(speed, spring.Velocity);
        spring.Step(1d / 60d);
        Assert.True(spring.Value > position);
        Assert.True(StepUntilSettled(spring) > 0);
        Assert.Equal(200, spring.Value);
    }

    [Fact]
    public void Displace_MovesTheValueOnly_ThenTheTrackGlidesBack()
    {
        var spring = new HandheldSpring(HandheldMotion.Track);
        spring.Snap(0);

        spring.Displace(264);

        Assert.Equal(264, spring.Value);
        Assert.Equal(0, spring.Target);
        Assert.True(StepUntilSettled(spring) > 0);
        Assert.Equal(0, spring.Value);
    }

    [Fact]
    public void ALongFrame_NeverDestabilisesTheSpring()
    {
        var spring = new HandheldSpring(HandheldMotion.Selector);
        spring.Snap(0);
        spring.Retarget(100);
        spring.Step(2); // a two-second stall is clamped and integrated in small steps

        Assert.InRange(spring.Value, 0, 110);
    }

    [Fact]
    public void PopCurve_IsTheSpringsStepResponse_WithASmallOvershoot()
    {
        var easing = new HandheldSpringEasing(HandheldMotion.Pop, HandheldMotion.PopFromScale, 1);

        Assert.Equal(0, easing.Ease(0));
        Assert.Equal(1, easing.Ease(1));
        Assert.InRange(easing.Peak, 1.01, 1.08);
        // Spec: settles in about half a second.
        Assert.InRange(easing.Duration.TotalSeconds, 0.35, 0.8);
        // Monotonic on the way up (no wobble before the first peak).
        var samples = Enumerable.Range(0, 20).Select(i => easing.Ease(i / 60d)).ToList();
        Assert.Equal(samples.OrderBy(value => value), samples);
    }

    [Fact]
    public void HeldDirection_IsPressesCloserThan150Milliseconds()
    {
        var clock = new ManualClock();
        var held = new HandheldHeldDirection(clock);

        Assert.False(held.Press());
        clock.Advance(TimeSpan.FromMilliseconds(90));
        Assert.True(held.Press());
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.False(held.Press());
        held.Reset();
        Assert.False(held.Press());
    }
}

public partial class HandheldShellViewModelTests
{
    private static async Task<(HandheldShellViewModel Shell, ManualClock Clock)> CreateWithClockAsync(FakeLibrary library)
    {
        var clock = new ManualClock();
        var store = new InMemoryHandheldHomeLayoutStore();
        store.Save(HandheldHomeLayout.FromSlots(library.Platforms.Select(platform => HandheldSlot.PlatformFolder(platform.SystemId))));
        var shell = new HandheldShellViewModel(library, clock: clock, layoutStore: store);
        await shell.InitializeAsync();
        return (shell, clock);
    }

    [Fact]
    public async Task ASinglePress_ChangesTheStageAtOnce()
    {
        var (shell, clock) = await CreateWithClockAsync(new FakeLibrary().WithPlatform("ps2", 1).WithPlatform("gc", 1));
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateRight));

        Assert.False(shell.IsStagePending);
        Assert.Equal("GC System", shell.Stage.Title);
    }

    [Fact]
    public async Task AHeldDirection_WaitsUntilTheMovementSettles_ThenChangesTheStageOnce()
    {
        var (shell, clock) = await CreateWithClockAsync(
            new FakeLibrary().WithPlatform("ps2", 1).WithPlatform("gc", 1).WithPlatform("wii", 1));
        var changes = 0;
        shell.StageChanged += () => changes++;

        Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.Equal("GC System", shell.Stage.Title);
        clock.Advance(TimeSpan.FromMilliseconds(80));
        Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateRight));

        // The selector moved on to the third folder; the top screen waits for the run to end.
        Assert.Equal(2, shell.HomePosition.Slot);
        Assert.True(shell.IsStagePending);
        Assert.Equal("GC System", shell.Stage.Title);

        shell.SettleStage();

        Assert.False(shell.IsStagePending);
        Assert.Equal("WII System", shell.Stage.Title);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task OpeningAFolder_HoldsTheOpenFolderOnTheStage_ThenShowsTheFirstGame()
    {
        var (shell, clock) = await CreateWithClockAsync(new FakeLibrary().WithPlatform("ps2", 3));

        Assert.True(shell.DispatchGamepadAction(GamepadAction.Confirm));

        Assert.True(shell.IsFolderOpen);
        Assert.Equal(0, shell.OpenFolder!.SelectedIndex);
        Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
        Assert.True(shell.IsStagePending);

        clock.Advance(HandheldMotion.FolderOpenStageHold);
        shell.SettleStage();

        Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
        Assert.Equal("ps2 game 000", shell.Stage.Title);
    }

    [Fact]
    public async Task StageChanging_IsRaisedBeforeADifferentThing_AndNotForTheSameOne()
    {
        var (shell, clock) = await CreateWithClockAsync(new FakeLibrary().WithPlatform("ps2", 1).WithPlatform("gc", 1));
        var events = new List<string>();
        shell.StageChanging += () => events.Add("changing:" + shell.Stage.Title);
        shell.StageChanged += () => events.Add("changed:" + shell.Stage.Title);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateRight));
        shell.RefreshStatus(); // nothing on the Stage changes

        Assert.Equal(["changing:PS2 System", "changed:GC System"], events);
    }

    [Fact]
    public async Task Play_RaisesLaunchStarting_BeforeTheEmulatorStarts()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 1);
        library.PendingLaunch = new TaskCompletionSource<bool>();
        var shell = await CreateAsync(library);
        HandheldTitlePageViewModel? started = null;
        shell.LaunchStarting += page => started = page;

        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Confirm);

        Assert.NotNull(started);
        Assert.Equal(library.Games["ps2"][0].Id, started!.Entry.Id);
        Assert.Equal([library.Games["ps2"][0].Id], library.Launched);
        library.PendingLaunch.SetResult(true);
    }

    [Fact]
    public async Task TheFocusedFolder_GetsItsFirstThreeCoversToPeek()
    {
        var library = new FakeLibrary().WithPlatform("ps2", 5);
        var shell = await CreateAsync(library);

        // The fake decodes nothing, but the shell asked for exactly the first three games at peek size.
        Assert.Equal(library.Games["ps2"].Take(3).Select(game => game.Id), library.PeekRequests);
        // Through the cheap first-three read, not a view model for every game on the platform.
        Assert.Equal([("ps2", 3)], library.FirstGamesReads);
        Assert.Null(shell.Stage.PeekA);
    }
}
