namespace EmuShelf.Core.Metadata;

[Flags]
public enum GameScrapeCapability
{
    None = 0,
    Metadata = 1 << 0,
    BoxFront = 1 << 1,
    Screenshot = 1 << 2,
    Wheel = 1 << 3,
    Fanart = 1 << 4,
    Batch = 1 << 5,
    ManualTitleSearch = 1 << 6,
}

public enum GameScrapeProviderTrust
{
    /// <summary>Results are tied to stable game evidence such as a hash or serial.</summary>
    VerifiedIdentity,

    /// <summary>Results are unverified suggestions and require the user to choose one.</summary>
    UserReviewedSearch,
}

public sealed record GameScrapeProviderDescriptor(
    string Id,
    string DisplayName,
    GameScrapeCapability Capabilities,
    GameScrapeProviderTrust Trust,
    bool RequiresAuthentication);

public interface IGameScrapeProviderRegistry
{
    IReadOnlyList<GameScrapeProviderDescriptor> All { get; }

    bool TryGet(string providerId, out GameScrapeProviderDescriptor? provider);
}

/// <summary>
/// Capability registry shared by automatic enrichment and user-initiated artwork search.
/// Provider implementations remain separate because their trust and result shapes differ.
/// </summary>
public sealed class GameScrapeProviderRegistry : IGameScrapeProviderRegistry
{
    private readonly IReadOnlyDictionary<string, GameScrapeProviderDescriptor> _byId;

    public GameScrapeProviderRegistry(IEnumerable<GameScrapeProviderDescriptor> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var all = providers.ToArray();
        if (all.Any(provider => string.IsNullOrWhiteSpace(provider.Id)))
            throw new ArgumentException("Scraping provider IDs cannot be empty.", nameof(providers));

        var duplicate = all
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate scraping provider ID '{duplicate.Key}'.", nameof(providers));

        All = all;
        _byId = all.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<GameScrapeProviderDescriptor> All { get; }

    public bool TryGet(string providerId, out GameScrapeProviderDescriptor? provider)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            provider = null;
            return false;
        }

        return _byId.TryGetValue(providerId, out provider);
    }
}

public enum GameMetadataField
{
    Title,
    Developer,
    Publisher,
    Genre,
    Description,
    ReleaseDate,
    Players,
    Rating,
}

public enum GameMetadataValueOrigin
{
    Provider,
    User,
}

public enum GameMetadataApplyMode
{
    /// <summary>Write only when the field and locale have no value.</summary>
    FillMissing,

    /// <summary>Also refresh a value previously written by this same provider.</summary>
    RefreshProviderOwned,

    /// <summary>A direct user edit, which may replace any existing value.</summary>
    UserEdit,
}

public sealed record GameMetadataValue(
    long GameId,
    GameMetadataField Field,
    string Value,
    string? Locale,
    GameMetadataValueOrigin Origin,
    string? ProviderId,
    string? ProviderItemId,
    string? SourceUri,
    DateTimeOffset UpdatedAt);

public enum GameMediaKind
{
    BoxFront,
    Screenshot,
    Wheel,
    Fanart,
}

public enum GameMediaOrigin
{
    Provider,
    User,
}

public enum GameMediaSelectionOrigin
{
    Provider,
    User,
}

public sealed record GameMediaAsset(
    long Id,
    long GameId,
    GameMediaKind Kind,
    string LocalPath,
    bool IsSelected,
    GameMediaSelectionOrigin? SelectionOrigin,
    GameMediaOrigin Origin,
    string? ProviderId,
    string? ProviderItemId,
    string? SourceUri,
    string? Region,
    string? Language,
    string FileExtension,
    int? Width,
    int? Height,
    string? Crc32,
    string? Md5,
    string? Sha1,
    DateTimeOffset UpdatedAt);

public enum GameProviderMatchMethod
{
    Sha1,
    Md5,
    Crc32,
    Serial,
    ProviderGameId,
    UserSelectedTitleSearch,
}

public sealed record GameProviderMatch(
    long GameId,
    string ProviderId,
    string? ProviderSystemId,
    int? SystemMappingVersion,
    string? ProviderGameId,
    string? ProviderRomId,
    GameProviderMatchMethod MatchMethod,
    string? EvidenceValue,
    GameMetadataStatus Status,
    DateTimeOffset LastAttemptedAt,
    string? LastError);

public sealed record GameDetails(
    long GameId,
    IReadOnlyList<GameMetadataValue> Metadata,
    IReadOnlyList<GameMediaAsset> Media,
    IReadOnlyList<GameProviderMatch> ProviderMatches);

/// <summary>
/// A provider media candidate to download and import for a game. Provider-neutral so the apply
/// service does not depend on any one provider's response type.
/// </summary>
public sealed record GameMediaImport(
    GameMediaKind Kind,
    Uri SourceUri,
    string FileExtension,
    string ProviderId,
    string? ProviderItemId = null,
    string? Region = null,
    string? Language = null,
    int? Width = null,
    int? Height = null,
    string? Crc32 = null,
    string? Md5 = null,
    string? Sha1 = null);

/// <summary>
/// A request to apply a provider scrape result to a game: scalar/localized metadata values, media
/// to download and import, and the provider match to record. The apply service never mutates game
/// files; it only writes EmuShelf's own detail store, media library, and cover projection.
/// </summary>
public sealed record GameScrapeApplyRequest(
    long GameId,
    GameProviderMatch Match,
    IReadOnlyList<GameMetadataValue> Metadata,
    IReadOnlyList<GameMediaImport> Media,
    GameMetadataApplyMode Mode,
    bool ProjectBoxFrontToCover = true);

public enum GameMediaApplyOutcome
{
    /// <summary>The media was downloaded, imported, and recorded.</summary>
    Imported,

    /// <summary>Skipped because fill-missing mode found an existing active asset for this kind.</summary>
    SkippedExisting,

    /// <summary>Skipped because a user-owned or another provider's asset holds this kind.</summary>
    SkippedProtected,

    /// <summary>The candidate could not be downloaded or was not a valid image.</summary>
    DownloadFailed,
}

public sealed record GameMediaApplyResult(
    GameMediaKind Kind,
    GameMediaApplyOutcome Outcome,
    string? Error = null);

public sealed record GameScrapeApplyResult(
    long GameId,
    int MetadataApplied,
    int MetadataSkipped,
    IReadOnlyList<GameMediaApplyResult> Media,
    bool CoverProjected,
    string? Error = null)
{
    public int MediaImported => Media.Count(result => result.Outcome == GameMediaApplyOutcome.Imported);
}

/// <summary>
/// Owns field precedence, safe media import, cover projection, and provider-match persistence for a
/// single game. It applies only what the caller selected and never overwrites user-owned data.
/// </summary>
public interface IGameScrapeApplicationService
{
    Task<GameScrapeApplyResult> ApplyAsync(
        GameScrapeApplyRequest request,
        CancellationToken cancellationToken = default);
}

public enum GameScrapeBatchOutcome
{
    /// <summary>Matched and its selected values/media were written.</summary>
    Applied,

    /// <summary>Matched by hash/serial returned nothing to write (already filled, or empty result).</summary>
    NothingToApply,

    /// <summary>No hash/serial match. Batch never falls back to title search; this is left for manual scraping.</summary>
    NoMatch,

    /// <summary>The platform or file format cannot be identified for ScreenScraper.</summary>
    Unsupported,

    /// <summary>The game file was missing or changed, so it could not be identified.</summary>
    SourceProblem,

    /// <summary>An unexpected error scraping this one game; the batch continues.</summary>
    Failed,
}

public sealed record GameScrapeBatchItemResult(
    long GameId,
    string GameTitle,
    GameScrapeBatchOutcome Outcome,
    int FieldsApplied = 0,
    int MediaImported = 0,
    string? Error = null);

public sealed record GameScrapeBatchProgress(
    int Completed,
    int Total,
    string? CurrentGameTitle,
    GameScrapeBatchItemResult? LastResult = null);

/// <summary>Why a batch stopped — either it finished, or it halted early to fail safe.</summary>
public enum GameScrapeBatchStopReason
{
    Completed,
    Cancelled,
    NotConnected,
    ProviderDisabled,
    QuotaExhausted,
    RateLimited,
}

public sealed record GameScrapeBatchSummary(
    int Total,
    GameScrapeBatchStopReason StopReason,
    IReadOnlyList<GameScrapeBatchItemResult> Results)
{
    public int Applied => Results.Count(result => result.Outcome == GameScrapeBatchOutcome.Applied);
    public int NoMatch => Results.Count(result => result.Outcome == GameScrapeBatchOutcome.NoMatch);
    public int Unsupported => Results.Count(result => result.Outcome == GameScrapeBatchOutcome.Unsupported);
    public int Failed => Results.Count(result =>
        result.Outcome is GameScrapeBatchOutcome.Failed or GameScrapeBatchOutcome.SourceProblem);

    /// <summary>Games in the request that were never reached because the batch stopped early.</summary>
    public int NotProcessed => Total - Results.Count;
}
