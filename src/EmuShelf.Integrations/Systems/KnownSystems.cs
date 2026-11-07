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
    // Display order: systems are grouped by Manufacturer in the navigation list, and the groups
    // themselves are ordered by their oldest system — Nintendo → Sega → Sony → Arcade → Valve. Within a
    // group the order below is oldest-first (handhelds interleave with home consoles by year).
    // This authored order IS the navigation order; ids stay stable regardless of position.
    //
    // Emulator routing is orthogonal to this grouping. Notably PSP, Mega Drive, NDS and GBA share
    // one portable RetroArch installation, while each one's integration-owned reader controls when
    // a file is eligible for import; other systems' routing is noted per entry where it matters.
    // Backdrops are the animated top-screen backdrops for Handheld mode on the Thor (M45 H7), as ids into
    // EmuShelf.Rendering's BackdropCatalog; a system rotates through its list (the owner-approved picks.json of the motion lab).
    // Era (cartridge or disc) is only a look, for Handheld mode's top-screen backdrop: cartridges, game
    // cards and arcade boards are Cartridge; optical media and Steam's digital library are Disc (the default).
    // NDS can also run on standalone melonDS (release or nightly) instead — its battery saves are the
    // same raw cartridge dump either way, so they sync as one cloud entry per game.
    public static IReadOnlyList<GameSystem> All { get; } =
    [
        // ── Nintendo ──
        // NES launches through RetroArch (an FCEUmm / Nestopia / Mesen core). The North-American
        // cardboard boxes are portrait, so the placeholder frame is portrait like the disc systems;
        // a loaded cover's own dimensions replace this ratio.
        new("nes",          "Nintendo Entertainment System", "NES", "#9B7E6B", 0.72,  "Nintendo", Era: SystemEra.Cartridge, Backdrops: ["nes-flow", "nes-stars", "nes-meta", "nes-raster"]),
        // SNES box art is the wide North-American cardboard box: representative Libretro scans are
        // 512×357 (1.434), so unlike the portrait disc systems a SNES cover is short and wide. The
        // frame stays under the 266px disc shelf, bottom-aligned like the other short covers.
        new("snes",         "Super Nintendo", "SNES", "#8D66C4", 1.434, "Nintendo", Era: SystemEra.Cartridge, Backdrops: ["runway", "snes-mosaic-v2", "snes-lake-v2", "tiles"]),
        // Game Boy Color launches through RetroArch (a Game Boy core such as Gambatte). Its boxes
        // are small and roughly square, matching the Game Boy Advance frame; a loaded cover's own
        // dimensions replace this placeholder ratio.
        new("gbc",          "Game Boy Color", "GBC", "#4FAE9C", 1.0,   "Nintendo", Era: SystemEra.Cartridge, Backdrops: ["gbc-attr", "gbc-bands", "gbc-sheen-v2", "gbc-confetti-v2"]),
        new("gba",          "Game Boy Advance", "GBA", "#7065A7", 1.0, "Nintendo", Era: SystemEra.Cartridge, Backdrops: ["gba-corridor-v2", "gba-iso"]),
        new("gamecube",     "GameCube",      "GC",  "#7B68C9", 0.708, "Nintendo", Backdrops: ["gc-caustics", "gc-cubes-v3", "gc-fur", "gc-lens"]),
        // The DS renders two screens the emulator draws together in one app, so the launch-screen
        // chooser (which single physical display?) does not apply — see GameSystem.IsDualScreen.
        new("nds",          "Nintendo DS",   "DS",  "#7580B9", 1.115, "Nintendo", IsDualScreen: true, Era: SystemEra.Cartridge, Backdrops: ["ds-ripples", "ds-ripplefloor-v2", "ds-dots", "ds-flip"]),
        new("wii",          "Wii",           "Wii", "#49B3C9", 0.708, "Nintendo", Backdrops: ["wii-pointer-v2", "wii-tiles"]),
        // 3DS launches through the standalone Azahar emulator (not RetroArch). GameTDB serves its
        // 2D front covers on a fixed 768×680 canvas (1.129, near-square landscape) — measured from
        // representative coverHQ scans — so the frame matches the downloaded art with no letterbox.
        new("3ds",          "Nintendo 3DS",  "3DS", "#C0568E", 1.129, "Nintendo", IsDualScreen: true, Era: SystemEra.Cartridge, Backdrops: ["3ds-tiles", "3ds-floorwave", "3ds-tapwave"]),

        // ── Sega ──
        new("megadrive",    "Mega Drive / Genesis", "MD", "#3A6D74", 0.708, "Sega", Era: SystemEra.Cartridge, Backdrops: ["md-speed-v3", "md-planes", "md-pipe-v2"]),
        // The library's default region is US, whose Dreamcast releases use square jewel-case art.
        // Loaded PAL covers replace this with their image ratio.
        new("dreamcast",    "Dreamcast",      "DC",  "#F07C3E", 1.0,   "Sega", Backdrops: ["dc-fog-v3", "dc-sea-v2", "dc-checker"]),

        // ── Sony ──
        new("playstation",  "PlayStation",   "PS1", "#8A8FA3", 1.0,   "Sony", Backdrops: ["field", "tide", "frames", "ps1-terrace-v2", "ps1-pipe"]),
        new("playstation2", "PlayStation 2", "PS2", "#3D6DB5", 0.708, "Sony", Backdrops: ["ps2-towers", "ps2-trails-v3", "ps2-sea", "ps2-orbs-v3"]),
        new("psp",          "PSP",           "PSP", "#596EBC", 0.581, "Sony", Backdrops: ["psp-field-v2"]),
        new("playstation3", "PlayStation 3", "PS3", "#2E3A87", 0.708, "Sony", Backdrops: ["ps3-sheet", "ps3-wire", "ps3-twin"]),

        // ── Arcade ──
        // Arcade launches through RetroArch's FinalBurn Neo core. Arcade output is 4:3, and box art
        // barely exists for it, so the card is landscape and the cover is a title screen / snap
        // (see LibretroArcadeArtworkProvider) rather than portrait packaging.
        new("arcade",       "Arcade",         "ARC", "#C0473A", 1.333, "Arcade", Era: SystemEra.Cartridge, Backdrops: ["arc-vector-v3", "arc-bulbs-v2", "arc-checker", "arc-plating"]),

        // Steam library capsules are portrait; GameNative owns installation and cloud saves.
        new("steam", "Steam", "Steam", "#66C0F4", 2.0 / 3.0, "Valve", Backdrops: ["steam-wall-v2", "steam-mesh", "steam-grid-v2"]),
    ];
}
