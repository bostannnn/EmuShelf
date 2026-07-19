using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>
/// PPSSPP is registered for the forthcoming PSP importer. PSP formats are intentionally not
/// recognized until M14 proves the import and launch contract with a chosen PPSSPP release.
/// </summary>
public static class PpssppDefinition
{
    public static EmulatorDefinition Instance { get; } = new(
        "ppsspp",
        "PPSSPP",
        ["psp"],
        "\"{GamePath}\"");
}
