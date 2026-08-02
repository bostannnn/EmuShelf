using EmuShelf.Core.Systems;

namespace EmuShelf.Integrations.Systems;

/// <summary>
/// The systems supported in version 1. Ids are stable and stored in the
/// library database; never change them once released.
/// </summary>
public static class KnownSystems
{
    // CoverAspectRatio (width:height) is the placeholder/default frame. Once a real cover is
    // loaded its own dimensions take precedence, so regional packaging (notably Dreamcast's
    // square US/Japanese jewel cases and portrait PAL keep cases) is never cropped to fit a
    // system-wide assumption.
    public static IReadOnlyList<GameSystem> All { get; } =
    [
        new("playstation",  "PlayStation",   "PS1", "#8A8FA3", 1.0),
        new("playstation2", "PlayStation 2", "PS2", "#3D6DB5", 0.708),
        new("playstation3", "PlayStation 3", "PS3", "#2E3A87", 0.708),
        new("gamecube",     "GameCube",      "GC",  "#7B68C9", 0.708),
        new("wii",          "Wii",           "Wii", "#49B3C9", 0.708),
        // These systems share their portable RetroArch installation, while each one's
        // integration-owned reader controls when a file is eligible for import.
        new("psp",          "PSP",           "PSP", "#596EBC", 0.581),
        new("megadrive",    "Mega Drive / Genesis", "MD", "#3A6D74", 0.708),
        new("nds",          "Nintendo DS",   "DS",  "#7580B9", 1.115),
        new("gba",          "Game Boy Advance", "GBA", "#7065A7", 1.0),
        // NES launches through RetroArch (an FCEUmm / Nestopia / Mesen core). The North-American
        // cardboard boxes are portrait, so the placeholder frame is portrait like the disc systems;
        // a loaded cover's own dimensions replace this ratio.
        new("nes",          "Nintendo Entertainment System", "NES", "#9B7E6B", 0.72),
        // SNES box art is the wide North-American cardboard box: representative Libretro scans are
        // 512×357 (1.434), so unlike the portrait disc systems a SNES cover is short and wide. The
        // frame stays under the 266px disc shelf, bottom-aligned like the other short covers.
        new("snes",         "Super Nintendo", "SNES", "#8D66C4", 1.434),
        // The library's default region is US, whose Dreamcast releases use square jewel-case art.
        // Loaded PAL covers replace this with their image ratio.
        new("dreamcast",    "Dreamcast",      "DC",  "#F07C3E", 1.0),
        // Arcade launches through RetroArch's FinalBurn Neo core. Arcade output is 4:3, and box art
        // barely exists for it, so the card is landscape and the cover is a title screen / snap
        // (see LibretroArcadeArtworkProvider) rather than portrait packaging.
        new("arcade",       "Arcade",         "ARC", "#C0473A", 1.333),
        // Game Boy Color launches through RetroArch (a Game Boy core such as Gambatte). Its boxes
        // are small and roughly square, matching the Game Boy Advance frame; a loaded cover's own
        // dimensions replace this placeholder ratio.
        new("gbc",          "Game Boy Color", "GBC", "#4FAE9C", 1.0),
    ];
}
