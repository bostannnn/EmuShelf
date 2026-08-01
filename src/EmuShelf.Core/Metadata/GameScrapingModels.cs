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
