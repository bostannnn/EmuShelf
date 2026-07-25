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
    public string DatabaseFilePath { get; }
    public string SettingsFilePath { get; }

    public AppPaths() : this(ResolvePortableBaseDirectory())
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
    }

    internal static string ResolvePortableBaseDirectory()
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

        return AppContext.BaseDirectory;
    }
}
