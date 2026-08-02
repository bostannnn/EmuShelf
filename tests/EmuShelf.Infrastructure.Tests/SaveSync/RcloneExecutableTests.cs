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
