namespace EmuShelf.Core.Storage;

/// <summary>
/// The persisted "where does EmuShelf keep its data" pointer. On Android this is stored in app-private
/// storage (the one always-writable place) and points at the user-chosen external base directory, because
/// the base directory cannot be recorded inside the data folder it names. Desktop never writes one — its
/// base directory is resolved from the environment.
/// </summary>
/// <param name="BaseDirectory">
/// The chosen root beneath which <c>Data/Covers/Cache/Logs/Settings/Saves</c> live — a real filesystem
/// path (e.g. <c>/storage/AE6A-1092/EmuShelf</c>), not a SAF content URI.
/// </param>
/// <param name="SourceUri">
/// The SAF tree URI the folder was picked from, kept only for display and re-validation. Null when the
/// path was not obtained through the document picker.
/// </param>
/// <param name="ChosenAtUtc">When the user selected this folder; informational.</param>
public sealed record DataLocation(
    string BaseDirectory,
    string? SourceUri = null,
    DateTimeOffset? ChosenAtUtc = null);

/// <summary>
/// Reads and writes the <see cref="DataLocation"/> pointer. The store lives in a location that does not
/// depend on the pointer's own contents (on Android, app-private storage), so it is available before the
/// composition root has resolved the data folder.
/// </summary>
public interface IDataLocationStore
{
    /// <summary>The persisted pointer, or null if none has been written (first run) or it was unreadable.</summary>
    DataLocation? Read();

    /// <summary>Persists the pointer durably, overwriting any previous value.</summary>
    void Write(DataLocation location);

    /// <summary>Removes the pointer, returning the app to its first-run state.</summary>
    void Clear();
}
