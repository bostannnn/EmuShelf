using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.Rpcs3;

public static class Rpcs3Definition
{
    public static EmulatorDefinition Instance { get; } = new(
        "rpcs3",
        "RPCS3",
        ["playstation3"],
        "--no-gui \"{GamePath}\"");
}
