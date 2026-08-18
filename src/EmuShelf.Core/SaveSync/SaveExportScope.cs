namespace EmuShelf.Core.SaveSync;

/// <summary>What a save export gathers.</summary>
public enum SaveExportScope
{
    /// <summary>Only the saves currently present on this machine.</summary>
    Device,

    /// <summary>
    /// This machine's saves plus any that exist only in the connected cloud remote. Where a save
    /// exists on both sides the device copy is exported; the cloud contributes only what is missing
    /// locally.
    /// </summary>
    DeviceAndCloud,
}
