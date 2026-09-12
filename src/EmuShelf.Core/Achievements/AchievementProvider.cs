namespace EmuShelf.Core.Achievements;

public sealed record AchievementGameRef(string Provider, string GameId);

/// <summary>Unknown progress is distinct from a known locked achievement.</summary>
public sealed record AchievementEntry(
    string Id, string Title, string Description, string Icon, int DisplayOrder,
    bool? IsUnlocked, DateTimeOffset? EarnedAt = null, int? Points = null,
    bool IsHardcore = false, bool IsHidden = false);

public sealed record AchievementSnapshot(
    AchievementGameRef Game, string AccountId, string Title,
    IReadOnlyList<AchievementEntry> Achievements, DateTimeOffset RefreshedAt,
    bool ProgressKnown = true);

public enum AchievementStatus
{
    Success, NotConnected, AuthenticationFailed, Unavailable, Offline, RateLimited,
    ServerError, MalformedResponse, AccountChanged,
}

public sealed record AchievementResult(AchievementStatus Status, AchievementSnapshot? Snapshot = null,
    TimeSpan? RetryAfter = null)
{
    public bool IsSuccess => Status == AchievementStatus.Success && Snapshot is not null;
}

/// <summary>Read-only provider boundary shared by all achievement presentations.</summary>
public interface IAchievementProvider
{
    string Id { get; }
    string DisplayName { get; }
    string? AccountId { get; }
    bool IsConnected { get; }
    bool SupportsPoints { get; }
    bool SupportsHardcore { get; }
    AchievementSnapshot? GetCached(AchievementGameRef game);
    Task<AchievementResult> RefreshAsync(AchievementGameRef game, CancellationToken cancellationToken = default,
        bool manual = false);
    Task<string?> GetIconPathAsync(string icon, CancellationToken cancellationToken = default);
    event Action? Changed;
}

public interface ISteamCredentialStore
{
    bool IsPersistent { get; }
    string? Read();
    void Write(string key);
    void Clear();
}

public sealed record SteamProfile(string SteamId, string DisplayName);
public sealed record SteamProfileResult(AchievementStatus Status, SteamProfile? Profile = null);

public interface ISteamAchievementsClient
{
    Task<SteamProfileResult> ResolveProfileAsync(string profile, string apiKey, CancellationToken cancellationToken);
    Task<AchievementResult> GetAchievementsAsync(string steamId, string appId, string apiKey,
        CancellationToken cancellationToken);
}

public sealed record SteamAchievementSyncProgress(int Completed, int Total, int Updated, int Unavailable);
public sealed record SteamAchievementSyncResult(int Completed, int Total, int Updated, int Unavailable,
    AchievementStatus Status);
