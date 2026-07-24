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
        (GamepadButtons.LeftShoulder, GamepadAction.PreviousPlatform),
        (GamepadButtons.RightShoulder, GamepadAction.NextPlatform),
    ];

    private readonly long _initialRepeatDelayMs;
    private readonly long _repeatIntervalMs;
    private readonly Dictionary<GamepadAction, long> _directionNextRepeat = new();

    private GamepadButtons _previousButtons;
    private bool _wasConnected;

    public GamepadNavigationController(long initialRepeatDelayMs = 400, long repeatIntervalMs = 110)
    {
        _initialRepeatDelayMs = initialRepeatDelayMs;
        _repeatIntervalMs = repeatIntervalMs;
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
            return [];
        }

        var actions = new List<GamepadAction>();

        foreach (var (button, action) in PressActions)
        {
            if (reading.Buttons.HasFlag(button) && !_previousButtons.HasFlag(button))
                actions.Add(action);
        }

        HandleDirection(IsUp(reading), GamepadAction.NavigateUp, timestampMs, actions);
        HandleDirection(IsDown(reading), GamepadAction.NavigateDown, timestampMs, actions);
        HandleDirection(IsLeft(reading), GamepadAction.NavigateLeft, timestampMs, actions);
        HandleDirection(IsRight(reading), GamepadAction.NavigateRight, timestampMs, actions);

        _previousButtons = reading.Buttons;
        return actions;
    }

    private void HandleDirection(bool active, GamepadAction action, long timestampMs, List<GamepadAction> actions)
    {
        if (!active)
        {
            _directionNextRepeat.Remove(action);
            return;
        }

        if (!_directionNextRepeat.TryGetValue(action, out var nextRepeat))
        {
            // Fresh press: fire now, schedule the first repeat after the initial delay.
            actions.Add(action);
            _directionNextRepeat[action] = timestampMs + _initialRepeatDelayMs;
            return;
        }

        if (timestampMs >= nextRepeat)
        {
            actions.Add(action);
            _directionNextRepeat[action] = timestampMs + _repeatIntervalMs;
        }
    }

    private void Reset()
    {
        _wasConnected = false;
        _previousButtons = GamepadButtons.None;
        _directionNextRepeat.Clear();
    }

    private static bool IsUp(GamepadReading reading) =>
        reading.Buttons.HasFlag(GamepadButtons.DpadUp) || reading.LeftStickY < -StickDeadZone;

    private static bool IsDown(GamepadReading reading) =>
        reading.Buttons.HasFlag(GamepadButtons.DpadDown) || reading.LeftStickY > StickDeadZone;

    private static bool IsLeft(GamepadReading reading) =>
        reading.Buttons.HasFlag(GamepadButtons.DpadLeft) || reading.LeftStickX < -StickDeadZone;

    private static bool IsRight(GamepadReading reading) =>
        reading.Buttons.HasFlag(GamepadButtons.DpadRight) || reading.LeftStickX > StickDeadZone;
}
