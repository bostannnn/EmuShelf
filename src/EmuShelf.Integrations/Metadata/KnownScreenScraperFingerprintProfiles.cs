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
            // 3DS images (.3ds/.cci/.cia) are not canonically whole-file hashable, so — like the
            // disc-id/title-id systems above — it has no hash route and falls back to title search
            // until a validated 3DS fingerprint rule lands.
            Profile("3ds"),
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
