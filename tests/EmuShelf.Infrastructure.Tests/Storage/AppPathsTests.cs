namespace EmuShelf.Infrastructure.Tests.Storage;

public class AppPathsTests : TempAppDirectoryTestBase
{
    [Fact]
    public void ResolvePortableBaseDirectory_UsesAppImageParent()
    {
        var previous = Environment.GetEnvironmentVariable("APPIMAGE");
        try
        {
            var image = Path.Combine(AppPaths.BaseDirectory, "EmuShelf.AppImage");
            Environment.SetEnvironmentVariable("APPIMAGE", image);

            Assert.Equal(
                AppPaths.BaseDirectory,
                EmuShelf.Infrastructure.Storage.AppPaths.ResolvePortableBaseDirectory());
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
    }

    [Fact]
    public void FilePaths_AreNestedUnderExpectedDirectories()
    {
        Assert.Equal(Path.Combine(AppPaths.DataDirectory, "library.db"), AppPaths.DatabaseFilePath);
        Assert.Equal(Path.Combine(AppPaths.SettingsDirectory, "settings.json"), AppPaths.SettingsFilePath);
    }
}
