using EmuShelf.Core.Systems;

namespace EmuShelf.Integrations.Systems;

/// <summary>
/// The systems supported in version 1. Ids are stable and stored in the
/// library database; never change them once released.
/// </summary>
public static class KnownSystems
{
    // CoverAspectRatio (width:height) is the one canonical frame every cover of a system
    // is drawn into — real art and the "artwork missing" placeholder alike — so a
    // platform's covers are uniform in size. PlayStation ships square CD jewel-case art
    // (1.0); disc-case systems are portrait (0.708, the measured mode of the scanned box
    // art, ≈ the physical DVD/BD case). Real covers fill this frame, so matching it to the
    // real scan ratio keeps the crop to a hair of bleed. Tunable per platform.
    public static IReadOnlyList<GameSystem> All { get; } =
    [
        new("playstation",  "PlayStation",   "PS1", "#8A8FA3", 1.0),
        new("playstation2", "PlayStation 2", "PS2", "#3D6DB5", 0.708),
        new("playstation3", "PlayStation 3", "PS3", "#2E3A87", 0.708),
        new("gamecube",     "GameCube",      "GC",  "#7B68C9", 0.708),
        new("wii",          "Wii",           "Wii", "#49B3C9", 0.708),
        // These systems are present in navigation and launcher configuration before their
        // strict, format-verified import milestones. Do not add import extensions here.
        new("psp",          "PSP",           "PSP", "#596EBC", 0.708),
        new("megadrive",    "Mega Drive / Genesis", "MD", "#3A6D74", 0.708),
        new("nds",          "Nintendo DS",   "DS",  "#7580B9", 0.708),
        new("gba",          "Game Boy Advance", "GBA", "#7065A7", 0.708),
    ];
}
