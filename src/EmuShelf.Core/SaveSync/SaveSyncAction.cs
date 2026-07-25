namespace EmuShelf.Core.SaveSync;

/// <summary>The direction a single save unit should move to reconcile local and remote.</summary>
public enum SaveSyncAction
{
    /// <summary>Both sides already agree; nothing to copy.</summary>
    None,

    /// <summary>Copy the local unit up to the remote.</summary>
    Upload,

    /// <summary>Copy the remote unit down to the local machine.</summary>
    Download,

    /// <summary>Both sides changed; the local copy is newer and wins, and the remote copy is kept as a backup.</summary>
    ConflictLocalWins,

    /// <summary>Both sides changed; the remote copy is newer and wins, and the local copy is kept as a backup.</summary>
    ConflictRemoteWins,
}
