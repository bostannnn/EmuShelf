using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>Holds the sync manifest in memory across passes, like a persisted store would.</summary>
internal sealed class InMemorySaveSyncManifestStore : ISaveSyncManifestStore
{
    private SaveSyncManifest _manifest = new();

    public int Saves { get; private set; }

    public SaveSyncManifest Current => _manifest;

    public Task<SaveSyncManifest> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_manifest);

    public Task SaveAsync(SaveSyncManifest manifest, CancellationToken cancellationToken = default)
    {
        _manifest = manifest;
        Saves++;
        return Task.CompletedTask;
    }
}
