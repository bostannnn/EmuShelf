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
    IReadOnlyList<IGameArtworkProvider> ArtworkProviders,
    IReadOnlyList<GameIdentifierKind>? FallbackCatalogKeyKinds = null,
    bool ReadRomSerials = false)
{
    /// <summary>
    /// Ordered catalogue keys. The first is authoritative; later keys are used only if no exact
    /// match exists for an earlier kind.
    /// </summary>
    public IReadOnlyList<GameIdentifierKind> CatalogKeyKinds =>
        FallbackCatalogKeyKinds is { Count: > 0 }
            ? [CatalogKeyKind, .. FallbackCatalogKeyKinds]
            : [CatalogKeyKind];
}
