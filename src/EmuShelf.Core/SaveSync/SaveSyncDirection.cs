namespace EmuShelf.Core.SaveSync;

/// <summary>The direction of a manual, baseline-ignoring overwrite.</summary>
public enum SaveSyncDirection
{
    /// <summary>Copy local saves up to the cloud, overwriting the cloud copy.</summary>
    Upload,

    /// <summary>Copy cloud saves down to the local machine, overwriting the local copy.</summary>
    Download,
}
