namespace EmuShelf.Core.Metadata;

/// <summary>A user-selectable image result. Search results are never applied automatically.</summary>
public sealed record ArtworkSearchResult(
    string ProviderId,
    Uri ImageUri,
    Uri ThumbnailUri,
    Uri? SourcePageUri,
    int Width,
    int Height,
    string Title,
    string FileExtension)
{
    public double AspectRatio => Height > 0 ? Width / (double)Height : 0;

    public ArtworkCandidate OriginalCandidate =>
        new(ProviderId, ImageUri, FileExtension);

    public ArtworkCandidate ThumbnailCandidate =>
        new($"{ProviderId}-preview", ThumbnailUri, FileExtension);
}

/// <summary>
/// Searches the web for covers for an explicit, user-driven picker. This is deliberately
/// separate from automatic metadata enrichment: an unverified web result must never replace a
/// game cover without the user choosing it.
/// </summary>
public interface IGameArtworkSearchProvider
{
    Task<IReadOnlyList<ArtworkSearchResult>> SearchAsync(
        string title,
        string systemName,
        double preferredAspectRatio,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Decides whether an untrusted web-artwork address is safe to contact. Implementations must
/// validate host names as well as literal IP addresses so redirects cannot reach local services.
/// </summary>
public interface IRemoteArtworkUriPolicy
{
    Task<bool> IsAllowedAsync(Uri uri, CancellationToken cancellationToken = default);
}
