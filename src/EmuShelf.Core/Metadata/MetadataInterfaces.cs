using EmuShelf.Core.Library;

namespace EmuShelf.Core.Metadata;

/// <summary>
/// System-specific, read-only identifier extraction. The caller runs this off the UI thread.
/// </summary>
public interface IGameIdentifierExtractor
{
    IReadOnlyList<GameIdentifier> Extract(Game game);
}

public interface IGameMetadataCatalog
{
    /// <summary>
    /// Resolves the best catalog entry for a game's identifiers. When one catalog key maps to
    /// several regional releases — as a region-free cartridge's shared serial does — the optional
    /// <paramref name="regionHint"/> (typically the game's filename, which carries a "(Europe)"-style
    /// tag) selects the matching region instead of an arbitrary one.
    /// </summary>
    Task<GameCatalogMatch?> FindMatchAsync(
        MetadataSystemProfile profile,
        IReadOnlyList<GameIdentifier> identifiers,
        string? regionHint = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Builds remote artwork candidates; it never downloads or stores artwork.</summary>
public interface IGameArtworkProvider
{
    string Id { get; }

    IReadOnlyList<ArtworkCandidate> GetCandidates(
        IReadOnlyList<GameIdentifier> identifiers,
        GameCatalogMatch? match);
}

/// <summary>Artwork provider that can build a candidate from a title found in its own index.</summary>
public interface IArtworkTitleIndexProvider : IGameArtworkProvider
{
    string ArtworkIndexKey { get; }

    IReadOnlyList<string> GetIndexedTitleQueries(GameCatalogMatch match);

    ArtworkCandidate CreateCandidate(string title);
}

/// <summary>Resolves verified catalog titles against a provider's remotely maintained title index.</summary>
public interface IGameArtworkTitleIndex
{
    Task<IReadOnlyList<ArtworkCandidate>> FindCandidatesAsync(
        IArtworkTitleIndexProvider provider,
        GameCatalogMatch match,
        CancellationToken cancellationToken = default);
}

public interface IRemoteArtworkDownloader
{
    Task<DownloadedArtwork?> DownloadFirstAsync(
        IReadOnlyList<ArtworkCandidate> candidates,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// SQLite persistence for extracted evidence, provider provenance, and conservative
/// metadata updates. This is separate from path-based library identity.
/// </summary>
public interface IGameMetadataStore
{
    Game? GetGame(long gameId);

    IReadOnlyList<Game> GetGamesMissingMetadata(string? systemId = null);

    IReadOnlyList<GameIdentifier> GetIdentifiers(long gameId);

    /// <summary>
    /// Every stored identifier, grouped by game id, in one query. Callers that need identifiers
    /// for the whole library — texture-pack matching, for example — use this instead of calling
    /// <see cref="GetIdentifiers"/> per row.
    /// </summary>
    IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> GetAllIdentifiers();

    /// <summary>
    /// Every scraped canonical title, by game id, in one query. The gamepad spotlight list and hero
    /// show these instead of the filename-derived title, without rewriting the game record; games
    /// with no scraped title are absent. Defaults to empty for stores that hold no metadata.
    /// </summary>
    IReadOnlyDictionary<long, string> GetProviderTitles() => new Dictionary<long, string>();

    void ReplaceIdentifiers(long gameId, IReadOnlyList<GameIdentifier> identifiers);

    bool TryApplyCatalogTitle(long gameId, string canonicalTitle, string filenameTitle);

    bool TryApplyDownloadedCover(
        long gameId,
        string coverPath,
        string providerId,
        string sourceUri);

    void RecordAttempt(GameMetadataAttempt attempt);
}

/// <summary>
/// On-demand game details, media choices, and provider matches. These records stay out of the
/// hot <see cref="Game"/> library projection so adding rich scraping does not slow the grid.
/// </summary>
public interface IGameDetailsStore
{
    GameDetails GetDetails(long gameId);

    /// <summary>Every game's list-view metadata projection, by game id, in a small fixed number of
    /// queries (never one per game). Games with no stored details are absent. Mirrors
    /// <see cref="IGameMetadataStore.GetAllIdentifiers"/>. Defaults to empty for stores that hold no details.</summary>
    IReadOnlyDictionary<long, GameDetailsProjection> GetAllDetailsProjections() =>
        new Dictionary<long, GameDetailsProjection>();

    bool TryApplyMetadata(GameMetadataValue value, GameMetadataApplyMode mode);

    GameMediaAsset SaveMedia(GameMediaAsset media);

    bool SelectMedia(long gameId, GameMediaKind kind, long mediaId);

    void UpsertProviderMatch(GameProviderMatch match);
}
