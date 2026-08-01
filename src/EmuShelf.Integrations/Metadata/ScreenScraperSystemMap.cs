namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Explicit boundary between stable EmuShelf system IDs and ScreenScraper's numeric IDs.
/// Keeping this out of the domain models lets the provider mapping be audited and versioned.
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

    public static bool TryGetSystemId(string emuShelfSystemId, out int screenScraperSystemId) =>
        SystemIds.TryGetValue(emuShelfSystemId, out screenScraperSystemId);
}
