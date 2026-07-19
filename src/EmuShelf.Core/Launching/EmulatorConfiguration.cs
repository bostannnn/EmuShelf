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
    /// <summary>Stable integration id of the selected emulator.</summary>
    public string? EmulatorId { get; init; }

    /// <summary>
    /// Stable user-data id for the executable installation. Several system configurations may
    /// point at the same id. It is deliberately not a filesystem path.
    /// </summary>
    public string? EmulatorInstallationId { get; init; }

    /// <summary>Optional per-system Libretro core path for core-aware launchers.</summary>
    public string? CorePath { get; init; }
}
