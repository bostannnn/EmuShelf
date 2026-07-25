using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.RetroArch;

/// <summary>
/// A core-aware RetroArch launcher. It never discovers, downloads, or configures cores: each
/// supported system supplies an explicit existing core path through its launch configuration.
/// </summary>
public static class RetroArchDefinition
{
    public static EmulatorDefinition Instance { get; } = new(
        "retroarch",
        "RetroArch",
        ["megadrive", "nds", "gba", "snes", "dreamcast"],
        "-L \"{CorePath}\" \"{GamePath}\"",
        RequiresCorePath: true,
        SharesDefaultInstallation: true,
        RequiresContentFile: true);
}
