namespace EmuShelf.Infrastructure.Emulators;

/// <summary>
/// Fetches emulator release metadata and downloads release assets. A seam so the install service can be
/// unit tested end to end without a network call.
/// </summary>
public interface IEmulatorReleaseClient
{
    /// <summary>
    /// The newest release for an <c>owner/repo</c>, or null when it could not be fetched (offline,
    /// rate-limited, no release, or an unreadable response). Never throws for those cases.
    /// </summary>
    Task<GitHubEmulatorRelease?> GetLatestReleaseAsync(string repository, CancellationToken cancellationToken);

    /// <summary>Streams a release asset to <paramref name="destinationPath"/>, reporting 0..1 progress.</summary>
    Task DownloadAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
