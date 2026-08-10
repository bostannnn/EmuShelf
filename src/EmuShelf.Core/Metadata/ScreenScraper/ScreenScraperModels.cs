namespace EmuShelf.Core.Metadata.ScreenScraper;

public static class ScreenScraperProvider
{
    public const string Id = "screenscraper";
}

/// <summary>Build-provisioned application identity approved by ScreenScraper.</summary>
public sealed record ScreenScraperDeveloperCredentials(
    string DeveloperId,
    string DeveloperPassword,
    string SoftwareName);

/// <summary>
/// Developer-only debug parameters. This is never populated by user-facing flows: it forces
/// cache updates, IP/level, and quota counters to validate behavior against the live API, and is
/// capped by ScreenScraper at 100 uses per day. <see cref="DebugPassword"/> is a secret and must
/// be redacted from every log, exception, and diagnostic exactly like the developer password.
/// </summary>
public sealed record ScreenScraperDebugOptions(
    string DebugPassword,
    bool ForceUpdate = false,
    string? ForceIp = null,
    int? ForceLevel = null,
    int? ForceRequestOk = null,
    int? ForceRequestKo = null,
    int? ForceRequestMin = null);

/// <summary>In-memory account credentials. These must never be serialized into AppSettings.</summary>
public sealed record ScreenScraperUserCredentials(string Username, string Password);

/// <summary>
/// Platform-protected account storage. User credentials never belong in settings.json, database
/// records, request logs, or exception messages.
/// </summary>
public interface IScreenScraperCredentialStore
{
    ScreenScraperUserCredentials? GetCredentials();

    void SaveCredentials(ScreenScraperUserCredentials credentials);

    void ClearCredentials();
}

public sealed record ScreenScraperGameRequest(
    int SystemId,
    string RomName,
    long RomSize,
    string? Crc32 = null,
    string? Md5 = null,
    string? Sha1 = null,
    string? Serial = null,
    string? ProviderGameId = null,
    string? Language = null,
    // A deliberate file-name-only lookup: the caller vouches that this system is matched by ROM
    // file name (arcade romsets), so a lookup with no hash/serial/game id is intentional and safe.
    // Off by default so a stray under-specified request for a hashable system is still rejected.
    bool AllowFileNameMatch = false);

public enum ScreenScraperRequestStatus
{
    Success,
    AuthenticationFailed,
    NotFound,
    ServiceUnavailable,
    ClientUpdateRequired,
    RateLimited,
    DailyQuotaExceeded,
    FailedLookupQuotaExceeded,
    ApiRejected,
    InvalidResponse,
    NetworkError,
}

public sealed record ScreenScraperQuota(
    int? MaxThreads,
    int? RequestsToday,
    int? MaxRequestsPerDay,
    int? FailedRequestsToday,
    int? MaxFailedRequestsPerDay,
    int? MaxDownloadSpeed);

public sealed record ScreenScraperResult<T>(
    ScreenScraperRequestStatus Status,
    T? Data,
    ScreenScraperQuota? Quota,
    string? Error)
{
    public bool IsSuccess => Status == ScreenScraperRequestStatus.Success && Data is not null;
}

public sealed record ScreenScraperAccountInfo(
    string? UserId,
    string? Username,
    string? Tier,
    ScreenScraperQuota Quota);

public sealed record ScreenScraperLocalizedText(
    string Value,
    string? Language,
    string? Region);

public sealed record ScreenScraperReleaseDate(string Value, string? Region);

public sealed record ScreenScraperMediaCandidate(
    string MediaType,
    Uri SourceUri,
    string FileExtension,
    string? ProviderMediaId,
    string? Region,
    string? Language,
    int? Width,
    int? Height,
    long? Size,
    string? Crc32,
    string? Md5,
    string? Sha1);

public sealed record ScreenScraperGameInfo(
    string ProviderGameId,
    string? ProviderRomId,
    string? RomCrc32,
    string? RomMd5,
    string? RomSha1,
    IReadOnlyList<ScreenScraperLocalizedText> Names,
    IReadOnlyList<ScreenScraperLocalizedText> Descriptions,
    IReadOnlyList<ScreenScraperLocalizedText> Genres,
    IReadOnlyList<ScreenScraperReleaseDate> ReleaseDates,
    string? Developer,
    string? Publisher,
    string? Players,
    string? Rating,
    IReadOnlyList<ScreenScraperMediaCandidate> Media);

/// <summary>One entry from <c>systemesListe.php</c>, used to validate the EmuShelf system map.</summary>
public sealed record ScreenScraperSystem(
    int Id,
    string? Name,
    IReadOnlyList<string> Names);

/// <summary>A ranked candidate from <c>jeuRecherche.php</c> for the manual title-search fallback.</summary>
public sealed record ScreenScraperGameMatch(
    string ProviderGameId,
    string Name,
    string? System);

public interface IScreenScraperClient
{
    Task<ScreenScraperResult<ScreenScraperAccountInfo>> GetAccountInfoAsync(
        ScreenScraperUserCredentials userCredentials,
        CancellationToken cancellationToken = default);

    Task<ScreenScraperResult<ScreenScraperGameInfo>> GetGameInfoAsync(
        ScreenScraperUserCredentials userCredentials,
        ScreenScraperGameRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the ScreenScraper system catalogue for auditing the EmuShelf system map.</summary>
    Task<ScreenScraperResult<IReadOnlyList<ScreenScraperSystem>>> GetSystemsAsync(
        ScreenScraperUserCredentials userCredentials,
        CancellationToken cancellationToken = default);

    /// <summary>Ranked title-search candidates (<c>jeuRecherche.php</c>) for the manual fallback.</summary>
    Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchGamesAsync(
        ScreenScraperUserCredentials userCredentials,
        int systemId,
        string query,
        CancellationToken cancellationToken = default);
}

public enum ScreenScraperPreviewStatus
{
    Success,
    ProviderDisabled,
    NotConnected,
    LibraryGameMissing,
    UnsupportedSystem,
    FingerprintConsentRequired,
    UnsupportedFormat,
    SourceMissing,
    SourceChanged,
    FingerprintFailed,
    ProviderFailure,
}

public sealed record ScreenScraperGamePreview(
    long GameId,
    GameProviderMatch Match,
    IReadOnlyList<GameMetadataValue> Metadata,
    IReadOnlyDictionary<GameMediaKind, ScreenScraperMediaCandidate> Media,
    GameDetails ExistingDetails,
    ScreenScraperQuota? Quota,
    ScreenScraperFingerprintStatus? FingerprintStatus,
    // The media kind projected to the cover for this game's system (arcade -> title screen).
    GameMediaKind CoverKind = GameMediaKind.BoxFront);

public sealed record ScreenScraperPreviewResult(
    ScreenScraperPreviewStatus Status,
    ScreenScraperGamePreview? Preview,
    ScreenScraperRequestStatus? RequestStatus,
    string? Error)
{
    public bool IsSuccess => Status == ScreenScraperPreviewStatus.Success && Preview is not null;
}

public interface IScreenScraperPreviewService
{
    Task<ScreenScraperPreviewResult> PreviewAsync(
        long gameId,
        Settings.ScreenScraperSettings settings,
        bool allowFingerprinting,
        CancellationToken cancellationToken = default);

    /// <summary>Ranked title-search candidates for the manual fallback when no exact match is found.</summary>
    Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchAsync(
        long gameId,
        string query,
        Settings.ScreenScraperSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>Builds a preview for a user-chosen title-search result, keyed by its provider game id.</summary>
    Task<ScreenScraperPreviewResult> PreviewByProviderGameIdAsync(
        long gameId,
        string providerGameId,
        Settings.ScreenScraperSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Scrapes many games in one pass. It matches only by hash, serial, or (for arcade) ROM file name —
/// each a single deterministic request per game, never the multi-request title search, so a rom-hack
/// heavy run cannot exhaust the failed-lookup quota. It applies with the caller's overwrite mode and
/// field/media selection, reports cancellable progress, and stops cleanly on quota exhaustion — leaving
/// completed work intact and the run resumable (fill-missing skips games already done).
/// </summary>
public interface IScreenScraperBatchService
{
    Task<GameScrapeBatchSummary> RunAsync(
        IReadOnlyList<long> gameIds,
        Settings.ScreenScraperSettings settings,
        GameMetadataApplyMode mode,
        IReadOnlySet<GameMetadataField>? includeFields,
        IReadOnlySet<GameMediaKind>? includeMedia,
        IProgress<GameScrapeBatchProgress>? progress,
        CancellationToken cancellationToken = default);
}
