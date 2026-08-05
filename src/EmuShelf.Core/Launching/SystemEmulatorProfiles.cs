namespace EmuShelf.Core.Launching;

/// <summary>
/// Every configured emulator "profile" for one game system, plus which one is active. A profile is a
/// (system, emulator) pairing that owns its own launch arguments, installation, and core; the active
/// profile is the one EmuShelf launches, syncs saves for, and shows first in Settings.
/// </summary>
/// <param name="SystemId">The stable system id these profiles belong to.</param>
/// <param name="ActiveEmulatorId">
/// The emulator id the user selected for this system, or null when the system has never been
/// configured. It may name a profile that has no stored configuration yet (the user picked the
/// emulator but has not filled in its executable/core).
/// </param>
/// <param name="Configurations">
/// Every stored (system, emulator) configuration, in no particular order. A system with a single
/// supported emulator has at most one entry.
/// </param>
public sealed record SystemEmulatorProfiles(
    string SystemId,
    string? ActiveEmulatorId,
    IReadOnlyList<EmulatorConfiguration> Configurations)
{
    /// <summary>The active profile's configuration, or null when the active profile is unconfigured.</summary>
    public EmulatorConfiguration? Active =>
        ActiveEmulatorId is null
            ? null
            : Configurations.FirstOrDefault(configuration =>
                string.Equals(configuration.EmulatorId, ActiveEmulatorId, StringComparison.Ordinal));

    /// <summary>The stored configuration for one emulator, or null when it has none.</summary>
    public EmulatorConfiguration? ForEmulator(string emulatorId) =>
        Configurations.FirstOrDefault(configuration =>
            string.Equals(configuration.EmulatorId, emulatorId, StringComparison.Ordinal));
}
