namespace EmuShelf.Core.Achievements;

/// <summary>Cached, account-scoped full achievement details for one RA game.</summary>
public sealed record RetroAchievementsDetailsSnapshot(
    RetroAchievementsGameDetails Details,
    DateTimeOffset LastRefreshedAt);

/// <summary>
/// Persists the full game detail returned for the connected account. These records contain
/// account-specific earned dates, so they are cleared with the account's progress on disconnect.
/// </summary>
public interface IRetroAchievementsDetailsStore
{
    RetroAchievementsDetailsSnapshot? GetDetails(int retroAchievementsGameId);

    void SaveDetails(RetroAchievementsGameDetails details, DateTimeOffset refreshedAt);

    void ClearDetails();
}
