namespace EmuShelf.Core.Launching;

/// <summary>
/// User-editable launcher configuration for one game system. The executable belongs to a
/// named installation so compatible systems can share it while retaining their own arguments
/// and (where required) a system-specific RetroArch core.
/// </summary>
public sealed record EmulatorConfiguration(
    string SystemId,
    string? ExecutablePath,
    string? LaunchArguments)
{
    /// <summary>
    /// The selected shared installation target. <see cref="ExecutablePath"/> remains as a
    /// backwards-compatible direct-target projection while old settings are migrated.
    /// </summary>
    public EmulatorLaunchTarget? LaunchTarget { get; init; }

    /// <summary>Stable integration id of the selected emulator.</summary>
    public string? EmulatorId { get; init; }

    /// <summary>
    /// Stable user-data id for the executable installation. Several system configurations may
    /// point at the same id. It is deliberately not a filesystem path.
    /// </summary>
    public string? EmulatorInstallationId { get; init; }

    /// <summary>Optional per-system Libretro core path for core-aware launchers.</summary>
    public string? CorePath { get; init; }

    /// <summary>
    /// Which screen this system launches on when the device has a second display (the Thor). Defaults
    /// to <see cref="GameLaunchScreen.Ask"/> — prompt once, with a "remember" choice that pins it. Only
    /// meaningful on the Android head with an external display present; ignored everywhere else.
    /// </summary>
    public GameLaunchScreen LaunchScreen { get; init; } = GameLaunchScreen.Ask;
}
