using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class RcloneExecutableTests : TempAppDirectoryTestBase
{
    [Fact]
    public void Resolve_PrefersAnExplicitPath()
    {
        Assert.Equal("/custom/rclone", RcloneExecutable.Resolve(AppPaths, "/custom/rclone"));
    }

    [Fact]
    public void Resolve_FindsRcloneBesideTheExecutable()
    {
        AppPaths.EnsureDirectoriesExist();
        var beside = Path.Combine(AppPaths.BaseDirectory, RcloneExecutable.FileName);
        File.WriteAllText(beside, "binary");

        Assert.Equal(beside, WithAppDir(null, () => RcloneExecutable.Resolve(AppPaths)));
    }

    [Fact]
    public void Resolve_FindsRcloneBundledBesideTheAppBinary()
    {
        // Mirrors macOS: the data directory (Application Support) holds no rclone, but the bundled
        // copy sits beside the app binary — AppContext.BaseDirectory, i.e. the .app's Contents/MacOS.
        var bundled = Path.Combine(AppContext.BaseDirectory, RcloneExecutable.FileName);
        var alreadyPresent = File.Exists(bundled);
        if (!alreadyPresent)
            File.WriteAllText(bundled, "binary");
        try
        {
            Assert.Equal(bundled, WithAppDir(null, () => RcloneExecutable.Resolve(AppPaths)));
        }
        finally
        {
            if (!alreadyPresent)
                File.Delete(bundled);
        }
    }

    [Fact]
    public void Resolve_PrefersTheAppImageMountWhenPresent()
    {
        var appDir = Path.Combine(BaseDirectory, "mount");
        var bundled = Path.Combine(appDir, "usr", "bin", RcloneExecutable.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
        File.WriteAllText(bundled, "binary");

        Assert.Equal(bundled, WithAppDir(appDir, () => RcloneExecutable.Resolve(AppPaths)));
    }

    [Fact]
    public void Resolve_FallsBackToTheBaseDirectoryWhenNothingExists()
    {
        Assert.Equal(
            Path.Combine(AppPaths.BaseDirectory, RcloneExecutable.FileName),
            WithAppDir(null, () => RcloneExecutable.Resolve(AppPaths)));
    }

    private static string WithAppDir(string? appDir, Func<string> resolve)
    {
        var previous = Environment.GetEnvironmentVariable("APPDIR");
        Environment.SetEnvironmentVariable("APPDIR", appDir);
        try
        {
            return resolve();
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPDIR", previous);
        }
    }
}
