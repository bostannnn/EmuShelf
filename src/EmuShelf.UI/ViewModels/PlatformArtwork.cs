using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Resolves stable EmuShelf system ids to the licensed OpenEmu platform-library
/// artwork bundled with the app. Bitmaps are intentionally cached for the app
/// lifetime: these tiny shared navigation assets are never owned by a game tile.
/// </summary>
public static class PlatformArtwork
{
    private const string AssetRoot =
        "avares://EmuShelf.UI/Assets/ThirdParty/OpenEmu/PlatformIcons/";
    private const string ConsoleAssetRoot =
        "avares://EmuShelf.UI/Assets/PlatformConsoleArt/";

    private static readonly IReadOnlyDictionary<string, string> ConsoleAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["playstation2"] = "playstation2.png",
            ["playstation3"] = "playstation3.png",
            ["wii"] = "wii.png",
            ["psp"] = "psp.png",
            // Original EmuShelf art: OpenEmu ships no 3DS icon, so this dual-screen clamshell is
            // bundled here rather than under the licensed OpenEmu PlatformIcons set.
            ["3ds"] = "3ds.png",
        };

    private static readonly IReadOnlyDictionary<string, string> Assets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["playstation"] = "PlayStation/Images.xcassets/psx_library.imageset/psx_library@2x.png",
            ["playstation2"] = "PlayStation 2/Images.xcassets/ps2_library.imageset/psx_library@2x.png",
            ["playstation3"] = "PlayStation 2/Images.xcassets/ps2_library.imageset/psx_library@2x.png",
            ["playstation4"] = "PlayStation 2/Images.xcassets/ps2_library.imageset/psx_library@2x.png",
            ["psp"] = "PSP/Images.xcassets/psp_library.imageset/psp_library@2x.png",
            ["gamecube"] = "GameCube/Images.xcassets/gamecube_library.imageset/gamecube_library@2x.png",
            ["wii"] = "Wii/Images.xcassets/wii_library.imageset/wii_library@2x.png",
            ["nes"] = "NES/Images.xcassets/nes_library.imageset/nes_library@2x.png",
            ["fds"] = "Nintendo FDS/Images.xcassets/famicom_library.imageset/famicom_library@2x.png",
            ["snes"] = "SuperNES/Images.xcassets/snes_usa_library.imageset/snes_usa_library@2x.png",
            ["n64"] = "N64/Images.xcassets/n64_library.imageset/n64_library@2x.png",
            ["nds"] = "NDS/Images.xcassets/nds_library.imageset/nds_library@2x.png",
            ["nintendo-ds"] = "NDS/Images.xcassets/nds_library.imageset/nds_library@2x.png",
            ["gameboy"] = "GameBoy/Images.xcassets/gameboy_library.imageset/gameboy_library@2x.png",
            ["gameboy-color"] = "GameBoy/Images.xcassets/gameboy_library.imageset/gameboy_library@2x.png",
            ["gbc"] = "GameBoy/Images.xcassets/gameboy_library.imageset/gameboy_library@2x.png",
            ["gameboy-advance"] = "GameBoy Advance/Images.xcassets/gba_library.imageset/gba_library@2x.png",
            ["gba"] = "GameBoy Advance/Images.xcassets/gba_library.imageset/gba_library@2x.png",
            ["virtual-boy"] = "Virtual Boy/Images.xcassets/vb_library.imageset/vb_library@2x.png",
            ["genesis"] = "Genesis/Images.xcassets/genesis_library.imageset/genesis_library@2x.png",
            ["megadrive"] = "Genesis/Images.xcassets/megadrive_library.imageset/megadrive_library@2x.png",
            ["sega-cd"] = "Sega CD/Images.xcassets/segacd_library.imageset/segacd_library@2x.png",
            ["sega-32x"] = "Sega 32X/Images.xcassets/32x_na_library.imageset/32x_na_library@2x.png",
            ["master-system"] = "SegaMasterSystem/Images.xcassets/sms_library.imageset/sms_library@2x.png",
            ["game-gear"] = "GameGear/Images.xcassets/gamegear_library.imageset/gamegear_library@2x.png",
            ["saturn"] = "Saturn/Images.xcassets/saturn_library.imageset/saturn_library@2x.png",
            ["dreamcast"] = "Dreamcast/Images.xcassets/dc_library.imageset/dc_library@2x.png",
            ["arcade"] = "Arcade/Images.xcassets/arcade_library.imageset/arcade_library.png",
            ["atari-2600"] = "Atari 2600/Images.xcassets/atari2600_library.imageset/atari2600_library@2x.png",
            ["atari-5200"] = "Atari 5200/Images.xcassets/atari5200_library.imageset/atari5200_library@2x.png",
            ["atari-7800"] = "Atari 7800/Images.xcassets/atari7800_library.imageset/atari7800_library@2x.png",
            ["atari-8bit"] = "Atari 8-bit/Images.xcassets/atari8bit_library.imageset/atari8bit_library.png",
            ["atari-lynx"] = "Lynx/Images.xcassets/lynx_library.imageset/lynx_library@2x.png",
            ["atari-jaguar"] = "Jaguar/Images.xcassets/jaguar_library.imageset/jaguar_library.png",
            ["jaguar"] = "Jaguar/Images.xcassets/jaguar_library.imageset/jaguar_library.png",
            ["colecovision"] = "ColecoVision/Images.xcassets/colecovision_library.imageset/colecovision_library@2x.png",
            ["intellivision"] = "Intellivision/Images.xcassets/intellivision_library.imageset/intellivision_library@2x.png",
            ["odyssey2"] = "Odyssey2/Images.xcassets/odyssey2_library.imageset/odyssey2_library@2x.png",
            ["neo-geo-pocket"] = "NeoGeoPocket/Images.xcassets/neogeopocket_library.imageset/neogeopocket_library@2x.png",
            ["pc-engine"] = "PC Engine/Images.xcassets/pcengine_library.imageset/pcengine_library@2x.png",
            ["turbografx-16"] = "PC Engine/Images.xcassets/tg16_library.imageset/tg16_library@2x.png",
            ["pc-engine-cd"] = "PC Engine CD/Images.xcassets/pcenginecd_library.imageset/pcenginecd_library@2x.png",
            ["pc-fx"] = "PC-FX/Images.xcassets/pcfx_library.imageset/pcfx_library@2x.png",
            ["vectrex"] = "Vectrex/Images.xcassets/vectrex_library.imageset/vectrex_library@2x.png",
            ["wonderswan"] = "WonderSwan/Images.xcassets/wonderswan_library.imageset/wonderswan_library@2x.png",
            ["pokemon-mini"] = "Pokemon mini/Images.xcassets/pokemonmini_library.imageset/pokemonmini_library@2x.png",
            ["3do"] = "3DO/Images.xcassets/3do_library.imageset/3do_library@2x.png",
            ["commodore-64"] = "Commodore 64/Images.xcassets/c64_library.imageset/c64_library@2x.png",
            ["c64"] = "Commodore 64/Images.xcassets/c64_library.imageset/c64_library@2x.png",
            ["msx"] = "MSX/Images.xcassets/msx_library.imageset/msx_library.png",
            ["sg-1000"] = "SG-1000/Images.xcassets/sg1000_library.imageset/sg1000_library@2x.png",
            ["supervision"] = "Supervision/Images.xcassets/supervision_library.imageset/supervision_library.png",
            ["vmu"] = "VMU/Images.xcassets/vmu_library.imageset/vmu_library.png",
        };

    private static readonly Dictionary<string, IImage> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> SupportedSystemIds { get; } =
        Assets.Keys.ToArray();

    public static IImage? ForSystem(string? systemId)
    {
        if (systemId is null)
            return null;

        var assetRoot = ConsoleAssetRoot;
        if (!ConsoleAssets.TryGetValue(systemId, out var relativePath))
        {
            if (!Assets.TryGetValue(systemId, out relativePath))
                return null;
            assetRoot = AssetRoot;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(systemId, out var cached))
                return cached;

            var escapedPath = string.Join(
                '/',
                relativePath.Split('/').Select(Uri.EscapeDataString));
            using var stream = AssetLoader.Open(new Uri(
                assetRoot + escapedPath));
            var bitmap = new Bitmap(stream);
            Cache[systemId] = bitmap;
            return bitmap;
        }
    }

    public static readonly IValueConverter Converter =
        new FuncValueConverter<string?, IImage?>(ForSystem);
}
