namespace EmuShelf.Core.Emulators;

/// <summary>
/// Installs and updates the external emulators EmuShelf supports into a managed, portable
/// <c>Emulators/&lt;id&gt;/</c> folder beside the executable. EmuShelf only ever overwrites installs it wrote
/// itself (tracked in the manifest); a user-provided executable is read-only to this service. Downloads
/// come only from each emulator's official source over HTTPS and are checksum-verified when the release
/// publishes one.
/// </summary>
public interface IEmulatorInstallService
{
    /// <summary>
    /// Resolves the current install state of one emulator, contacting its source for the newest build when
    /// reachable. Never throws for an unknown emulator or an unreachable source — those map to
    /// <see cref="EmulatorInstallStatus.Unsupported"/> / <see cref="EmulatorInstallStatus.CheckFailed"/>.
    /// </summary>
    Task<EmulatorInstallStatus> GetStatusAsync(string emulatorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the newest managed build for this machine, verifies it, unpacks it into the managed
    /// directory, records it in the manifest, and returns the resolved executable path. Refuses to touch a
    /// user-provided install.
    /// </summary>
    Task<EmulatorInstallResult> InstallAsync(
        string emulatorId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing managed install to the newest build. Equivalent to <see cref="InstallAsync"/>
    /// but returns <see cref="EmulatorInstallResult.AlreadyCurrent"/> when nothing newer is published.
    /// </summary>
    Task<EmulatorInstallResult> UpdateAsync(
        string emulatorId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
