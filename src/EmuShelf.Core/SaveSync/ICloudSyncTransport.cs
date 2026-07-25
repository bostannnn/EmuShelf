namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The cloud side of save sync: list, download, and upload opaque per-unit payloads. It is
/// deliberately copy-only — there is no delete — so a reconciliation bug can never remove the
/// only copy of a save. The v1 implementation drives an external, user-owned rclone remote.
/// </summary>
public interface ICloudSyncTransport
{
    /// <summary>Whether a cloud remote is currently configured and reachable.</summary>
    Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default);

    /// <summary>A snapshot of every unit currently stored on the remote.</summary>
    Task<IReadOnlyList<SaveUnitSnapshot>> ListAsync(CancellationToken cancellationToken = default);

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
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits everything queued by <see cref="UploadAsync"/> since the last flush. The rclone
    /// implementation stages uploads locally and pushes them here in a single rclone session, so a
    /// whole sync paces itself against the provider's rate limits instead of making one call per
    /// save. Called once at the end of a sync.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
