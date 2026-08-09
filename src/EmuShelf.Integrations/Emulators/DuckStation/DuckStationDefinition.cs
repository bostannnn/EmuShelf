using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.DuckStation;

public static class DuckStationDefinition
{
    public static EmulatorDefinition Instance { get; } = new(
        "duckstation",
        "DuckStation",
        ["playstation"],
        "-batch -- \"{GamePath}\"")
    {
        ReleaseSource = EmulatorReleaseSources.DuckStation,
    };
}
