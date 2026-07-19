using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>
/// PPSSPP launches the M14 PSP ISO/CSO import profile with the game path as one argv entry.
/// </summary>
public static class PpssppDefinition
{
    public static EmulatorDefinition Instance { get; } = new(
        "ppsspp",
        "PPSSPP",
        ["psp"],
        "\"{GamePath}\"");
}
