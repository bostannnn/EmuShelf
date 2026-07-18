namespace EmuShelf.Core.Achievements;

/// <summary>
/// Maps EmuShelf system ids to RetroAchievements console ids. RA defines PlayStation (12),
/// PlayStation 2 (21), GameCube (16), and Wii (19); it has no PlayStation 3 console id, so that
/// system has no mapping and is never matched.
/// </summary>
public static class RetroAchievementsConsoles
{
    public static int? ForSystem(string systemId) => systemId switch
    {
        "playstation" => 12,
        "playstation2" => 21,
        "gamecube" => 16,
        "wii" => 19,
        _ => null,
    };
}
