namespace EmuShelf.Integrations.Emulators.Android;

/// <summary>
/// One Android RetroArch core that EmuShelf knows how to launch for a system. Android keeps
/// RetroArch's downloaded cores in the emulator's app-private data directory, so EmuShelf cannot
/// enumerate or open them. The controller settings therefore offer the known compatible filenames
/// and persist the exact path RetroArch accepts in its <c>LIBRETRO</c> intent extra.
/// </summary>
public sealed record AndroidRetroArchCoreOption(string DisplayName, string CoreId, string Path);

/// <summary>
/// Compatible Android RetroArch cores by EmuShelf system id. This is a selector, not a core manager:
/// the user still installs the chosen core in RetroArch, and EmuShelf never reads or changes RetroArch's
/// private files. Paths use the app-private layout verified on the AYN Thor and in the Android port plan.
/// </summary>
public static class AndroidRetroArchCoreCatalog
{
    public const string CoreDirectory = "/data/data/com.retroarch.aarch64/cores";

    public static IReadOnlyDictionary<string, IReadOnlyList<AndroidRetroArchCoreOption>> BySystem { get; } =
        new Dictionary<string, IReadOnlyList<AndroidRetroArchCoreOption>>(StringComparer.Ordinal)
        {
            ["playstation"] =
            [
                Core("SwanStation", "swanstation"),
                Core("Beetle PSX HW", "mednafen_psx_hw"),
                Core("Beetle PSX", "mednafen_psx"),
                Core("PCSX-ReARMed", "pcsx_rearmed"),
            ],
            ["megadrive"] =
            [
                Core("Genesis Plus GX", "genesis_plus_gx"),
                Core("PicoDrive", "picodrive"),
            ],
            ["nds"] =
            [
                Core("melonDS DS", "melondsds"),
                Core("melonDS", "melonds"),
                Core("DeSmuME", "desmume"),
            ],
            ["gba"] =
            [
                Core("mGBA", "mgba"),
                Core("VBA-M", "vbam"),
            ],
            ["snes"] =
            [
                Core("Snes9x", "snes9x"),
                Core("Snes9x 2010", "snes9x2010"),
                Core("bsnes", "bsnes"),
            ],
            ["nes"] =
            [
                Core("Mesen", "mesen"),
                Core("FCEUmm", "fceumm"),
                Core("Nestopia UE", "nestopia"),
            ],
            ["dreamcast"] = [Core("Flycast", "flycast")],
            ["arcade"] = [Core("FinalBurn Neo", "fbneo")],
            ["gbc"] =
            [
                Core("Gambatte", "gambatte"),
                Core("SameBoy", "sameboy"),
                Core("mGBA", "mgba"),
            ],
        };

    private static AndroidRetroArchCoreOption Core(string displayName, string coreId) =>
        new(
            displayName,
            coreId,
            $"{CoreDirectory}/{coreId}_libretro_android.so");
}
