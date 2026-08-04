using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using EmuShelf.App.Services;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.App.Tests;

public class AppThemeServiceTests : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfThemeTests",
        Guid.NewGuid().ToString("N"));

    [AvaloniaFact]
    public async Task SetThemeAsync_AppliesAndPersistsThreeWayPreference()
    {
        var paths = new AppPaths(_baseDirectory);
        paths.EnsureDirectoriesExist();
        var settings = new JsonSettingsService(paths);
        var themes = new AppThemeService(
            settings,
            new AppSettings { Theme = ThemePreference.Light });

        Assert.Equal(ThemePreference.Light, themes.Current);
        Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

        await themes.SetThemeAsync(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, themes.Current);
        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.Equal(ThemePreference.Dark, settings.Load().Theme);

        await themes.SetThemeAsync(ThemePreference.System);

        Assert.Equal(ThemeVariant.Default, Application.Current.RequestedThemeVariant);
        Assert.Equal(ThemePreference.System, settings.Load().Theme);
    }

    [AvaloniaFact]
    public async Task SetAmbientFromArtworkAsync_PersistsAndRaisesEvent()
    {
        var paths = new AppPaths(_baseDirectory);
        paths.EnsureDirectoriesExist();
        var settings = new JsonSettingsService(paths);
        var themes = new AppThemeService(settings, new AppSettings());

        Assert.False(themes.AmbientFromArtwork);
        var raised = false;
        themes.AmbientFromArtworkChanged += (_, _) => raised = true;

        await themes.SetAmbientFromArtworkAsync(true);

        Assert.True(themes.AmbientFromArtwork);
        Assert.True(raised);
        Assert.True(settings.Load().AmbientThemeFromArtwork);
    }

    [AvaloniaFact]
    public void ApplyArtworkPalette_RetintsAccentAndVariant_ThenClearRestoresTheme()
    {
        var paths = new AppPaths(_baseDirectory);
        paths.EnsureDirectoriesExist();
        var settings = new JsonSettingsService(paths);
        var themes = new AppThemeService(settings, new AppSettings { Theme = ThemePreference.Light });

        var palette = ArtworkPaletteFactory.Create(new Rgb(40, 60, 200), averageLuminance: 0.1);
        themes.ApplyArtworkPalette(palette);

        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.True(Application.Current.TryGetResource("EmuAccentBrush", null, out var accentObject));
        var accent = Assert.IsType<SolidColorBrush>(accentObject);
        Assert.Equal(palette.Accent, $"#{accent.Color.R:X2}{accent.Color.G:X2}{accent.Color.B:X2}");

        themes.ClearArtworkPalette();

        Assert.Equal(ThemeVariant.Light, Application.Current.RequestedThemeVariant);
    }

    public void Dispose()
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = ThemeVariant.Default;
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }
}
