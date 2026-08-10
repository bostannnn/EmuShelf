using EmuShelf.Core.Input;

namespace EmuShelf.App.Services;

/// <summary>
/// Translates a stream of polled <see cref="GamepadReading"/> snapshots into discrete
/// <see cref="GamepadAction"/> commands. Face/shoulder buttons fire once per press (edge-triggered);
/// directional input (d-pad or left stick past a dead zone) fires on press and then auto-repeats
/// while held, like a menu. Pure and time-driven so it is fully unit-testable without native input.
/// </summary>
public sealed class GamepadNavigationController
{
    private const float StickDeadZone = 0.5f;

    private static readonly (GamepadButtons Button, GamepadAction Action)[] PressActions =
    [
        (GamepadButtons.A, GamepadAction.Confirm),
        (GamepadButtons.B, GamepadAction.Cancel),
        (GamepadButtons.X, GamepadAction.Search),
        (GamepadButtons.Y, GamepadAction.Actions),
        (GamepadButtons.Start, GamepadAction.Menu),
        (GamepadButtons.LeftShoulder, GamepadAction.PreviousPlatform),
        (GamepadButtons.RightShoulder, GamepadAction.NextPlatform),
        // Edge-triggered like every other button, so holding R3 recentres once rather than
        // fighting the stick every tick.
        (GamepadButtons.RightStick, GamepadAction.ResetRotation),
    ];

    private readonly long _initialRepeatDelayMs;
    private readonly long _repeatStartIntervalMs;
    private readonly long _minRepeatIntervalMs;
    private readonly long _verticalMinRepeatIntervalMs;
    private readonly int _rampRepeats;
    private readonly Dictionary<GamepadAction, long> _directionNextRepeat = new();
    private readonly Dictionary<GamepadAction, int> _directionRepeatCount = new();

    private GamepadButtons _previousButtons;
    private bool _wasConnected;

    /// <param name="initialRepeatDelayMs">Delay before the first auto-repeat once a direction is held.</param>
    /// <param name="repeatIntervalMs">Interval before the first repeat; a held direction accelerates from here.</param>
    /// <param name="minRepeatIntervalMs">Fastest interval the acceleration ramp reaches on a long hold,
    /// for the horizontal (Left/Right) directions, which move within a row and need no scroll.</param>
    /// <param name="rampRepeats">Repeats taken to ramp from <paramref name="repeatIntervalMs"/> down to the floor.</param>
    /// <param name="verticalMinRepeatIntervalMs">Fastest interval for the vertical (Up/Down) directions.
    /// Deliberately gentler than the horizontal floor: each vertical step scrolls a whole row, and a very
    /// fast vertical hold outruns the grid's row virtualization/centre-reveal, which then reads as rows
    /// blinking and jumping. Horizontal stays snappy.</param>
    public GamepadNavigationController(
        long initialRepeatDelayMs = 320,
        long repeatIntervalMs = 90,
        long minRepeatIntervalMs = 38,
        int rampRepeats = 8,
        long verticalMinRepeatIntervalMs = 72)
    {
        _initialRepeatDelayMs = initialRepeatDelayMs;
        _repeatStartIntervalMs = repeatIntervalMs;
        _minRepeatIntervalMs = minRepeatIntervalMs;
        _verticalMinRepeatIntervalMs = verticalMinRepeatIntervalMs;
        _rampRepeats = Math.Max(1, rampRepeats);
    }

    /// <summary>Advances state to <paramref name="reading"/> at <paramref name="timestampMs"/> and returns the actions to fire this tick.</summary>
    public IReadOnlyList<GamepadAction> Poll(GamepadReading reading, long timestampMs)
    {
        if (!reading.IsConnected)
        {
            Reset();
            return [];
        }

        // Swallow the first frame after a controller (re)connects so buttons already held at connect
        // time don't register as fresh presses.
        if (!_wasConnected)
        {
            _wasConnected = true;
            _previousButtons = reading.Buttons;
            _directionNextRepeat.Clear();
            _directionRepeatCount.Clear();
            return [];
        }

        var actions = new List<GamepadAction>();

        foreach (var (button, action) in PressActions)
        {
            if (reading.Buttons.HasFlag(button) && !_previousButtons.HasFlag(button))
                actions.Add(action);
        }

        var stick = StickDirections(reading);
        HandleDirection(reading.Buttons.HasFlag(GamepadButtons.DpadUp) || stick.Up, GamepadAction.NavigateUp, timestampMs, actions);
        HandleDirection(reading.Buttons.HasFlag(GamepadButtons.DpadDown) || stick.Down, GamepadAction.NavigateDown, timestampMs, actions);
        HandleDirection(reading.Buttons.HasFlag(GamepadButtons.DpadLeft) || stick.Left, GamepadAction.NavigateLeft, timestampMs, actions);
        HandleDirection(reading.Buttons.HasFlag(GamepadButtons.DpadRight) || stick.Right, GamepadAction.NavigateRight, timestampMs, actions);

        _previousButtons = reading.Buttons;
        return actions;
    }

    private void HandleDirection(bool active, GamepadAction action, long timestampMs, List<GamepadAction> actions)
    {
        if (!active)
        {
            _directionNextRepeat.Remove(action);
            _directionRepeatCount.Remove(action);
            return;
        }

        if (!_directionNextRepeat.TryGetValue(action, out var nextRepeat))
        {
            // Fresh press: fire now, schedule the first repeat after the initial delay.
            actions.Add(action);
            _directionNextRepeat[action] = timestampMs + _initialRepeatDelayMs;
            _directionRepeatCount[action] = 0;
            return;
        }

        if (timestampMs >= nextRepeat)
        {
            actions.Add(action);
            // A held direction accelerates: each repeat's interval ramps from the start interval down
            // to the floor over _rampRepeats steps, so a long hold glides toward the target instead of
            // crawling at one fixed cadence.
            var count = _directionRepeatCount[action] = _directionRepeatCount.GetValueOrDefault(action) + 1;
            var progress = Math.Min(1.0, count / (double)_rampRepeats);
            // Vertical steps scroll a whole row, so they ramp to a gentler floor than horizontal.
            var floor = action is GamepadAction.NavigateUp or GamepadAction.NavigateDown
                ? _verticalMinRepeatIntervalMs
                : _minRepeatIntervalMs;
            var interval = (long)Math.Round(
                _repeatStartIntervalMs + (floor - _repeatStartIntervalMs) * progress);
            _directionNextRepeat[action] = timestampMs + interval;
        }
    }

    /// <summary>
    /// Drops the current controller state. The next connected reading is intentionally swallowed,
    /// so an input held while another application owned the foreground cannot become an EmuShelf
    /// command when the frontend returns.
    /// </summary>
    public void Reset()
    {
        _wasConnected = false;
        _previousButtons = GamepadButtons.None;
        _directionNextRepeat.Clear();
        _directionRepeatCount.Clear();
    }

    /// <summary>
    /// Resolves the left stick to at most one axis. When a diagonal push crosses the dead zone on
    /// both axes, only the larger-magnitude one survives, so one flick steps a single grid cell
    /// instead of jumping a row and a column at once. The d-pad is handled separately and unchanged.
    /// </summary>
    private static (bool Up, bool Down, bool Left, bool Right) StickDirections(GamepadReading reading)
    {
        var x = reading.LeftStickX;
        var y = reading.LeftStickY;
        var horizontal = Math.Abs(x) > StickDeadZone;
        var vertical = Math.Abs(y) > StickDeadZone;
        if (horizontal && vertical)
        {
            if (Math.Abs(x) >= Math.Abs(y))
                vertical = false;
            else
                horizontal = false;
        }

        return (vertical && y < 0, vertical && y > 0, horizontal && x < 0, horizontal && x > 0);
    }
}
