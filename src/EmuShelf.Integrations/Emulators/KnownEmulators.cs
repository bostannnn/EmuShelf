using EmuShelf.Core.Launching;
using EmuShelf.Integrations.Emulators.Azahar;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;
using EmuShelf.Integrations.Emulators.RetroArch;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.Integrations.Emulators;

public static class KnownEmulators
{
    public static IReadOnlyList<EmulatorDefinition> All { get; } =
    [
        DuckStationDefinition.Instance,
        Pcsx2Definition.Instance,
        Rpcs3Definition.Instance,
        DolphinDefinition.Instance,
        PpssppDefinition.Instance,
        AzaharDefinition.Instance,
        RetroArchDefinition.Instance,
    ];
}
