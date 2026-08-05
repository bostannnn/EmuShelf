namespace EmuShelf.Core.Updates;

/// <summary>
/// Checks GitHub Releases for a newer EmuShelf build and downloads the matching portable artifact,
/// verifying it against the release's published SHA-256 before it is ever used. Applying the staged
/// update and relaunching is the separate, platform-specific concern of <see cref="IUpdateApplier"/>.
/// </summary>
public interface IUpdateService
{
    /// <summary>Reads the latest release and reports whether it is newer than the running build.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the update artifact to the portable Cache directory and verifies its SHA-256 against
    /// the release's checksum file. Throws if the download or verification fails — a mismatched file
    /// is deleted and never returned.
    /// </summary>
    Task<StagedUpdate> DownloadAndStageAsync(
        UpdateCheckResult.UpdateAvailable update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
