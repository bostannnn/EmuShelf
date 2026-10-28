using EmuShelf.Core.Library;

namespace EmuShelf.Core.Achievements;

/// <summary>
/// State of EmuShelf's local, read-only attempt to calculate a canonical
/// RetroAchievements game hash. A calculated hash is not itself proof that the
/// game has achievements; that requires a later catalogue lookup.
/// </summary>
public enum RetroAchievementsIdentificationStatus
{
    NotAttempted = 0,
    Hashed = 1,
    UnsupportedFormat = 2,
    InvalidMedia = 3,
    Unreadable = 4,
}

/// <summary>
/// Cheap file-metadata snapshot used to avoid reading an unchanged game image
/// again. Descriptor dependencies such as CUE payloads and the selected M3U
/// entry are included in <see cref="Fingerprint"/>.
/// </summary>
public sealed record RetroAchievementsSourceSnapshot(
    string Fingerprint,
    bool CanHash,
    RetroAchievementsIdentificationStatus Status,
    string? Error);

public sealed record RetroAchievementsHashResult(
    RetroAchievementsIdentificationStatus Status,
    string? CanonicalHash,
    string HashAlgorithmVersion,
    string SourceFingerprint,
    DateTimeOffset AttemptedAt,
    string? Error);

/// <summary>
/// Persisted local-game link. RetroAchievementsGameId and HasAchievements stay
/// null until a fresh achievement-bearing catalogue resolves CanonicalHash.
/// </summary>
public sealed record RetroAchievementsGameLink(
    long GameId,
    RetroAchievementsIdentificationStatus Status,
    string? CanonicalHash,
    string HashAlgorithmVersion,
    string SourceFingerprint,
    int? RetroAchievementsGameId,
    bool? HasAchievements,
    DateTimeOffset LastAttemptedAt,
    string? LastError);

/// <summary>
/// System-specific canonical hashing. Implementations open game media read-only;
/// callers must execute Identify off the UI thread.
/// </summary>
public interface IRetroAchievementsGameHasher
{
    /// <summary>Returns the version of the canonical hash algorithm for this game's system.</summary>
    string GetAlgorithmVersion(Game game);

    /// <summary>
    /// Determines whether a persisted algorithm version produces the same canonical hash for the
    /// supplied game. This permits a narrowly scoped compatibility migration without reopening
    /// unchanged media from systems whose hash readers did not change.
    /// </summary>
    bool IsAlgorithmVersionCompatible(Game game, string persistedVersion);

    RetroAchievementsSourceSnapshot Inspect(Game game);

    RetroAchievementsHashResult Identify(
        Game game,
        CancellationToken cancellationToken = default);
}

/// <summary>A locally hashed game awaiting catalogue resolution.</summary>
public sealed record RetroAchievementsHashedGame(
    long GameId,
    string SystemId,
    string CanonicalHash,
    string Title = "");

public interface IRetroAchievementsStore
{
    Game? GetGame(long gameId);

    RetroAchievementsGameLink? GetGameLink(long gameId);

    void SaveIdentification(long gameId, RetroAchievementsHashResult result);

    /// <summary>Returns every game with a successfully calculated canonical hash, for matching.</summary>
    IReadOnlyList<RetroAchievementsHashedGame> GetHashedGames();

    /// <summary>
    /// Records the outcomes of resolving hashes against a fresh catalogue, as one write. Leave a game
    /// out when the catalogue was stale, so a miss never becomes a false "no". Batched because a
    /// library sync resolves every hashed game, and one commit per game is seconds of stall on
    /// Android's shared storage.
    /// </summary>
    void SaveCatalogueMatches(IReadOnlyCollection<RetroAchievementsCatalogueResolution> resolutions);
}

/// <summary>
/// The catalogue outcome for one local game: a matched RA game id and whether it has achievements,
/// or (null, false) for a fresh miss.
/// </summary>
public sealed record RetroAchievementsCatalogueResolution(
    long GameId,
    int? RetroAchievementsGameId,
    bool? HasAchievements);
