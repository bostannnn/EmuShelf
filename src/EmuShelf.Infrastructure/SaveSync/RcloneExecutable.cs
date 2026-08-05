using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Resolves the rclone executable. An explicit path wins; otherwise a bundled copy is preferred —
/// inside the AppImage mount (<c>$APPDIR/usr/bin</c>) on Linux, or beside the app binary (the .app
/// bundle's <c>Contents/MacOS</c> on macOS) — then a copy in the writable data directory (where
/// "Get rclone" downloads it), falling back to that data directory so a "not found" error still
/// names a sensible, writable location.
/// </summary>
public static class RcloneExecutable
{
    /// <summary>The platform-specific rclone file name.</summary>
    public static string FileName => OperatingSystem.IsWindows() ? "rclone.exe" : "rclone";

    /// <summary>The path EmuShelf will invoke for rclone, whether or not the file exists.</summary>
    public static string Resolve(IAppPaths appPaths, string? explicitPath = null)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return explicitPath;

        foreach (var candidate in Candidates(appPaths))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(appPaths.BaseDirectory, FileName);
    }

    private static IEnumerable<string> Candidates(IAppPaths appPaths)
    {
        // Inside an AppImage the app runs from a read-only mount ($APPDIR) while portable data
        // lives beside $APPIMAGE. The bundled rclone travels in the mount, so look there first.
        var appDir = Environment.GetEnvironmentVariable("APPDIR");
        if (!string.IsNullOrWhiteSpace(appDir))
        {
            yield return Path.Combine(appDir, "usr", "bin", FileName);
            yield return Path.Combine(appDir, FileName);
        }

        // The bundled rclone travels beside the app binary. On Windows/Linux portable this is the
        // same as the data directory below, but on macOS the binary lives in the read-only .app
        // bundle while data lives in Application Support, so the two must be checked separately.
        yield return Path.Combine(AppContext.BaseDirectory, FileName);

        yield return Path.Combine(appPaths.BaseDirectory, FileName);
    }
}
