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
        Assert.False(settings.AutomaticallyFetchMetadataAfterImport);
        Assert.False(settings.MetadataConsentPromptShown);
        Assert.False(settings.Scraping.ScreenScraper.Enabled);
        Assert.True(settings.Scraping.WebImageSearchEnabled);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        var service = new JsonSettingsService(AppPaths);
        var settings = new AppSettings
        {
            Theme = ThemePreference.Dark,
            AutomaticallyFetchMetadataAfterImport = true,
            MetadataConsentPromptShown = true,
            Scraping = new ScrapingSettings
            {
                WebImageSearchEnabled = false,
                ScreenScraper = new ScreenScraperSettings { Enabled = true },
            },
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.True(loaded.AutomaticallyFetchMetadataAfterImport);
        Assert.True(loaded.MetadataConsentPromptShown);
        Assert.False(loaded.Scraping.WebImageSearchEnabled);
        Assert.True(loaded.Scraping.ScreenScraper.Enabled);
    }

    [Fact]
    public void Load_OlderFileWithLegacyScrapingFields_IgnoresThemAndLoads()
    {
        // A settings.json written by an older build serialized media-kind/metadata-field lists and other
        // now-removed scraping toggles (BuiltInCatalog/DuckDuckGoArtwork/AutomaticallyScrapeAfterImport).
        // Those are code-owned again, so the loader must ignore the unknown members and still read the
        // fields that remain, without throwing.
        File.WriteAllText(
            AppPaths.SettingsFilePath,
            """
            {
              "Scraping": {
                "ScreenScraper": {
                  "Enabled": true,
                  "AutomaticallyScrapeAfterImport": true,
                  "MediaKinds": ["BoxFront", "Screenshot", "Wheel", "Fanart"]
                },
                "DuckDuckGoArtwork": { "Enabled": false },
                "BuiltInCatalog": { "Enabled": true }
              }
            }
            """);
        var service = new JsonSettingsService(AppPaths);

        var scraping = service.Load().Scraping;

        Assert.True(scraping.ScreenScraper.Enabled);
        // WebImageSearchEnabled is the new toggle; the legacy DuckDuckGoArtwork block is ignored, so it
        // keeps its default rather than picking up the old "Enabled": false.
        Assert.True(scraping.WebImageSearchEnabled);
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

    [Fact]
    public void Update_MergesEachScopedChangeAgainstTheLatestFile()
    {
        var service = new JsonSettingsService(AppPaths);
        service.Save(new AppSettings { Theme = ThemePreference.Dark });

        service.Update(settings => settings with
        {
            InterfaceMode = InterfaceMode.Gamepad,
        });

        var loaded = service.Load();
        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal(InterfaceMode.Gamepad, loaded.InterfaceMode);
    }

    [Fact]
    public void Update_MalformedSettingsFile_DoesNotOverwriteItWithDefaults()
    {
        const string malformed = "{ not valid json";
        File.WriteAllText(AppPaths.SettingsFilePath, malformed);
        var service = new JsonSettingsService(AppPaths);

        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            service.Update(settings => settings with { Theme = ThemePreference.Dark }));

        Assert.Equal(malformed, File.ReadAllText(AppPaths.SettingsFilePath));
    }

    [Fact]
    public async Task Update_SerializesIndependentServicesUsingTheSameSettingsFile()
    {
        var firstService = new JsonSettingsService(AppPaths);
        var secondService = new JsonSettingsService(AppPaths);
        firstService.Save(new AppSettings());
        using var firstUpdateEntered = new ManualResetEventSlim();
        using var releaseFirstUpdate = new ManualResetEventSlim();
        using var secondUpdateEntered = new ManualResetEventSlim();

        var firstUpdate = Task.Run(() => firstService.Update(settings =>
        {
            firstUpdateEntered.Set();
            releaseFirstUpdate.Wait();
            return settings with { Theme = ThemePreference.Dark };
        }));
        // The callback is entered the instant the cross-instance lock is taken, so a passing run
        // returns immediately; this generous bound only absorbs a loaded CI runner being slow to
        // acquire the lock. The former 2s bound flaked on Windows CI (release build for v1.0.3).
        Assert.True(firstUpdateEntered.Wait(TimeSpan.FromSeconds(30)));

        var secondUpdate = Task.Run(() => secondService.Update(settings =>
        {
            secondUpdateEntered.Set();
            return settings with { InterfaceMode = InterfaceMode.Gamepad };
        }));

        Assert.False(secondUpdateEntered.Wait(TimeSpan.FromMilliseconds(100)));
        releaseFirstUpdate.Set();
        await Task.WhenAll(firstUpdate, secondUpdate);

        var loaded = firstService.Load();
        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.Equal(InterfaceMode.Gamepad, loaded.InterfaceMode);
    }
}
