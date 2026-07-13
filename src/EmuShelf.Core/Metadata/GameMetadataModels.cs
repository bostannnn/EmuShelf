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

public sealed record ArtworkCandidate(
    string ProviderId,
    Uri SourceUri,
    string FileExtension);

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
