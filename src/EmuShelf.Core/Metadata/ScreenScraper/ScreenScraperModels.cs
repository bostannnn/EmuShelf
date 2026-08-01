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
    string? Language = null);

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
    IReadOnlyList<ScreenScraperLocalizedText> Names,
    IReadOnlyList<ScreenScraperLocalizedText> Descriptions,
    IReadOnlyList<ScreenScraperLocalizedText> Genres,
    IReadOnlyList<ScreenScraperReleaseDate> ReleaseDates,
    string? Developer,
    string? Publisher,
    string? Players,
    string? Rating,
    IReadOnlyList<ScreenScraperMediaCandidate> Media);

public interface IScreenScraperClient
{
    Task<ScreenScraperResult<ScreenScraperAccountInfo>> GetAccountInfoAsync(
        ScreenScraperUserCredentials userCredentials,
        CancellationToken cancellationToken = default);

    Task<ScreenScraperResult<ScreenScraperGameInfo>> GetGameInfoAsync(
        ScreenScraperUserCredentials userCredentials,
        ScreenScraperGameRequest request,
        CancellationToken cancellationToken = default);
}
