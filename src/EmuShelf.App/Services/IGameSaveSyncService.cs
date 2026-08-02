namespace EmuShelf.App.Services;

/// <summary>Synchronizes the save provider associated with a launched game's system.</summary>
public interface IGameSaveSyncService
{
    bool CanSyncSystem(string systemId);

    Task<CloudSaveSyncOutcome> SyncSystemAsync(
        string systemId,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? launchStateKeys = null);
}
