namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Explicit boundary between stable EmuShelf system IDs and ScreenScraper's numeric IDs.
/// Keeping this out of the domain models lets the provider mapping be audited and versioned.
/// Every entry below was verified against the live <c>systemesListe.php</c> catalogue on
/// 2026-08-03 (see <c>ScreenScraperLiveValidationTests</c>); the set is no longer provisional.
/// </summary>
public static class ScreenScraperSystemMap
{
    public const int Version = 1;

    private static readonly IReadOnlyDictionary<string, int> SystemIds =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["playstation"] = 57,
            ["playstation2"] = 58,
            ["playstation3"] = 59,
            ["psp"] = 61,
            ["gamecube"] = 13,
            ["wii"] = 16,
            ["megadrive"] = 1,
            ["nds"] = 15,
            ["gba"] = 12,
            ["snes"] = 4,
            ["dreamcast"] = 23,
            ["arcade"] = 75,
            ["gbc"] = 10,
            // Added alongside the 3DS/NES system support merged from main. These two ids
            // (3DS = 17, NES = 3) are ScreenScraper's documented values but have not yet been
            // cross-checked against the live systemesListe.php catalogue like the entries above.
            ["3ds"] = 17,
            ["nes"] = 3,
        };

    /// <summary>All EmuShelf-to-ScreenScraper system mappings, for auditing against the live catalogue.</summary>
    public static IReadOnlyDictionary<string, int> Entries => SystemIds;

    public static bool TryGetSystemId(string emuShelfSystemId, out int screenScraperSystemId) =>
        SystemIds.TryGetValue(emuShelfSystemId, out screenScraperSystemId);
}
