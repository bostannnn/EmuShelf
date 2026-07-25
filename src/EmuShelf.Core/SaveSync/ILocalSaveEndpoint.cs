namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The local side of save sync for one emulator: read a unit's current content and hash, write
/// an incoming unit, and preserve a superseded copy under the portable conflict-backup area.
/// Implementations touch only save data — never game files or emulator configuration.
/// </summary>
public interface ILocalSaveEndpoint
{
    /// <summary>The current state of a unit on disk, or null when it does not exist locally.</summary>
    Task<SaveUnitSnapshot?> SnapshotAsync(string unitId, CancellationToken cancellationToken = default);

    /// <summary>Opens the local payload for a unit for reading.</summary>
    Task<Stream> ReadAsync(string unitId, CancellationToken cancellationToken = default);

    /// <summary>Writes (creating or overwriting) the local unit from incoming content.</summary>
    Task WriteAsync(
        string unitId,
        Stream content,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Copies the current local unit into the timestamped conflict-backup area before it is overwritten.</summary>
    Task BackupLocalAsync(string unitId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Writes incoming (losing) content into the conflict-backup area without touching the live unit.</summary>
    Task BackupIncomingAsync(
        string unitId,
        Stream content,
        string reason,
        CancellationToken cancellationToken = default);
}
