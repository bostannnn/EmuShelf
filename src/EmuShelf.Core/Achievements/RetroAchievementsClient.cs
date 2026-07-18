namespace EmuShelf.Core.Achievements;

/// <summary>
/// Outcome of a single read-only RetroAchievements Web API request. The states are kept
/// distinct so callers can retain usable cached data and react correctly: an authentication
/// failure must never be retried automatically, an offline or server failure should fall back
/// to cache, and a rate-limited response carries the server's requested wait.
/// </summary>
public enum RetroAchievementsRequestStatus
{
    Success = 0,
    NotConnected = 1,
    AuthenticationFailed = 2,
    Offline = 3,
    RateLimited = 4,
    ServerError = 5,
    MalformedResponse = 6,
}

/// <summary>Result of a read-only request, pairing a status with a value or the reason it is absent.</summary>
public sealed record RetroAchievementsResponse<T>(
    RetroAchievementsRequestStatus Status,
    T? Value,
    TimeSpan? RetryAfter = null,
    string? Error = null)
{
    public bool IsSuccess => Status == RetroAchievementsRequestStatus.Success && Value is not null;

    public static RetroAchievementsResponse<T> Success(T value) =>
        new(RetroAchievementsRequestStatus.Success, value);

    public static RetroAchievementsResponse<T> Failure(
        RetroAchievementsRequestStatus status,
        TimeSpan? retryAfter = null,
        string? error = null) =>
        new(status, default, retryAfter, error);
}

/// <summary>
/// The username and Web API key used to authenticate read-only calls. The optional stable user id
/// is used when querying account-owned data so a later username change does not break progress
/// refreshes. This is a Web API key — credential setup, never a password login — and must never
/// be logged or placed in a logged URI.
/// </summary>
public sealed record RetroAchievementsCredentials(
    string Username,
    string ApiKey,
    string? UserUlid = null);

public static class RetroAchievementsApi
{
    /// <summary>
    /// Maximum game ids per <see cref="IRetroAchievementsClient.GetUserProgressAsync"/> call;
    /// larger sets must be split by the caller so the request URI stays within server limits.
    /// </summary>
    public const int MaxUserProgressBatchSize = 100;
}

/// <summary>Validated account profile. <see cref="UserUlid"/> is RetroAchievements' stable id.</summary>
public sealed record RetroAchievementsProfile(
    string Username,
    string UserUlid,
    int TotalPoints,
    int TotalSoftcorePoints);

/// <summary>One achievement-bearing game in a system catalogue, with the hashes that map to it.</summary>
public sealed record RetroAchievementsCatalogueGame(
    int GameId,
    string Title,
    int AchievementCount,
    IReadOnlyList<string> Hashes);

/// <summary>Account-scoped progress summary for a single RetroAchievements game.</summary>
public sealed record RetroAchievementsGameProgress(
    int GameId,
    int AchievementCount,
    int NumAwarded,
    int NumAwardedHardcore);

/// <summary>
/// One achievement in the resolved game's display data. An earned date means the achievement
/// counts toward the primary progress total; a hardcore date is an additional distinction, not
/// a separate mode the user must choose.
/// </summary>
public sealed record RetroAchievementsAchievement(
    int AchievementId,
    string Title,
    string Description,
    int Points,
    string BadgeName,
    int DisplayOrder,
    DateTimeOffset? DateEarned,
    DateTimeOffset? DateEarnedHardcore)
{
    public bool IsEarned => DateEarned is not null || DateEarnedHardcore is not null;
    public bool IsHardcore => DateEarnedHardcore is not null;
}

/// <summary>
/// The resolved game's achievement set and the connected user's per-achievement progress.
/// Totals are derived from the individual entries so softcore unlocks always count toward the
/// primary presentation, even when only some of those unlocks were earned in hardcore mode.
/// </summary>
public sealed record RetroAchievementsGameDetails(
    int GameId,
    string Title,
    int AchievementCount,
    int NumAwarded,
    int NumAwardedHardcore,
    IReadOnlyList<RetroAchievementsAchievement> Achievements)
{
    public int TotalAchievements => Math.Max(AchievementCount, Achievements.Count);
    public int UnlockedAchievements => Achievements.Count(achievement => achievement.IsEarned);
    public int UnlockedHardcoreAchievements => Achievements.Count(achievement => achievement.IsHardcore);
    public int TotalPoints => Achievements.Sum(achievement => achievement.Points);
    public int EarnedPoints => Achievements
        .Where(achievement => achievement.IsEarned)
        .Sum(achievement => achievement.Points);
}

/// <summary>
/// Typed, cancellable client for the read-only RetroAchievements Web API endpoints EmuShelf
/// needs. Implementations must map authentication, offline, malformed-response, 429, and server
/// failures to distinct <see cref="RetroAchievementsRequestStatus"/> values and never emit the
/// API key into logs or a logged request URI.
/// </summary>
public interface IRetroAchievementsClient
{
    /// <summary>Validates credentials and returns the caller's own profile (RA console-agnostic).</summary>
    Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
        RetroAchievementsCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the achievement-bearing games and their hashes for one RA console id.</summary>
    Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
        RetroAchievementsCredentials credentials,
        int consoleId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns progress summaries for the given RA game ids in one batched call.</summary>
    Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
        RetroAchievementsCredentials credentials,
        IReadOnlyList<int> gameIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one game's achievement set plus the connected user's earned dates. This is the
    /// comparatively expensive detail endpoint, so callers must serve cached data first and
    /// request it only on an explicit refresh or after their detail-cache TTL expires.
    /// </summary>
    Task<RetroAchievementsResponse<RetroAchievementsGameDetails>> GetGameDetailsAsync(
        RetroAchievementsCredentials credentials,
        int gameId,
        CancellationToken cancellationToken = default);
}
