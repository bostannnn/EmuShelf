using EmuShelf.Core.Metadata.ScreenScraper;

namespace EmuShelf.Integrations.Metadata;

public static class KnownScreenScraperFingerprintProfiles
{
    private static readonly IReadOnlyDictionary<string, ScreenScraperFingerprintProfile> Profiles =
        new[]
        {
            Profile("playstation", ".bin", ".iso", ".img"),
            Profile("playstation2", ".iso", ".bin", ".img"),
            Profile("playstation3"),
            Profile("psp", ".iso"),
            Profile("gamecube", ".iso", ".gcm"),
            Profile("wii", ".iso"),
            Profile("megadrive", ".md", ".gen", ".bin"),
            Profile("nds", ".nds"),
            Profile("gba", ".gba"),
            Profile("snes", ".sfc", ".smc"),
            Profile("dreamcast"),
            // Arcade has no whole-file hash (a repacked set archive isn't byte-stable); it matches by
            // ROM file name instead — see ScreenScraperPreviewService.FileNameMatchSystems.
            Profile("arcade"),
            Profile("gbc", ".gb", ".gbc"),
            Profile("nes", ".nes"),
            // A clean NCSD cartridge dump (.3ds/.cci — the same CTR card image, only the extension
            // differs) is the exact file No-Intro catalogues and ScreenScraper indexes by whole-file
            // hash, so it matches like the other No-Intro cartridge sets above. The installable,
            // single-title, homebrew, and compressed 3DS formats (.cia/.cxi/.app/.3dsx/.z*) are not
            // that dump — their whole-file hash is never in the catalogue — so they are deliberately
            // excluded and fall back to filename/title search instead.
            Profile("3ds", ".3ds", ".cci"),
        }.ToDictionary(profile => profile.SystemId, StringComparer.OrdinalIgnoreCase);

    public static bool TryGet(
        string emuShelfSystemId,
        out ScreenScraperFingerprintProfile? profile) =>
        Profiles.TryGetValue(emuShelfSystemId, out profile);

    private static ScreenScraperFingerprintProfile Profile(string systemId, params string[] extensions) =>
        new(
            systemId,
            new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase));
}
