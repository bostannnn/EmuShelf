namespace EmuShelf.Core.Storage;

/// <summary>Why the app must show data-folder onboarding instead of booting straight into the library.</summary>
public enum DataLocationOnboardingReason
{
    /// <summary>No pointer has ever been written — the genuine first launch.</summary>
    FirstRun,

    /// <summary>A folder was chosen, but the all-files grant that makes it readable is no longer held.</summary>
    StoragePermissionMissing,

    /// <summary>A folder was chosen and the grant is held, but the folder itself is gone or unwritable
    /// (SD card removed, deleted by the user, storage remounted).</summary>
    LocationUnavailable,
}

/// <summary>
/// The outcome of <see cref="DataLocationResolver.Resolve"/>: either a ready-to-use base directory or a
/// reason the app must onboard first. Exactly one of the two states holds.
/// </summary>
public sealed record DataLocationResolution
{
    private DataLocationResolution(string? baseDirectory, DataLocationOnboardingReason? reason)
    {
        BaseDirectory = baseDirectory;
        OnboardingReason = reason;
    }

    /// <summary>The resolved base directory when <see cref="IsResolved"/>; otherwise null.</summary>
    public string? BaseDirectory { get; }

    /// <summary>The onboarding reason when not resolved; otherwise null.</summary>
    public DataLocationOnboardingReason? OnboardingReason { get; }

    /// <summary>True when a usable base directory was resolved and no onboarding is required.</summary>
    public bool IsResolved => BaseDirectory is not null;

    public static DataLocationResolution Resolved(string baseDirectory) =>
        new(baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)), null);

    public static DataLocationResolution Onboarding(DataLocationOnboardingReason reason) =>
        new(null, reason);
}

/// <summary>
/// Decides, before the composition root opens the database, whether EmuShelf has a usable data folder or
/// must run first-run onboarding — and if it must, why. Pure and platform-agnostic: it combines the
/// persisted pointer, the storage-permission gate, and a writability probe passed in as a delegate, so it
/// is fully unit-testable with fakes and carries no Android or filesystem dependency of its own.
/// </summary>
public sealed class DataLocationResolver
{
    private readonly IDataLocationStore _store;
    private readonly IStoragePermissionService _permission;
    private readonly Func<string, bool> _isWritable;

    /// <param name="isWritable">
    /// Returns whether the given directory exists (or can be created) and accepts a write. Injected so the
    /// resolver stays free of <c>System.IO</c>; production supplies a real create-dir-and-probe function.
    /// </param>
    public DataLocationResolver(
        IDataLocationStore store,
        IStoragePermissionService permission,
        Func<string, bool> isWritable)
    {
        _store = store;
        _permission = permission;
        _isWritable = isWritable;
    }

    public DataLocationResolution Resolve()
    {
        var pointer = _store.Read();
        if (pointer is null || string.IsNullOrWhiteSpace(pointer.BaseDirectory))
            return DataLocationResolution.Onboarding(DataLocationOnboardingReason.FirstRun);

        // The grant is checked before the writability probe: a missing all-files grant is the *reason* the
        // probe would fail on Android, and reporting it distinctly lets onboarding send the user to the
        // Settings toggle rather than to the folder picker they already completed.
        if (_permission.RequiresGrant && !_permission.IsGranted)
            return DataLocationResolution.Onboarding(DataLocationOnboardingReason.StoragePermissionMissing);

        if (!_isWritable(pointer.BaseDirectory))
            return DataLocationResolution.Onboarding(DataLocationOnboardingReason.LocationUnavailable);

        return DataLocationResolution.Resolved(pointer.BaseDirectory);
    }
}
