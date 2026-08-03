using EmuShelf.Core.Metadata;

namespace EmuShelf.Integrations.Metadata;

public static class KnownMetadataProfiles
{
    private static readonly IGameIdentifierExtractor PlayStationExtractor =
        new PlayStationIdentifierExtractor();
    private static readonly IGameIdentifierExtractor PlayStation3Extractor =
        new PlayStation3IdentifierExtractor();
    private static readonly IGameIdentifierExtractor PspExtractor =
        new PspIdentifierExtractor();
    private static readonly IGameIdentifierExtractor NintendoExtractor =
        new NintendoDiscIdentifierExtractor();
    private static readonly IGameIdentifierExtractor MegaDriveExtractor =
        new MegaDriveRomIdentifierExtractor();
    private static readonly IGameIdentifierExtractor NintendoDsExtractor =
        new NintendoDsRomIdentifierExtractor();
    private static readonly IGameIdentifierExtractor Nintendo3dsExtractor =
        new Nintendo3dsRomIdentifierExtractor();
    private static readonly IGameIdentifierExtractor GameBoyAdvanceExtractor =
        new GameBoyAdvanceRomIdentifierExtractor();
    private static readonly IGameIdentifierExtractor GameBoyColorExtractor =
        new GameBoyColorRomIdentifierExtractor();
    private static readonly IGameIdentifierExtractor NesExtractor =
        new NesRomIdentifierExtractor();
    private static readonly IGameIdentifierExtractor SuperNintendoExtractor =
        new SuperNintendoRomIdentifierExtractor();
    private static readonly IGameIdentifierExtractor DreamcastExtractor =
        new DreamcastIdentifierExtractor();
    private static readonly IGameIdentifierExtractor ArcadeExtractor =
        new ArcadeSetIdentifierExtractor();

    // Cover repos are fetched through the jsDelivr CDN rather than raw.githubusercontent.com:
    // GitHub's raw host enforces a per-IP anonymous rate limit that a whole library's worth of
    // covers trips in a burst (HTTP 429), whereas jsDelivr is built to serve those files in bulk.
    public static IReadOnlyList<MetadataSystemProfile> All { get; } =
    [
        new(
            "playstation",
            GameIdentifierKind.Serial,
            RawCatalog("metadat/redump/Sony%20-%20PlayStation.dat"),
            PlayStationExtractor,
            [
                new XlenoreArtworkProvider(
                    "xlenore-psx",
                    "https://cdn.jsdelivr.net/gh/xlenore/psx-covers@main/covers/default"),
                new LibretroArtworkProvider("Sony - PlayStation"),
            ]),
        new(
            "playstation2",
            GameIdentifierKind.Serial,
            RawCatalog("metadat/redump/Sony%20-%20PlayStation%202.dat"),
            PlayStationExtractor,
            [
                new XlenoreArtworkProvider(
                    "xlenore-ps2",
                    "https://cdn.jsdelivr.net/gh/xlenore/ps2-covers@main/covers/default"),
                new LibretroArtworkProvider("Sony - PlayStation 2"),
            ]),
        new(
            "playstation3",
            GameIdentifierKind.Serial,
            RawCatalog("metadat/redump/Sony%20-%20PlayStation%203.dat"),
            PlayStation3Extractor,
            [
                new GameTdbPlayStation3ArtworkProvider(),
                new LibretroArtworkProvider("Sony - PlayStation 3"),
            ]),
        new(
            "psp",
            GameIdentifierKind.Serial,
            RawCatalog("metadat/redump/Sony%20-%20PlayStation%20Portable.dat"),
            PspExtractor,
            [new LibretroArtworkProvider("Sony - PlayStation Portable")]),
        new(
            "gamecube",
            GameIdentifierKind.DiscId,
            RawCatalog("dat/Nintendo%20-%20GameCube.dat"),
            NintendoExtractor,
            [new GameTdbArtworkProvider(), new LibretroArtworkProvider("Nintendo - GameCube")]),
        new(
            "wii",
            GameIdentifierKind.DiscId,
            RawCatalog("dat/Nintendo%20-%20Wii.dat"),
            NintendoExtractor,
            [new GameTdbArtworkProvider(), new LibretroArtworkProvider("Nintendo - Wii")]),
        new(
            "megadrive",
            GameIdentifierKind.Sha1,
            RawCatalog("metadat/no-intro/Sega%20-%20Mega%20Drive%20-%20Genesis.dat"),
            MegaDriveExtractor,
            [new LibretroArtworkProvider("Sega - Mega Drive - Genesis")]),
        new(
            "nds",
            GameIdentifierKind.Sha1,
            RawCatalog("metadat/no-intro/Nintendo%20-%20Nintendo%20DS.dat"),
            NintendoDsExtractor,
            // The header game code is deliberately not a catalogue fallback key: a romhack patches
            // the ROM but never that code, so every hack of a game would resolve to the original's
            // entry and inherit its title and cover. The checksum stays the only DS catalogue key,
            // and a modified dump is matched by filename through the artwork index instead.
            [new LibretroArtworkProvider("Nintendo - Nintendo DS")]),
        new(
            "gba",
            GameIdentifierKind.Sha1,
            RawCatalog("metadat/no-intro/Nintendo%20-%20Game%20Boy%20Advance.dat"),
            GameBoyAdvanceExtractor,
            [new LibretroArtworkProvider("Nintendo - Game Boy Advance")]),
        // 3DS covers come from the id-addressed GameTDB route (keyed by the NCCH product code), so
        // they resolve without hashing a multi-gigabyte dump. The No-Intro DAT is supplied for a
        // best-effort serial match and the Libretro title provider as a fallback; neither is needed
        // for the GameTDB covers. Compressed/CIA/homebrew files carry no product code and match by
        // filename only.
        new(
            "3ds",
            GameIdentifierKind.Serial,
            RawCatalog("metadat/no-intro/Nintendo%20-%20Nintendo%203DS.dat"),
            Nintendo3dsExtractor,
            [new GameTdb3dsArtworkProvider(), new LibretroArtworkProvider("Nintendo - Nintendo 3DS")]),
        // NES is keyed by the SHA-1 of the whole headered file, the form the No-Intro NES set uses.
        new(
            "nes",
            GameIdentifierKind.Sha1,
            RawCatalog("metadat/no-intro/Nintendo%20-%20Nintendo%20Entertainment%20System.dat"),
            NesExtractor,
            [new LibretroArtworkProvider("Nintendo - Nintendo Entertainment System")]),
        new(
            "snes",
            GameIdentifierKind.Sha1,
            RawCatalog("metadat/no-intro/Nintendo%20-%20Super%20Nintendo%20Entertainment%20System.dat"),
            SuperNintendoExtractor,
            [new LibretroArtworkProvider("Nintendo - Super Nintendo Entertainment System")]),
        new(
            "dreamcast",
            GameIdentifierKind.Sha1,
            RawCatalog("metadat/redump/Sega%20-%20Dreamcast.dat"),
            DreamcastExtractor,
            [new LibretroArtworkProvider("Sega - Dreamcast")],
            FallbackCatalogKeyKinds: [GameIdentifierKind.Serial],
            ReadRomSerials: true),
        // Arcade is keyed by the FinalBurn Neo romset short id (the zip basename), resolved to a
        // human title through the FBNeo DAT's game-name -> description mapping. Two DATs share the
        // fbneo-split folder: "FBNeo - Arcade Games.dat" is clrmamepro *text* whose game name is the
        // human title, while "FinalBurn Neo (ClrMame Pro XML, Arcade only).dat" is Logiqx *XML* whose
        // game name is the short set id and title is a <description> — the only one whose shape the
        // ArcadeSetName keying and the XML parse path expect. It carries ~7k sets with full ROM
        // hashes (~10 MB), so it needs a larger size cap than the console text DATs.
        new(
            "arcade",
            GameIdentifierKind.ArcadeSetName,
            RawCatalog("metadat/fbneo-split/FinalBurn%20Neo%20%28ClrMame%20Pro%20XML%2C%20Arcade%20only%29.dat"),
            ArcadeExtractor,
            [new LibretroArcadeArtworkProvider("FBNeo - Arcade Games")],
            CatalogFormat: DatFormat.LogiqxXml,
            MaxCatalogBytes: 48L * 1024 * 1024),
        new(
            "gbc",
            GameIdentifierKind.Sha1,
            RawCatalog("metadat/no-intro/Nintendo%20-%20Game%20Boy%20Color.dat"),
            GameBoyColorExtractor,
            [new LibretroArtworkProvider("Nintendo - Game Boy Color")]),
    ];

    private static Uri RawCatalog(string path) =>
        new($"https://raw.githubusercontent.com/libretro/libretro-database/master/{path}");
}
