namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The cloud side of save sync: list, download, and upload opaque per-unit payloads. It is
/// deliberately copy-only — there is no delete — so a reconciliation bug can never remove the
/// only copy of a save. The v1 implementation drives an external, user-owned rclone remote.
/// </summary>
public interface ICloudSyncTransport
{
    /// <summary>A snapshot of every unit currently stored on the remote.</summary>
    Task<IReadOnlyList<SaveUnitSnapshot>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Announces every unit this reconciliation may download, before the first
    /// <see cref="DownloadAsync"/>. A transport that fetches in one session can scope that session
    /// to these units instead of the whole remote; one that cannot may ignore it. Implementations
    /// must still serve a download that was not announced.
    /// </summary>
    void ExpectDownloads(IEnumerable<string> unitIds)
    {
    }

    /// <summary>Opens the remote payload for a unit for reading.</summary>
    Task<Stream> DownloadAsync(string unitId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes (creating or overwriting) the remote payload for a unit. The caller supplies the
    /// content hash it recorded locally so the remote reports exactly that hash on the next
    /// <see cref="ListAsync"/>, keeping change detection stable across machines.
    /// </summary>
    Task UploadAsync(
        string unitId,
        Stream content,
        string contentHash,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default,
        string? compatibility = null);

    /// <summary>
    /// Commits everything queued by <see cref="UploadAsync"/> since the last flush. The rclone
    /// implementation stages uploads locally and pushes them here in a single rclone session, so a
    /// whole sync paces itself against the provider's rate limits instead of making one call per
    /// save. Called once at the end of a sync.
    ///
    /// This is where a sync that moves real data spends its time, so implementations report the
    /// transfer's progress as it goes; a caller that does not care passes null.
    ///
    /// Implementations that can commit incrementally should: a flush that only becomes durable at
    /// the very end makes every interrupted pass lose all of its uploads and re-send them next
    /// time, which on a slow provider is the difference between a sync that converges and one that
    /// never does.
    /// </summary>
    Task FlushAsync(
        IProgress<SaveTransferProgress>? transferProgress = null,
        CancellationToken cancellationToken = default);
}
