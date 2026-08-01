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
            Profile("arcade"),
            Profile("gbc", ".gb", ".gbc"),
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
