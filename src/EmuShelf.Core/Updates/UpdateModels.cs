namespace EmuShelf.Core.Updates;

/// <summary>One downloadable file attached to a GitHub release.</summary>
public sealed record UpdateAsset(string Name, string DownloadUrl, long SizeBytes);

/// <summary>The newest published release, as read from the GitHub Releases API.</summary>
public sealed record ReleaseInfo(
    string TagName,
    SemanticVersion Version,
    string? Notes,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<UpdateAsset> Assets);

/// <summary>
/// The outcome of checking GitHub for a newer release. A closed hierarchy so callers must handle
/// each case: a newer build to offer, nothing to do, or a check that could not complete.
/// </summary>
public abstract record UpdateCheckResult
{
    // Private so the only subtypes are the ones nested here; nested records may still derive from it.
    private UpdateCheckResult() { }

    /// <summary>Running the newest release (or newer — a local dev build).</summary>
    public sealed record UpToDate(SemanticVersion Current) : UpdateCheckResult;

    /// <summary>A newer release exists and a matching artifact for this platform was found.</summary>
    public sealed record UpdateAvailable(
        SemanticVersion Version,
        string TagName,
        string? Notes,
        UpdateAsset Payload,
        UpdateAsset Checksum) : UpdateCheckResult;

    /// <summary>The check could not complete — offline, rate-limited, or no artifact for this build.</summary>
    public sealed record CheckFailed(string Reason) : UpdateCheckResult;
}

/// <summary>A downloaded, checksum-verified update file ready to be applied and relaunched.</summary>
public sealed record StagedUpdate(SemanticVersion Version, string PayloadPath);
