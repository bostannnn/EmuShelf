namespace EmuShelf.Core.Metadata;

/// <summary>
/// Declarative metadata support for one stable system id. Adding a platform should
/// require registering another profile, not changing the enrichment coordinator.
/// </summary>
public sealed record MetadataSystemProfile(
    string SystemId,
    GameIdentifierKind CatalogKeyKind,
    Uri CatalogUri,
    IGameIdentifierExtractor IdentifierExtractor,
    IReadOnlyList<IGameArtworkProvider> ArtworkProviders);
