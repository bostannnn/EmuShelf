namespace EmuShelf.Core.Settings;

/// <summary>
/// Portable cloud save-sync configuration. Holds no secret: the OAuth token lives only in rclone's
/// own config file, never here. Empty until the user connects a remote.
/// </summary>
public sealed record CloudSaveSyncSettings
{
    /// <summary>Whether save sync is turned on.</summary>
    public bool Enabled { get; init; }

    /// <summary>The rclone remote name (e.g. <c>emushelf-gdrive</c>). Not a secret. Null until connected.</summary>
    public string? RemoteName { get; init; }

    /// <summary>The folder within the remote that holds EmuShelf saves (e.g. <c>EmuShelf/Saves</c>).</summary>
    public string? CloudFolder { get; init; }

    /// <summary>
    /// The PCSX2 configuration directory (where <c>PCSX2.ini</c> lives) used to locate memory cards.
    /// Null until the user points EmuShelf at their PCSX2 install.
    /// </summary>
    public string? Pcsx2ConfigDirectory { get; init; }
}
