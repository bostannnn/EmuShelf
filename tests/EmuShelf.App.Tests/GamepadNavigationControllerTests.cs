using EmuShelf.App.Services;
using EmuShelf.Core.Input;

namespace EmuShelf.App.Tests;

public class GamepadNavigationControllerTests
{
    private static GamepadReading Connected(GamepadButtons buttons = GamepadButtons.None, float x = 0f, float y = 0f) =>
        new(buttons, x, y, true);

    private readonly GamepadNavigationController _controller = new(initialRepeatDelayMs: 400, repeatIntervalMs: 100);

    [Fact]
    public void Poll_FirstConnectedFrame_IsSwallowedSoHeldButtonsDoNotFire()
    {
        // A button already held at connect time must not register as a fresh press.
        var actions = _controller.Poll(Connected(GamepadButtons.A), 0);

        Assert.Empty(actions);
    }

    [Fact]
    public void Poll_FaceButton_FiresOncePerPressWithNoRepeatWhileHeld()
    {
        _controller.Poll(Connected(), 0);

        Assert.Equal([GamepadAction.Confirm], _controller.Poll(Connected(GamepadButtons.A), 16));
        Assert.Empty(_controller.Poll(Connected(GamepadButtons.A), 32));
        Assert.Empty(_controller.Poll(Connected(GamepadButtons.A), 1000));
    }

    [Fact]
    public void Poll_FaceButton_FiresAgainAfterReleaseAndRepress()
    {
        _controller.Poll(Connected(), 0);
        _controller.Poll(Connected(GamepadButtons.A), 16);
        _controller.Poll(Connected(), 32); // release

        Assert.Equal([GamepadAction.Confirm], _controller.Poll(Connected(GamepadButtons.A), 48));
    }

    [Fact]
    public void Poll_MapsEveryButtonToItsDocumentedAction()
    {
        _controller.Poll(Connected(), 0);

        Assert.Equal([GamepadAction.Confirm, GamepadAction.Cancel],
            _controller.Poll(Connected(GamepadButtons.A | GamepadButtons.B), 16));
        _controller.Poll(Connected(), 32);
        Assert.Equal([GamepadAction.Search], _controller.Poll(Connected(GamepadButtons.X), 48));
        _controller.Poll(Connected(), 64);
        Assert.Equal([GamepadAction.Actions], _controller.Poll(Connected(GamepadButtons.Y), 80));
        _controller.Poll(Connected(), 96);
        Assert.Equal([GamepadAction.Menu], _controller.Poll(Connected(GamepadButtons.Start), 112));
        _controller.Poll(Connected(), 128);
        Assert.Equal([GamepadAction.PreviousPlatform], _controller.Poll(Connected(GamepadButtons.LeftShoulder), 144));
        _controller.Poll(Connected(), 160);
        Assert.Equal([GamepadAction.NextPlatform], _controller.Poll(Connected(GamepadButtons.RightShoulder), 176));
    }

    [Fact]
    public void Poll_DpadDirection_FiresImmediatelyThenAutoRepeatsAfterTheInitialDelay()
    {
        _controller.Poll(Connected(), 0);

        Assert.Equal([GamepadAction.NavigateDown], _controller.Poll(Connected(GamepadButtons.DpadDown), 0));
        Assert.Empty(_controller.Poll(Connected(GamepadButtons.DpadDown), 399)); // before initial repeat delay
        Assert.Equal([GamepadAction.NavigateDown], _controller.Poll(Connected(GamepadButtons.DpadDown), 400));
    }

    [Fact]
    public void Poll_HorizontalHeld_AutoRepeatAcceleratesToTheFastFloor()
    {
        // A long hold should ramp from the start interval down to the floor and never overshoot it,
        // so navigating a big library glides instead of crawling at one fixed cadence. Left/Right moves
        // within a row (no scroll), so it reaches the fast floor.
        var controller = new GamepadNavigationController(
            initialRepeatDelayMs: 300, repeatIntervalMs: 90, minRepeatIntervalMs: 40, rampRepeats: 5,
            verticalMinRepeatIntervalMs: 72);
        controller.Poll(Connected(), 0);

        var fireTimes = new List<long>();
        for (long t = 0; t <= 3000 && fireTimes.Count < 12; t++)
        {
            if (controller.Poll(Connected(GamepadButtons.DpadRight), t).Contains(GamepadAction.NavigateRight))
                fireTimes.Add(t);
        }

        // Immediate press, then the first repeat waits the full initial delay.
        Assert.Equal(0, fireTimes[0]);
        Assert.Equal(300, fireTimes[1] - fireTimes[0]);

        var gaps = new List<long>();
        for (var i = 2; i < fireTimes.Count; i++)
            gaps.Add(fireTimes[i] - fireTimes[i - 1]);

        // Gaps shrink monotonically toward the floor and then hold at it.
        for (var i = 1; i < gaps.Count; i++)
            Assert.True(gaps[i] <= gaps[i - 1], $"repeat gap {gaps[i]} should not exceed the previous {gaps[i - 1]}");
        Assert.Equal(40, gaps[^1]);
    }

    [Fact]
    public void Poll_VerticalHeld_RampsToAGentlerFloorThanHorizontal()
    {
        // Up/Down scrolls a whole row, so a held vertical direction ramps to a higher (gentler) floor
        // than horizontal — a too-fast vertical hold outruns the grid's row virtualization/centre-reveal
        // and reads as rows blinking and jumping.
        var controller = new GamepadNavigationController(
            initialRepeatDelayMs: 300, repeatIntervalMs: 90, minRepeatIntervalMs: 40, rampRepeats: 5,
            verticalMinRepeatIntervalMs: 72);
        controller.Poll(Connected(), 0);

        var fireTimes = new List<long>();
        for (long t = 0; t <= 4000 && fireTimes.Count < 12; t++)
        {
            if (controller.Poll(Connected(GamepadButtons.DpadDown), t).Contains(GamepadAction.NavigateDown))
                fireTimes.Add(t);
        }

        var lastGap = fireTimes[^1] - fireTimes[^2];
        Assert.Equal(72, lastGap);       // reaches the gentler vertical floor
        Assert.True(lastGap > 40, "vertical should settle slower than the horizontal floor");
    }

    [Fact]
    public void Poll_LeftStick_ActsAsDirectionOnlyBeyondDeadZone()
    {
        _controller.Poll(Connected(), 0);

        Assert.Empty(_controller.Poll(Connected(x: 0.3f), 16));                       // inside dead zone
        Assert.Equal([GamepadAction.NavigateRight], _controller.Poll(Connected(x: 0.9f), 32));
        _controller.Poll(Connected(), 48);
        Assert.Equal([GamepadAction.NavigateUp], _controller.Poll(Connected(y: -0.9f), 64)); // up is negative Y
    }

    [Fact]
    public void Poll_DiagonalLeftStick_FiresOnlyTheDominantAxis()
    {
        _controller.Poll(Connected(), 0);

        // A diagonal push past the dead zone on both axes resolves to the larger one, so one flick
        // moves a single grid cell instead of a row and a column at once.
        Assert.Equal([GamepadAction.NavigateRight], _controller.Poll(Connected(x: 0.9f, y: -0.6f), 16));
        _controller.Poll(Connected(), 32);
        Assert.Equal([GamepadAction.NavigateDown], _controller.Poll(Connected(x: -0.6f, y: 0.9f), 48));
    }

    [Fact]
    public void Poll_Disconnect_ResetsStateAndSuppressesPhantomPressOnReconnect()
    {
        _controller.Poll(Connected(), 0);
        _controller.Poll(Connected(GamepadButtons.A), 16);

        Assert.Empty(_controller.Poll(GamepadReading.Disconnected, 32));
        // Reconnect while A is still held: the first frame is swallowed, so no phantom Confirm.
        Assert.Empty(_controller.Poll(Connected(GamepadButtons.A), 48));
        Assert.Empty(_controller.Poll(Connected(GamepadButtons.A), 64));
    }

    [Fact]
    public void Reset_AfterExternalGameSession_SwallowsTheButtonStillHeldOnReturn()
    {
        _controller.Poll(Connected(), 0);
        _controller.Poll(Connected(GamepadButtons.B), 16);

        // The emulator was closed with B while EmuShelf was minimized. Returning to the frontend
        // must not treat that still-held button as its Desktop-mode Cancel command.
        _controller.Reset();

        Assert.Empty(_controller.Poll(Connected(GamepadButtons.B), 32));
        Assert.Empty(_controller.Poll(Connected(GamepadButtons.B), 48));
    }
}
