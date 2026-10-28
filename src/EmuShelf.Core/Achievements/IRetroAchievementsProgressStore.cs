namespace EmuShelf.Core.Achievements;

/// <summary>
/// Cached account-scoped progress for one RA game. <see cref="LastRefreshedAt"/> is when these numbers
/// were last written: a refresh that finds them unchanged leaves the row (and so the time) alone.
/// </summary>
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

    /// <summary>All cached progress, keyed by RA game id, so a refresh can skip unchanged games.</summary>
    IReadOnlyDictionary<int, RetroAchievementsProgressSnapshot> GetAllProgress();

    void SaveProgress(RetroAchievementsGameProgress progress, DateTimeOffset refreshedAt);

    /// <summary>
    /// Saves a whole batch of summaries as one write. A library refresh must use this rather than
    /// calling the single-game overload in a loop: on Android the database sits on shared storage,
    /// where every separate commit costs ~20 ms, so 800 games saved one by one stalled the device for
    /// ~16 s after every cold launch.
    /// </summary>
    void SaveProgress(IReadOnlyCollection<RetroAchievementsGameProgress> progress, DateTimeOffset refreshedAt);

    /// <summary>
    /// Gets when every linked game's summary last completed successfully. This is separate from
    /// each game's timestamp because a batched refresh can partially update before a later batch
    /// fails.
    /// </summary>
    DateTimeOffset? GetLastSummaryRefreshAt();

    /// <summary>Records a fully successful summary refresh of the linked game set.</summary>
    void SaveLastSummaryRefreshAt(DateTimeOffset refreshedAt);

    void ClearProgress();
}
