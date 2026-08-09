namespace EmuShelf.Core.Emulators;

/// <summary>
/// The install state of one emulator, as resolved from EmuShelf's managed-install manifest, the user's
/// own configured executable, and (when reachable) the newest published build. A closed hierarchy so the
/// UI must handle every case. Mirrors the shape of <see cref="EmuShelf.Core.Updates.UpdateCheckResult"/>.
/// </summary>
public abstract record EmulatorInstallStatus
{
    // Private so the only subtypes are the ones nested here.
    private EmulatorInstallStatus() { }

    /// <summary>
    /// Nothing is installed that EmuShelf knows about. <paramref name="LatestVersion"/> is the newest
    /// managed build's tag when the check reached the source, otherwise null.
    /// </summary>
    public sealed record NotInstalled(string? LatestVersion) : EmulatorInstallStatus;

    /// <summary>A managed install EmuShelf wrote, running the newest build it knows of.</summary>
    public sealed record Managed(string InstalledVersion) : EmulatorInstallStatus;

    /// <summary>A managed install with a newer build available to update to.</summary>
    public sealed record UpdateAvailable(string InstalledVersion, string LatestVersion) : EmulatorInstallStatus;

    /// <summary>
    /// The user configured their own executable. EmuShelf treats it as read-only — it never overwrites a
    /// user-provided install — so only a version note and the download page are offered.
    /// </summary>
    public sealed record UserProvided(string ExecutablePath, string? LatestVersion) : EmulatorInstallStatus;

    /// <summary>
    /// EmuShelf cannot manage this emulator here: no build is published for this OS/arch, or the source is
    /// a vendor server whose resolver has not shipped (Dolphin, RetroArch). <paramref name="DownloadPageUrl"/>
    /// is the emulator's own download page when one is known.
    /// </summary>
    public sealed record Unsupported(string Reason, string? DownloadPageUrl) : EmulatorInstallStatus;

    /// <summary>The status check could not complete — offline, rate-limited, or an unreadable response.</summary>
    public sealed record CheckFailed(string Reason) : EmulatorInstallStatus;
}

/// <summary>The outcome of an install or update action. A closed hierarchy so callers handle each case.</summary>
public abstract record EmulatorInstallResult
{
    private EmulatorInstallResult() { }

    /// <summary>The emulator was installed or updated; <paramref name="ExecutablePath"/> is the absolute path to run.</summary>
    public sealed record Installed(string Version, string ExecutablePath) : EmulatorInstallResult;

    /// <summary>The managed install was already the newest build; nothing was downloaded.</summary>
    public sealed record AlreadyCurrent(string Version) : EmulatorInstallResult;

    /// <summary>
    /// The action was refused before touching disk — most often because it would overwrite a user-provided
    /// install, which EmuShelf never does.
    /// </summary>
    public sealed record Refused(string Reason) : EmulatorInstallResult;

    /// <summary>The install failed (download, checksum, extraction, or no asset for this platform).</summary>
    public sealed record Failed(string Reason) : EmulatorInstallResult;
}
