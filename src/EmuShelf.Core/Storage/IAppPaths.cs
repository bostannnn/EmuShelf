namespace EmuShelf.Core.Storage;

/// <summary>
/// Portable, execution-relative locations for EmuShelf's on-disk state.
/// All paths live beside the running executable so the whole install can
/// move as a unit with the app, emulators, and games.
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

    /// <summary>Creates Data/Covers/Cache/Logs/Settings/Saves beside the executable if they don't already exist.</summary>
    void EnsureDirectoriesExist();
}
