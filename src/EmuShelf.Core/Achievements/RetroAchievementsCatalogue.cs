namespace EmuShelf.Core.Achievements;

/// <summary>The RA game a local hash resolves to, with its achievement count.</summary>
public sealed record RetroAchievementsCatalogueMatch(int GameId, string Title, int AchievementCount);

/// <summary>
/// A per-console hash lookup over an achievement-bearing catalogue. <see cref="IsFresh"/>
/// distinguishes a catalogue within its refresh window (a missing hash then means the game
/// definitely has no achievement set) from a stale one served offline (a missing hash is unknown).
/// </summary>
public sealed class RetroAchievementsCatalogueLookup
{
    private readonly IReadOnlyDictionary<string, RetroAchievementsCatalogueMatch> _byHash;

    public RetroAchievementsCatalogueLookup(
        bool isFresh,
        IReadOnlyDictionary<string, RetroAchievementsCatalogueMatch> byHash)
    {
        IsFresh = isFresh;
        _byHash = byHash;
    }

    public bool IsFresh { get; }

    /// <summary>Returns the RA game a canonical hash maps to, or null when the catalogue has none.</summary>
    public RetroAchievementsCatalogueMatch? Find(string canonicalHash) =>
        _byHash.TryGetValue(canonicalHash.ToLowerInvariant(), out var match) ? match : null;
}

/// <summary>
/// Caches the achievement-bearing game/hash catalogue for each RA console under
/// <c>Cache/RetroAchievements/</c>. A catalogue is fetched at most once per console every seven
/// days unless a refresh is forced, and a stale cache is still served when a fetch cannot run.
/// </summary>
public interface IRetroAchievementsCatalogueCache
{
    /// <summary>
    /// Returns the hash lookup for a console, fetching and caching when the cache is missing,
    /// stale, or <paramref name="forceRefresh"/> is set and credentials are available. Returns
    /// null only when no cache exists and no fresh copy could be fetched.
    /// </summary>
    Task<RetroAchievementsCatalogueLookup?> GetLookupAsync(
        int consoleId,
        RetroAchievementsCredentials? credentials,
        bool forceRefresh,
        CancellationToken cancellationToken = default);
}
