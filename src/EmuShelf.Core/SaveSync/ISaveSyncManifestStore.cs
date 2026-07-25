namespace EmuShelf.Core.SaveSync;

/// <summary>Loads and persists the per-unit sync baseline manifest.</summary>
public interface ISaveSyncManifestStore
{
    Task<SaveSyncManifest> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(SaveSyncManifest manifest, CancellationToken cancellationToken = default);
}
