using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>
/// Applies and persists the selected appearance. System/Light/Dark use the base
/// <c>EmuShelfTheme</c> theme-dictionaries via <see cref="Application.RequestedThemeVariant"/>. Each
/// additional named palette (OLED, Cyberpunk, Nord …) is a flat resource dictionary appended last to
/// <see cref="Application"/>'s merged dictionaries, so its <c>EmuXxxBrush</c> tokens win over the base
/// set; every consumer references those tokens with <c>DynamicResource</c>, so a swap re-colors the
/// whole UI live. The palette also declares a base <see cref="ThemeVariant"/> so stock Fluent chrome
/// (text carets, scroll bars) stays legible.
/// </summary>
public sealed class AppThemeService : IAppThemeService
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<ThemePreference, ResourceInclude> _paletteCache = [];
    private AppSettings _settings;
    private ResourceInclude? _activeOverride;

    public ThemePreference Current { get; private set; }

    public AppThemeService(ISettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        Current = settings.Theme;
        ApplyToApplication(Current);
    }

    public async Task SetThemeAsync(
        ThemePreference preference,
        CancellationToken cancellationToken = default)
    {
        Current = preference;
        ApplyToApplication(preference);
        _settings = await Task.Run(
            () => _settingsService.Update(latest => latest with { Theme = preference }),
            cancellationToken);
    }

    private void ApplyToApplication(ThemePreference preference)
    {
        if (Application.Current is not { } application)
            return;

        application.RequestedThemeVariant = BaseVariant(preference);
        SwapPaletteOverride(application, preference);
    }

    private void SwapPaletteOverride(Application application, ThemePreference preference)
    {
        var dictionaries = application.Resources.MergedDictionaries;
        if (_activeOverride is not null)
        {
            dictionaries.Remove(_activeOverride);
            _activeOverride = null;
        }

        if (PaletteUri(preference) is not { } uri)
            return;

        if (!_paletteCache.TryGetValue(preference, out var palette))
        {
            palette = new ResourceInclude(uri) { Source = uri };
            _paletteCache[preference] = palette;
        }

        // Appended last so its tokens take precedence over the base EmuShelfTheme dictionary.
        dictionaries.Add(palette);
        _activeOverride = palette;
    }

    private static ThemeVariant BaseVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeVariant.Light,
        ThemePreference.System => ThemeVariant.Default,
        // Dark and every additional palette read as dark, so stock Fluent chrome uses its dark set.
        _ => ThemeVariant.Dark,
    };

    private static Uri? PaletteUri(ThemePreference preference) => preference switch
    {
        ThemePreference.Oled => new Uri("avares://EmuShelf/Styles/Palettes/Oled.axaml"),
        ThemePreference.Cyberpunk => new Uri("avares://EmuShelf/Styles/Palettes/Cyberpunk.axaml"),
        ThemePreference.Nord => new Uri("avares://EmuShelf/Styles/Palettes/Nord.axaml"),
        _ => null,
    };
}
