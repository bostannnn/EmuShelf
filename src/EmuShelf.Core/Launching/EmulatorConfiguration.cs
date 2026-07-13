namespace EmuShelf.Core.Launching;

/// <summary>User-editable emulator configuration for one game system.</summary>
public sealed record EmulatorConfiguration(
    string SystemId,
    string? ExecutablePath,
    string? LaunchArguments);
