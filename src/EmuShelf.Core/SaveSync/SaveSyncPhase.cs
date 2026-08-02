namespace EmuShelf.Core.SaveSync;

/// <summary>Which part of a sync is running.</summary>
public enum SaveSyncPhase
{
    /// <summary>Comparing units and staging what has to move. Progress counts units.</summary>
    Reconciling,

    /// <summary>
    /// Moving the staged data to the cloud. Uploads are staged locally and transferred once at the
    /// end, so this is where a large sync actually spends its time — counting units here would sit
    /// at "finished" while the transfer runs.
    /// </summary>
    Transferring,
}
