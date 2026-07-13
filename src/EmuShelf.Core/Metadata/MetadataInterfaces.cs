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
    Task<GameCatalogMatch?> FindMatchAsync(
        MetadataSystemProfile profile,
        IReadOnlyList<GameIdentifier> identifiers,
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

    void ReplaceIdentifiers(long gameId, IReadOnlyList<GameIdentifier> identifiers);

    bool TryApplyCatalogTitle(long gameId, string canonicalTitle, string filenameTitle);

    bool TryApplyDownloadedCover(
        long gameId,
        string coverPath,
        string providerId,
        string sourceUri);

    void RecordAttempt(GameMetadataAttempt attempt);
}
