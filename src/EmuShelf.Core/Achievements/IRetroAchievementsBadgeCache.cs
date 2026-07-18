namespace EmuShelf.Core.Achievements;

/// <summary>
/// Resolves a locally cached RetroAchievements badge image by its public badge name. A null
/// result means the caller must retain its local placeholder; implementations must never require
/// an account credential for these public image requests.
/// </summary>
public interface IRetroAchievementsBadgeCache
{
    Task<string?> GetBadgePathAsync(
        string badgeName,
        CancellationToken cancellationToken = default);
}
