namespace EmuShelf.Core.Achievements;

/// <summary>Cached account-scoped progress for one RA game, with when it was last refreshed.</summary>
public sealed record RetroAchievementsProgressSnapshot(
    RetroAchievementsGameProgress Progress,
    DateTimeOffset LastRefreshedAt);

/// <summary>
/// Persists account-scoped progress summaries (awarded / total, plus hardcore) so the library and
/// popup stay useful offline. Progress is keyed by RA game id and is cleared when the account
/// disconnects, since it belongs only to the connected account.
/// </summary>
public interface IRetroAchievementsProgressStore
{
    /// <summary>Distinct RA game ids linked to local games, for batched progress refreshes.</summary>
    IReadOnlyList<int> GetLinkedRetroAchievementsGameIds();

    RetroAchievementsProgressSnapshot? GetProgress(int retroAchievementsGameId);

    void SaveProgress(RetroAchievementsGameProgress progress, DateTimeOffset refreshedAt);

    void ClearProgress();
}
