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
    /// Decides, right now, whether EmuShelf has a usable data folder or must onboard first. Deliberately a
    /// method, not a value captured at process start: on Android the process is often created with no
    /// Activity at all (the system re-binds the accessibility service after every install; Shizuku pokes
    /// the provider), and a verdict frozen in such a process — early boot, storage not yet mounted — was
    /// what the user later saw when they finally opened the app. The composition root calls this when an
    /// Activity actually asks for a view, and onboarding calls it again on every return to the foreground
    /// so a pointer that becomes readable (grant restored, card remounted, mirror found after a reinstall)
    /// completes onboarding without a manual pick.
    /// </summary>
    DataLocationResolution Resolve();

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
    /// A data folder from a previous install that is still on the device — <c>Data/library.db</c> exists
    /// under it — found by looking in the usual places (the recommended folder, every mounted volume's
    /// root and first-level folders). Null when nothing is found or the grant is not held yet. Lets a
    /// reinstall offer "use your existing library" instead of a fresh pick.
    /// </summary>
    string? FindExistingDataFolder() => null;

    /// <summary>Adopts a folder returned by <see cref="FindExistingDataFolder"/>: validates it is writable and persists the pointer.</summary>
    Task<DataLocationPickResult> UseExistingFolderAsync(string baseDirectory) =>
        Task.FromResult(DataLocationPickResult.Failed("Existing data folders cannot be adopted here."));

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
