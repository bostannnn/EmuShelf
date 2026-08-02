namespace EmuShelf.Integrations.Importing;

/// <summary>
/// FinalBurn Neo BIOS and device archives. These ship as ordinary romset zips named by their short
/// set id (neogeo.zip, pgm.zip) but are never a playable game — they exist so the emulator can find
/// shared ROMs at launch. They are hidden at import time, before the DAT has been downloaded, so a
/// folder scan or a manual pick never turns them into junk library entries.
///
/// The FBNeo DAT's <c>isbios</c>/<c>isdevice</c> flags are the authoritative filter during metadata
/// enrichment (see <c>LibretroDatCatalog.ParseLogiqxXml</c>); this small bundled list only covers
/// the offline import path. Placing these files for launch remains the user's job and RetroArch's
/// system directory — EmuShelf never manages BIOS images.
/// </summary>
public static class KnownArcadeBiosSets
{
    private static readonly IReadOnlySet<string> Names =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "neogeo",   // Neo Geo (by far the most common separate BIOS zip a user will have)
            "pgm",      // IGS PolyGame Master
            "decocass", // DECO Cassette System
            "isgsm",    // ISG Selection Master Type 2006
            "skns",     // Super Kaneko Nova System
            "cvs",      // Century CVS System
            "nmk004",   // NMK004 protection MCU
        };

    public static bool Contains(string setName) => Names.Contains(setName);
}
