namespace EmuShelf.Core.Metadata;

/// <summary>
/// How a platform's catalog file is laid out. The console DATs are clrmamepro's native text
/// format; the FinalBurn Neo arcade DAT is Logiqx XML, where the game <c>name</c> attribute is the
/// romset key and the human title lives in a <c>description</c> element.
/// </summary>
public enum DatFormat
{
    ClrMameProText,
    LogiqxXml,
}

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
    bool ReadRomSerials = false,
    DatFormat CatalogFormat = DatFormat.ClrMameProText,
    long? MaxCatalogBytes = null)
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
