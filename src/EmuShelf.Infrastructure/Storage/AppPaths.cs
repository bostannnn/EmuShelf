using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Storage;

public sealed class AppPaths : IAppPaths
{
    public string BaseDirectory { get; }
    public string DataDirectory { get; }
    public string CoversDirectory { get; }
    public string CacheDirectory { get; }
    public string LogsDirectory { get; }
    public string SettingsDirectory { get; }
    public string SavesDirectory { get; }
    public string EmulatorsDirectory { get; }
    public string DatabaseFilePath { get; }
    public string SettingsFilePath { get; }

    public AppPaths() : this(ResolveBaseDirectory())
    {
    }

    public AppPaths(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        DataDirectory = Path.Combine(baseDirectory, "Data");
        CoversDirectory = Path.Combine(baseDirectory, "Covers");
        CacheDirectory = Path.Combine(baseDirectory, "Cache");
        LogsDirectory = Path.Combine(baseDirectory, "Logs");
        SettingsDirectory = Path.Combine(baseDirectory, "Settings");
        SavesDirectory = Path.Combine(baseDirectory, "Saves");
        EmulatorsDirectory = Path.Combine(baseDirectory, "Emulators");
        DatabaseFilePath = Path.Combine(DataDirectory, "library.db");
        SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");
    }

    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CoversDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(SavesDirectory);
        Directory.CreateDirectory(EmulatorsDirectory);
    }

    /// <summary>
    /// The root beneath which Data/Covers/Cache/Logs/Settings/Saves live. Portable (beside the
    /// executable) on Windows and Linux; on macOS the executable is buried inside the .app bundle,
    /// so a per-user Application Support folder is used instead — see <see cref="IAppPaths"/>.
    /// </summary>
    internal static string ResolveBaseDirectory()
    {
        // AppImage mounts $APPDIR read-only. Its launcher path, $APPIMAGE, is the only
        // stable writable anchor for a portable install; keep data beside that file.
        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath))
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(appImagePath));
            if (!string.IsNullOrWhiteSpace(parent))
                return parent;
        }

        // macOS ships as a .app bundle whose executable sits at Contents/MacOS/. Writing "beside
        // the executable" there would bury the whole library inside the bundle: hidden from Finder,
        // erased when the app is replaced on update, and unwritable once Gatekeeper translocates a
        // quarantined bundle to a read-only mount. Use the conventional per-user location instead.
        // SpecialFolder.ApplicationData maps to ~/.config on .NET for macOS, so build the Cocoa
        // path from the home directory rather than relying on it.
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
                return Path.Combine(home, "Library", "Application Support", "EmuShelf");
        }

        return AppContext.BaseDirectory;
    }
}
