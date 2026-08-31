using EmuShelf.Core.Launching;
using EmuShelf.Integrations.Emulators.Azahar;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.MelonDs;
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
        // Both melonDS channels serve Nintendo DS alongside RetroArch. They are listed after it on
        // purpose: the first emulator supporting a system is what an install that never picked one
        // falls back to, so RetroArch stays the DS default exactly as before.
        MelonDsDefinition.Instance,
        MelonDsDefinition.Nightly,
    ];
}
