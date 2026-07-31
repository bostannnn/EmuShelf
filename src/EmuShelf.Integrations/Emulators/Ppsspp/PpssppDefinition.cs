using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>
/// PPSSPP launches the PSP ISO/CSO/CHD import profile with the game path as one argv entry.
/// Every container is passed the same way, so CHD needs no launch-argument handling of its own.
/// </summary>
public static class PpssppDefinition
{
    public static EmulatorDefinition Instance { get; } = new(
        "ppsspp",
        "PPSSPP",
        ["psp"],
        "\"{GamePath}\"");
}
