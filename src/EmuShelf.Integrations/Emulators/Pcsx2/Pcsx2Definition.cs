using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Pcsx2;

public static class Pcsx2Definition
{
    public static EmulatorDefinition Instance { get; } = new(
        "pcsx2",
        "PCSX2",
        ["playstation2"],
        "-batch -- \"{GamePath}\"")
    {
        ReleaseSource = EmulatorReleaseSources.Pcsx2,
    };
}
