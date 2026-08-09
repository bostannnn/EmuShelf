using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Dolphin;

public static class DolphinDefinition
{
    public static EmulatorDefinition Instance { get; } = new(
        "dolphin",
        "Dolphin",
        ["gamecube", "wii"],
        "-b -e \"{GamePath}\"")
    {
        ReleaseSource = EmulatorReleaseSources.Dolphin,
    };
}
