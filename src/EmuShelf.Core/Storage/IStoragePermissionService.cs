namespace EmuShelf.Core.Storage;

/// <summary>
/// Abstracts the platform's "may EmuShelf read and write arbitrary shared-storage paths?" grant. On
/// Android the user-chosen data folder is a real <c>/storage/…</c> path, which the process can only open
/// by path once <c>MANAGE_EXTERNAL_STORAGE</c> (all-files access) is granted at runtime; the grant is a
/// system Settings toggle, so it must be requested and re-checked, not assumed. Every desktop target has
/// no such gate — the portable folder beside the executable is always writable — so they use
/// <see cref="GrantedStoragePermissionService"/>, which reports the grant as unconditionally held.
/// </summary>
public interface IStoragePermissionService
{
    /// <summary>
    /// Whether this platform gates path access behind a runtime grant at all. False on desktop, where the
    /// resolver never treats a missing grant as a reason to re-onboard; true on Android.
    /// </summary>
    bool RequiresGrant { get; }

    /// <summary>Whether the grant is currently held. Always true when <see cref="RequiresGrant"/> is false.</summary>
    bool IsGranted { get; }

    /// <summary>
    /// Sends the user to the platform's grant surface (on Android, the all-files-access Settings screen).
    /// A no-op where <see cref="RequiresGrant"/> is false. The result is observed later via
    /// <see cref="IsGranted"/> on return to the foreground, not returned here.
    /// </summary>
    void RequestGrant();
}

/// <summary>
/// The desktop/no-gate implementation: the portable data folder is always writable, so the grant is
/// reported as permanently held and requesting it does nothing. Also the safe default for tests.
/// </summary>
public sealed class GrantedStoragePermissionService : IStoragePermissionService
{
    public bool RequiresGrant => false;
    public bool IsGranted => true;
    public void RequestGrant()
    {
    }
}
