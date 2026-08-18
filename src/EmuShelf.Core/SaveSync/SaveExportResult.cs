namespace EmuShelf.Core.SaveSync;

/// <summary>The outcome of a save export.</summary>
/// <param name="Status">Whether the export completed, found nothing, was unconfigured, or failed.</param>
/// <param name="SavesExported">How many save units were written into the archive.</param>
/// <param name="FromCloud">How many of those came only from the cloud (a subset of <paramref name="SavesExported"/>).</param>
/// <param name="TotalBytes">The total size of the exported save content (uncompressed).</param>
/// <param name="DestinationPath">Where the archive was written, when the export completed.</param>
/// <param name="Message">A user-facing failure message when the export failed; otherwise null.</param>
/// <param name="Skipped">
/// Human-readable notes about units that were present in the cloud but could not be exported
/// (no owning platform, or not resolvable on this machine).
/// </param>
public sealed record SaveExportResult(
    SaveExportStatus Status,
    int SavesExported,
    int FromCloud,
    long TotalBytes,
    string? DestinationPath,
    string? Message,
    IReadOnlyList<string> Skipped)
{
    public static SaveExportResult Completed(
        string destinationPath,
        int savesExported,
        int fromCloud,
        long totalBytes,
        IReadOnlyList<string> skipped) =>
        new(SaveExportStatus.Completed, savesExported, fromCloud, totalBytes, destinationPath, null, skipped);

    public static SaveExportResult NothingToExport(IReadOnlyList<string>? skipped = null) =>
        new(SaveExportStatus.NothingToExport, 0, 0, 0, null, null, skipped ?? []);

    public static SaveExportResult NotConfigured() =>
        new(SaveExportStatus.NotConfigured, 0, 0, 0, null, null, []);

    public static SaveExportResult Failed(string message) =>
        new(SaveExportStatus.Failed, 0, 0, 0, null, message, []);
}

/// <summary>The outcome category of a save export.</summary>
public enum SaveExportStatus
{
    /// <summary>The export ran and wrote an archive.</summary>
    Completed,

    /// <summary>No saves were found to export; no archive was written.</summary>
    NothingToExport,

    /// <summary>A cloud export was requested but no cloud remote is connected.</summary>
    NotConfigured,

    /// <summary>The export was attempted but failed; nothing usable was written.</summary>
    Failed,
}
