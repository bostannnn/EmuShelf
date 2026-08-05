namespace EmuShelf.Infrastructure.Tests.Storage;

public class AppPathsTests : TempAppDirectoryTestBase
{
    [Fact]
    public void ResolveBaseDirectory_UsesAppImageParent()
    {
        var previous = Environment.GetEnvironmentVariable("APPIMAGE");
        try
        {
            var image = Path.Combine(AppPaths.BaseDirectory, "EmuShelf.AppImage");
            Environment.SetEnvironmentVariable("APPIMAGE", image);

            Assert.Equal(
                AppPaths.BaseDirectory,
                EmuShelf.Infrastructure.Storage.AppPaths.ResolveBaseDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPIMAGE", previous);
        }
    }

    [Fact]
    public void ResolveBaseDirectory_OnMacOS_UsesApplicationSupport()
    {
        // The macOS branch is only reachable on macOS; skip elsewhere rather than assert nothing.
        if (!OperatingSystem.IsMacOS())
            return;

        var previous = Environment.GetEnvironmentVariable("APPIMAGE");
        try
        {
            // APPIMAGE takes precedence, so clear it to exercise the macOS fallback.
            Environment.SetEnvironmentVariable("APPIMAGE", null);

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "EmuShelf");

            Assert.Equal(
                expected,
                EmuShelf.Infrastructure.Storage.AppPaths.ResolveBaseDirectory());
        }
        finally
        {
            Environment.SetEnvironmentVariable("APPIMAGE", previous);
        }
    }

    [Fact]
    public void EnsureDirectoriesExist_CreatesAllPortableFolders()
    {
        AppPaths.EnsureDirectoriesExist();

        Assert.True(Directory.Exists(AppPaths.DataDirectory));
        Assert.True(Directory.Exists(AppPaths.CoversDirectory));
        Assert.True(Directory.Exists(AppPaths.CacheDirectory));
        Assert.True(Directory.Exists(AppPaths.LogsDirectory));
        Assert.True(Directory.Exists(AppPaths.SettingsDirectory));
        Assert.True(Directory.Exists(AppPaths.SavesDirectory));
    }

    [Fact]
    public void FilePaths_AreNestedUnderExpectedDirectories()
    {
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "library.db"), AppPaths.DatabaseFilePath);
        Assert.Equal(Path.Combine(AppPaths.SettingsDirectory, "settings.json"), AppPaths.SettingsFilePath);
    }
}
