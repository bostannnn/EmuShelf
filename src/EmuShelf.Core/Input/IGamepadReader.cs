namespace EmuShelf.Core.Input;

/// <summary>
/// Digital controller inputs, normalized to the standard face/shoulder/d-pad layout regardless of
/// the physical controller. Used as a flags set so one reading reports every button held this frame.
/// </summary>
[Flags]
public enum GamepadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftShoulder = 1 << 4,
    RightShoulder = 1 << 5,
    DpadUp = 1 << 6,
    DpadDown = 1 << 7,
    DpadLeft = 1 << 8,
    DpadRight = 1 << 9,
}

/// <summary>
/// A single polled snapshot of a controller: which buttons are held plus the left-stick position
/// (each axis in the range -1..1). <see cref="IsConnected"/> is false when no controller is present.
/// </summary>
public readonly record struct GamepadReading(
    GamepadButtons Buttons,
    float LeftStickX,
    float LeftStickY,
    bool IsConnected)
{
    public static GamepadReading Disconnected { get; } = new(GamepadButtons.None, 0f, 0f, false);
}

/// <summary>
/// Reads a physical controller. Implementations are platform-specific and must degrade gracefully:
/// when no native backend or controller is available they report <see cref="IsAvailable"/> false and
/// return <see cref="GamepadReading.Disconnected"/> rather than throwing, so the UI keeps working on
/// keyboard/Steam Input.
/// </summary>
public interface IGamepadReader
{
    /// <summary>Whether a native controller backend initialized successfully.</summary>
    bool IsAvailable { get; }

    /// <summary>Polls the current controller state. Never throws.</summary>
    GamepadReading Read();
}
