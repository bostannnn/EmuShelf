using EmuShelf.Core.Settings;
using EmuShelf.Core.Metadata;
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
        Assert.True(settings.Scraping.BuiltInCatalog.Enabled);
        Assert.False(settings.Scraping.ScreenScraper.Enabled);
        Assert.False(settings.Scraping.ScreenScraper.AutomaticallyScrapeAfterImport);
        Assert.True(settings.Scraping.DuckDuckGoArtwork.Enabled);
        Assert.Contains(GameMediaKind.Fanart, settings.Scraping.ScreenScraper.MediaKinds);
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
                DuckDuckGoArtwork = new ScrapeProviderSettings { Enabled = false },
                ScreenScraper = new ScreenScraperSettings
                {
                    Enabled = true,
                    AutomaticallyScrapeAfterImport = true,
                    PreferredLanguage = "fr",
                    RegionPriority = ["fr", "eu", "wor"],
                    MetadataFields = [GameMetadataField.Description, GameMetadataField.Genre],
                    MediaKinds = [GameMediaKind.BoxFront, GameMediaKind.Wheel],
                },
            },
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.Equal(ThemePreference.Dark, loaded.Theme);
        Assert.True(loaded.AutomaticallyFetchMetadataAfterImport);
        Assert.True(loaded.MetadataConsentPromptShown);
        Assert.False(loaded.Scraping.DuckDuckGoArtwork.Enabled);
        Assert.True(loaded.Scraping.ScreenScraper.Enabled);
        Assert.True(loaded.Scraping.ScreenScraper.AutomaticallyScrapeAfterImport);
        Assert.Equal("fr", loaded.Scraping.ScreenScraper.PreferredLanguage);
        Assert.Equal(["fr", "eu", "wor"], loaded.Scraping.ScreenScraper.RegionPriority);
        // MetadataFields and MediaKinds are code-owned catalogue defaults (no UI edits them), so loading
        // re-merges the current defaults: the entries the file listed are preserved AND every supported
        // kind/field is ensured, so an older file can never hide one the app now scrapes.
        Assert.Contains(GameMetadataField.Description, loaded.Scraping.ScreenScraper.MetadataFields);
        Assert.Contains(GameMetadataField.Genre, loaded.Scraping.ScreenScraper.MetadataFields);
        Assert.Contains(GameMetadataField.Title, loaded.Scraping.ScreenScraper.MetadataFields);
        Assert.Contains(GameMediaKind.BoxFront, loaded.Scraping.ScreenScraper.MediaKinds);
        Assert.Contains(GameMediaKind.Wheel, loaded.Scraping.ScreenScraper.MediaKinds);
        Assert.Contains(GameMediaKind.TitleScreen, loaded.Scraping.ScreenScraper.MediaKinds);
    }

    [Fact]
    public void Load_OlderFileMissingNewMediaKinds_MergesCurrentCatalogueDefaults()
    {
        // A settings.json written by a build that predated the extra media kinds froze the old four-kind
        // allow-list. After an in-place update, the new kinds (title screen, box back/spine, cartridge/disc
        // and its texture) must still reach the scraper instead of being filtered out on load.
        File.WriteAllText(
            AppPaths.SettingsFilePath,
            """
            {
              "Scraping": {
                "ScreenScraper": {
                  "Enabled": true,
                  "MediaKinds": ["BoxFront", "Screenshot", "Wheel", "Fanart"]
                }
              }
            }
            """);
        var service = new JsonSettingsService(AppPaths);

        var mediaKinds = service.Load().Scraping.ScreenScraper.MediaKinds;

        Assert.Contains(GameMediaKind.TitleScreen, mediaKinds);
        Assert.Contains(GameMediaKind.BoxBack, mediaKinds);
        Assert.Contains(GameMediaKind.BoxSpine, mediaKinds);
        Assert.Contains(GameMediaKind.PhysicalMedia, mediaKinds);
        Assert.Contains(GameMediaKind.PhysicalMediaTexture, mediaKinds);
        // The kinds the file already listed remain present.
        Assert.Contains(GameMediaKind.BoxFront, mediaKinds);
        Assert.Contains(GameMediaKind.Screenshot, mediaKinds);
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
