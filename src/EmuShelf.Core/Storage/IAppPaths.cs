namespace EmuShelf.Core.Storage;

/// <summary>
/// Portable, execution-relative locations for EmuShelf's on-disk state.
/// On Windows and Linux all paths live beside the running executable so the whole install can
/// move as a unit with the app, emulators, and games. macOS is the exception: the executable is
/// buried inside the .app bundle, so state lives under ~/Library/Application Support/EmuShelf,
/// where it survives dragging a new build over the old one and Gatekeeper app translocation.
/// </summary>
public interface IAppPaths
{
    string BaseDirectory { get; }
    string DataDirectory { get; }
    string CoversDirectory { get; }
    string CacheDirectory { get; }
    string LogsDirectory { get; }
    string SettingsDirectory { get; }
    string SavesDirectory { get; }
    string DatabaseFilePath { get; }
    string SettingsFilePath { get; }

    /// <summary>
    /// Whether library paths on the same filesystem root as <see cref="BaseDirectory"/> may be stored
    /// relative to it. True on the desktop portable targets, where app data, emulators and games live on
    /// one drive that moves as a unit. False on Android: app-private storage (<c>/data/…</c>) and shared
    /// storage (<c>/storage/…</c>) both root at <c>/</c> but do not move together — uninstall wipes the
    /// former and the SD card is removable — so relativizing a game path against the app base produces a
    /// fragile <c>../../../storage/…</c> string. When false, <c>RelativePathResolver</c> stores absolute
    /// paths unchanged.
    /// </summary>
    bool UsesPortableStorage => true;

    /// <summary>Creates Data/Covers/Cache/Logs/Settings/Saves beside the executable if they don't already exist.</summary>
    void EnsureDirectoriesExist();
}
