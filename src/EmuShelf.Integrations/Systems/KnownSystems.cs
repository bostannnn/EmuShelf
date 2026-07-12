using EmuShelf.Core.Systems;

namespace EmuShelf.Integrations.Systems;

/// <summary>
/// The systems supported in version 1. Ids are stable and stored in the
/// library database; never change them once released.
/// </summary>
public static class KnownSystems
{
    public static IReadOnlyList<GameSystem> All { get; } =
    [
        new("playstation",  "PlayStation",   "PS1", "#8A8FA3"),
        new("playstation2", "PlayStation 2", "PS2", "#3D6DB5"),
        new("playstation3", "PlayStation 3", "PS3", "#2E3A87"),
        new("gamecube",     "GameCube",      "GC",  "#7B68C9"),
        new("wii",          "Wii",           "Wii", "#49B3C9"),
    ];
}
