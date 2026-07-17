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
    string AlgorithmVersion { get; }

    RetroAchievementsSourceSnapshot Inspect(Game game);

    RetroAchievementsHashResult Identify(
        Game game,
        CancellationToken cancellationToken = default);
}

public interface IRetroAchievementsStore
{
    Game? GetGame(long gameId);

    RetroAchievementsGameLink? GetGameLink(long gameId);

    void SaveIdentification(long gameId, RetroAchievementsHashResult result);
}
