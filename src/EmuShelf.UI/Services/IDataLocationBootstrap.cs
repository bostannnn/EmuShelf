using System;
using System.Threading.Tasks;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.Services;

/// <summary>
/// The outcome of a folder pick during onboarding: a resolved base directory on success, a plain
/// cancellation, or a validated failure with a user-facing reason (e.g. the folder resolved to no readable
/// path, or was an off-limits <c>Android/data</c> location).
/// </summary>
public sealed record DataLocationPickResult(string? BaseDirectory, string? Error)
{
    public bool Succeeded => BaseDirectory is not null;

    public static DataLocationPickResult Success(string baseDirectory) => new(baseDirectory, null);
    public static DataLocationPickResult Cancelled() => new(null, null);
    public static DataLocationPickResult Failed(string error) => new(null, error);
}

/// <summary>
/// The platform side of first-run data-folder onboarding, supplied by the Android head. Keeps the SAF
/// picker, the all-files-access grant, and the pointer persistence out of the shared
/// <c>OnboardingViewModel</c>, which only orchestrates them. Desktop never sets this — its data folder is
/// resolved from the environment and no onboarding runs.
/// </summary>
public interface IDataLocationBootstrap
{
    /// <summary>
    /// The already-resolved base directory, or null when onboarding is required. When non-null the shared
    /// composition root skips onboarding entirely and boots against this path.
    /// </summary>
    string? ResolvedBaseDirectory { get; }

    /// <summary>
    /// Why onboarding is required, valid only when <see cref="ResolvedBaseDirectory"/> is null. Seeds the
    /// onboarding view-model's first message (fresh install vs. lost grant vs. missing folder).
    /// </summary>
    DataLocationOnboardingReason OnboardingReason { get; }

    /// <summary>Whether the platform gates path access behind a runtime grant (true on Android).</summary>
    bool RequiresStoragePermission { get; }

    /// <summary>Whether that grant is currently held. Always true when it is not required.</summary>
    bool IsStoragePermissionGranted { get; }

    /// <summary>
    /// Raised when EmuShelf returns to the foreground, i.e. the user may have just toggled the system
    /// all-files switch and come back. The onboarding view-model re-reads <see cref="IsStoragePermissionGranted"/>
    /// in response so the picker enables without a manual refresh.
    /// </summary>
    event Action? StoragePermissionMaybeChanged;

    /// <summary>Sends the user to the system all-files-access screen. A no-op where no grant is required.</summary>
    void RequestStoragePermission();

    /// <summary>
    /// The default data folder EmuShelf can create by itself once all-files access is held — a normal path
    /// on internal shared storage (e.g. <c>/storage/emulated/0/EmuShelf</c>), shown to the user and used by
    /// <see cref="UseRecommendedFolderAsync"/>. Null on platforms with no recommended location (desktop).
    /// This sidesteps the system document picker entirely, which refuses <c>Download</c>, <c>Documents</c>,
    /// the storage root and other protected trees — the main source of first-run confusion on Android.
    /// </summary>
    string? RecommendedBaseDirectory { get; }

    /// <summary>
    /// Creates and persists <see cref="RecommendedBaseDirectory"/> directly (no document picker), validating
    /// it is writable under the current grant. The one-tap path for the common case.
    /// </summary>
    Task<DataLocationPickResult> UseRecommendedFolderAsync();

    /// <summary>
    /// Runs the system folder picker, validates the choice (readable real path, not an off-limits location),
    /// creates the <c>EmuShelf</c> subfolder, persists the pointer, and returns the resolved base directory.
    /// For users who want their data somewhere other than <see cref="RecommendedBaseDirectory"/>.
    /// </summary>
    Task<DataLocationPickResult> PickFolderAsync();

    /// <summary>
    /// Whether to offer the optional "enable second-screen return" step during onboarding — true only on a
    /// device that actually has a second display (the Thor). It wires the accessibility watcher that returns
    /// EmuShelf to the library when a game launched onto the second screen is closed. Defaults off, so the
    /// desktop path and any platform without a companion display never shows it.
    /// </summary>
    bool ShowSecondScreenReturnStep => false;

    /// <summary>Whether that watcher is currently enabled. Re-read on foreground return, like the grant.</summary>
    bool IsSecondScreenReturnEnabled => false;

    /// <summary>Sends the user to the system accessibility screen to enable the watcher. A no-op by default.</summary>
    void RequestSecondScreenReturn()
    {
    }
}
