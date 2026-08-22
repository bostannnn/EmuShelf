namespace EmuShelf.Core.Launching;

/// <summary>
/// One item in the per-system emulator picker. Standalone emulators carry only an
/// <see cref="EmulatorId"/>; a RetroArch core is presented as its own item and carries the same
/// emulator id plus the exact <see cref="CorePath"/> used for launch. The pair maps directly onto
/// <see cref="EmulatorConfiguration"/>, so changing the picker model needs no storage migration.
/// </summary>
public sealed record EmulatorChoice(
    string Id,
    string DisplayName,
    string EmulatorId,
    string? CoreId = null,
    string? CorePath = null)
{
    /// <summary>Whether this item represents the persisted emulator/core pair.</summary>
    public bool Matches(string? emulatorId, string? corePath)
    {
        if (!string.Equals(EmulatorId, emulatorId, StringComparison.Ordinal))
            return false;

        var expectedCore = string.IsNullOrWhiteSpace(CorePath) ? null : CorePath.Trim();
        var configuredCore = string.IsNullOrWhiteSpace(corePath) ? null : corePath.Trim();
        return string.Equals(expectedCore, configuredCore, StringComparison.OrdinalIgnoreCase);
    }
}
