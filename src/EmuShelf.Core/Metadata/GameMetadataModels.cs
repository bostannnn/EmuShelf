namespace EmuShelf.Core.Metadata;

public enum GameMetadataStatus
{
    Pending,
    Matched,
    Partial,
    Unmatched,
    Failed,
}

public sealed record GameCatalogMatch(
    string CatalogId,
    string CatalogEntryId,
    string CanonicalTitle,
    string? Region);

/// <summary>Whether a remote media candidate is a still image or a video. This selects the
/// downloader's content-type allow-list, size cap, and file-signature check.</summary>
public enum RemoteMediaKind
{
    Image,
    Video,
}

public sealed record ArtworkCandidate(
    string ProviderId,
    Uri SourceUri,
    string FileExtension,
    RemoteMediaKind MediaKind = RemoteMediaKind.Image);

public sealed record DownloadedArtwork(
    ArtworkCandidate Candidate,
    string TemporaryPath);

public sealed record GameMetadataAttempt(
    long GameId,
    GameMetadataStatus Status,
    GameCatalogMatch? Match,
    string? CoverProviderId,
    string? CoverSourceUri,
    string? Error,
    DateTimeOffset AttemptedAt);
