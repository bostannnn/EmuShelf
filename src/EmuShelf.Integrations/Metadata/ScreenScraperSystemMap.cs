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
        };

    /// <summary>All EmuShelf-to-ScreenScraper system mappings, for auditing against the live catalogue.</summary>
    public static IReadOnlyDictionary<string, int> Entries => SystemIds;

    public static bool TryGetSystemId(string emuShelfSystemId, out int screenScraperSystemId) =>
        SystemIds.TryGetValue(emuShelfSystemId, out screenScraperSystemId);
}
