using EmuShelf.Core.Library;

namespace EmuShelf.Core.Metadata.ScreenScraper;

public enum ScreenScraperFingerprintScope
{
    WholeFile,
}

public sealed record ScreenScraperFingerprintProfile(
    string SystemId,
    IReadOnlySet<string> WholeFileExtensions);

public sealed record ScreenScraperSystemProfile(
    string SystemId,
    int ProviderSystemId,
    int MappingVersion,
    ScreenScraperFingerprintProfile FingerprintProfile);

public sealed record GameFileFingerprint(
    long GameId,
    string ProviderId,
    string SourcePath,
    ScreenScraperFingerprintScope Scope,
    long FileSize,
    DateTimeOffset LastWriteAt,
    string Crc32,
    string Md5,
    string Sha1,
    DateTimeOffset ComputedAt);

public enum ScreenScraperFingerprintStatus
{
    Cached,
    Computed,
    ConsentRequired,
    UnsupportedFormat,
    SourceMissing,
    SourceChanged,
    ReadFailed,
}

public sealed record ScreenScraperFingerprintResult(
    ScreenScraperFingerprintStatus Status,
    GameFileFingerprint? Fingerprint,
    string? Error)
{
    public bool IsSuccess => Fingerprint is not null &&
        Status is ScreenScraperFingerprintStatus.Cached or ScreenScraperFingerprintStatus.Computed;
}

public interface IGameFileFingerprintStore
{
    GameFileFingerprint? Get(long gameId, string providerId);

    void Upsert(GameFileFingerprint fingerprint);

    void Remove(long gameId, string providerId);
}

public interface IScreenScraperFingerprintService
{
    Task<ScreenScraperFingerprintResult> GetOrComputeAsync(
        Game game,
        ScreenScraperFingerprintProfile profile,
        bool allowCompute,
        CancellationToken cancellationToken = default);
}
