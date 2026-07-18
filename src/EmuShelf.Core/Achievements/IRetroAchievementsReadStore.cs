namespace EmuShelf.Core.Achievements;

/// <summary>
/// Bulk read access for the library presentation: all links and all cached progress in two
/// queries, so the grid and list can render every game's achievement state without an N+1 pass.
/// </summary>
public interface IRetroAchievementsReadStore
{
    /// <summary>All identification links, keyed by local game id.</summary>
    IReadOnlyDictionary<long, RetroAchievementsGameLink> GetAllLinks();

    /// <summary>All cached progress, keyed by RA game id.</summary>
    IReadOnlyDictionary<int, RetroAchievementsProgressSnapshot> GetAllProgress();
}
