using Avalonia;
using Avalonia.Headless.XUnit;
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

    public void Dispose()
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = ThemeVariant.Default;
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }
}
