using EmuShelf.Core.Metadata;

namespace EmuShelf.Integrations.Metadata;

public static class KnownMetadataProfiles
{
    private static readonly IGameIdentifierExtractor PlayStationExtractor =
        new PlayStationIdentifierExtractor();
    private static readonly IGameIdentifierExtractor NintendoExtractor =
        new NintendoDiscIdentifierExtractor();

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
                    "https://raw.githubusercontent.com/xlenore/psx-covers/main/covers/default"),
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
                    "https://raw.githubusercontent.com/xlenore/ps2-covers/main/covers/default"),
                new LibretroArtworkProvider("Sony - PlayStation 2"),
            ]),
        new(
            "gamecube",
            GameIdentifierKind.DiscId,
            RawCatalog("dat/Nintendo%20-%20GameCube.dat"),
            NintendoExtractor,
            [new LibretroArtworkProvider("Nintendo - GameCube")]),
        new(
            "wii",
            GameIdentifierKind.DiscId,
            RawCatalog("dat/Nintendo%20-%20Wii.dat"),
            NintendoExtractor,
            [new LibretroArtworkProvider("Nintendo - Wii")]),
    ];

    private static Uri RawCatalog(string path) =>
        new($"https://raw.githubusercontent.com/libretro/libretro-database/master/{path}");
}
