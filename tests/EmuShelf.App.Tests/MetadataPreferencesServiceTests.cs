using EmuShelf.App.Services;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.App.Tests;

public class MetadataPreferencesServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfMetadataPreferencesTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Consent_IsOptInAndPreservesLatestThemeSetting()
    {
        var paths = new AppPaths(_directory);
        paths.EnsureDirectoriesExist();
        var settings = new JsonSettingsService(paths);
        settings.Save(new AppSettings { Theme = ThemePreference.Dark });
        var service = new MetadataPreferencesService(settings, settings.Load());

        await service.RecordConsentAsync(
            MetadataConsentChoice.Always,
            TestContext.Current.CancellationToken);

        var saved = settings.Load();
        Assert.Equal(ThemePreference.Dark, saved.Theme);
        Assert.True(saved.MetadataConsentPromptShown);
        Assert.True(saved.AutomaticallyFetchMetadataAfterImport);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
