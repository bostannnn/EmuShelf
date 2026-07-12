using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Settings;

namespace EmuShelf.Infrastructure.Tests.Settings;

public class JsonSettingsServiceTests : TempAppDirectoryTestBase
{
    public JsonSettingsServiceTests()
    {
        AppPaths.EnsureDirectoriesExist();
    }

    [Fact]
    public void Load_NoSettingsFile_ReturnsDefaults()
    {
        var service = new JsonSettingsService(AppPaths);

        var settings = service.Load();

        Assert.Equal(ThemePreference.System, settings.Theme);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var service = new JsonSettingsService(AppPaths);
        var settings = new AppSettings { Theme = ThemePreference.Dark };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(ThemePreference.Dark, loaded.Theme);
    }

    [Fact]
    public void Save_WritesHumanReadableEnumValue()
    {
        var service = new JsonSettingsService(AppPaths);

        service.Save(new AppSettings { Theme = ThemePreference.Light });

        var json = File.ReadAllText(AppPaths.SettingsFilePath);
        Assert.Contains("\"Light\"", json);
    }

    [Fact]
    public void Load_MalformedSettingsFile_FallsBackToDefaults()
    {
        File.WriteAllText(AppPaths.SettingsFilePath, "{ not valid json");
        var service = new JsonSettingsService(AppPaths);

        var settings = service.Load();

        Assert.Equal(ThemePreference.System, settings.Theme);
    }

    [Fact]
    public void Save_LeavesNoTempResidue_AndRemainsReadable()
    {
        var service = new JsonSettingsService(AppPaths);

        service.Save(new AppSettings { Theme = ThemePreference.Dark });

        // The write-then-rename must not leave the temp file behind, and the result must load.
        Assert.False(File.Exists(AppPaths.SettingsFilePath + ".tmp"));
        Assert.Equal(ThemePreference.Dark, service.Load().Theme);
    }
}
