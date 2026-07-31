namespace EmuShelf.Core.Achievements;

/// <summary>
/// Maps EmuShelf system ids to RetroAchievements console ids. PlayStation 3 has no mapping, so
/// it is never matched. The expansion mappings are kept beside their verified hash readers.
/// </summary>
public static class RetroAchievementsConsoles
{
    public static int? ForSystem(string systemId) => systemId switch
    {
        "playstation" => 12,
        "playstation2" => 21,
        "gamecube" => 16,
        "wii" => 19,
        "megadrive" => 1,
        "snes" => 3,
        "gba" => 5,
        "gbc" => 6,
        "arcade" => 27,
        "nds" => 18,
        "psp" => 41,
        "dreamcast" => 40,
        _ => null,
    };
}
