using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
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
///
/// The "match colours to artwork" mode reuses the same live-swap seam: <see cref="ApplyArtworkPalette"/>
/// generates a token dictionary from a game's artwork and appends it above the theme override, and
/// <see cref="ClearArtworkPalette"/> removes it to fall back to the chosen theme.
/// </summary>
public sealed class AppThemeService : IAppThemeService
{
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<ThemePreference, ResourceInclude> _paletteCache = [];
    private AppSettings _settings;
    private ResourceInclude? _activeOverride;
    private ResourceDictionary? _ambientOverride;
    private bool _ambientIsDark;

    public ThemePreference Current { get; private set; }

    public bool AmbientFromArtwork { get; private set; }

    public event EventHandler? AmbientFromArtworkChanged;

    /// <summary>Whether the couch shelf is presented through a simulated CRT tube.</summary>
    public bool CrtScreenEffect { get; private set; }

    public event EventHandler? CrtScreenEffectChanged;

    public AppThemeService(ISettingsService settingsService, AppSettings settings)
    {
        _settingsService = settingsService;
        _settings = settings;
        Current = settings.Theme;
        AmbientFromArtwork = settings.AmbientThemeFromArtwork;
        CrtScreenEffect = settings.CrtScreenEffect;
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

    public async Task SetAmbientFromArtworkAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (AmbientFromArtwork == enabled)
            return;

        AmbientFromArtwork = enabled;
        _settings = await Task.Run(
            () => _settingsService.Update(latest => latest with { AmbientThemeFromArtwork = enabled }),
            cancellationToken);
        AmbientFromArtworkChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetCrtScreenEffectAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (CrtScreenEffect == enabled)
            return;

        CrtScreenEffect = enabled;
        _settings = await Task.Run(
            () => _settingsService.Update(latest => latest with { CrtScreenEffect = enabled }),
            cancellationToken);
        CrtScreenEffectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyArtworkPalette(ArtworkPalette palette)
    {
        if (Application.Current is not { } application)
            return;

        var generated = BuildAmbientDictionary(palette);
        var dictionaries = application.Resources.MergedDictionaries;
        if (_ambientOverride is not null)
            dictionaries.Remove(_ambientOverride);

        // Appended last so its tokens win over both the base theme and the chosen palette override.
        dictionaries.Add(generated);
        _ambientOverride = generated;
        _ambientIsDark = palette.IsDark;
        application.RequestedThemeVariant = palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    public void ClearArtworkPalette()
    {
        if (Application.Current is not { } application)
            return;

        if (_ambientOverride is not null)
        {
            application.Resources.MergedDictionaries.Remove(_ambientOverride);
            _ambientOverride = null;
        }

        application.RequestedThemeVariant = BaseVariant(Current);
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

        if (PaletteUri(preference) is { } uri)
        {
            if (!_paletteCache.TryGetValue(preference, out var palette))
            {
                palette = new ResourceInclude(uri) { Source = uri };
                _paletteCache[preference] = palette;
            }

            // Appended last so its tokens take precedence over the base EmuShelfTheme dictionary.
            dictionaries.Add(palette);
            _activeOverride = palette;
        }

        // A live artwork palette must stay on top of a theme change (the theme is only its fallback),
        // so re-append it after re-inserting the theme override and keep its own dark/light reading.
        if (_ambientOverride is not null)
        {
            dictionaries.Remove(_ambientOverride);
            dictionaries.Add(_ambientOverride);
            application.RequestedThemeVariant = _ambientIsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private static ResourceDictionary BuildAmbientDictionary(ArtworkPalette p)
    {
        var accent = Color.Parse(p.Accent);
        var accentBare = p.Accent.TrimStart('#');
        var overlay = p.IsDark ? Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00);

        var dictionary = new ResourceDictionary
        {
            ["EmuWindowBackgroundBrush"] = Solid(p.Background),
            ["EmuSidebarBrush"] = Solid(p.Surface),
            ["EmuToolbarBrush"] = Solid(p.Elevated),
            ["EmuLibraryBrush"] = Solid(p.Background),
            ["EmuStatusBrush"] = Solid(p.Surface),
            ["EmuCardBrush"] = Solid(p.Card),
            ["EmuPopoverBrush"] = Solid(p.Elevated),
            ["EmuInputBrush"] = Solid(p.Elevated),
            ["EmuSegmentBrush"] = Solid(p.Surface),
            ["EmuAddButtonBrush"] = Solid(p.Card),
            ["EmuCoverWellBrush"] = Solid(p.Surface),
            ["EmuPlaceholderMedallionBrush"] = Solid(p.Surface),
            ["EmuPlaceholderLabelBrush"] = Alpha(p.TextTertiary, 0x73),
            ["EmuFormatPillBrush"] = new SolidColorBrush(overlay),
            ["EmuBorderBrush"] = Solid(p.Border),
            ["EmuStrongBorderBrush"] = Solid(p.StrongBorder),
            ["EmuCoverBorderBrush"] = Solid(p.Border),
            ["EmuTextPrimaryBrush"] = Solid(p.TextPrimary),
            ["EmuTextSecondaryBrush"] = Solid(p.TextSecondary),
            ["EmuTextTertiaryBrush"] = Solid(p.TextTertiary),
            ["EmuHoverBrush"] = new SolidColorBrush(overlay),
            ["EmuNavSelectionBrush"] = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
            ["EmuNavIconWellBrush"] = Solid(p.Surface),
            ["EmuNavIconBorderBrush"] = Solid(p.StrongBorder),
            ["EmuAccentBrush"] = new SolidColorBrush(accent),
            ["EmuAccentMutedBrush"] = new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)),
            ["EmuSelectionBrush"] = new SolidColorBrush(accent),
            ["EmuAchievementBrush"] = Solid(p.IsDark ? "#E0A526" : "#A96400"),
            ["EmuInfoBrush"] = Solid(p.IsDark ? "#5AB0E0" : "#28749A"),
            ["EmuProgressBrush"] = Solid(p.IsDark ? "#5AB0E0" : "#28749A"),
            ["EmuSuccessBrush"] = Solid(p.IsDark ? "#4BB07C" : "#287A59"),
            ["EmuWarningBrush"] = Solid(p.IsDark ? "#E0A030" : "#A85E00"),
            ["EmuDangerBrush"] = Solid(p.IsDark ? "#E0555F" : "#B42332"),
            ["EmuFocusGlow"] = BoxShadows.Parse($"0 0 12 0 #B0{accentBare}"),
        };

        return dictionary;
    }

    private static SolidColorBrush Solid(string hex) => new(Color.Parse(hex));

    private static SolidColorBrush Alpha(string hex, byte alpha)
    {
        var color = Color.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static ThemeVariant BaseVariant(ThemePreference preference) => preference switch
    {
        ThemePreference.System => ThemeVariant.Default,
        // The palette's own dark/light reading (from the catalog) decides which base Fluent chrome set
        // stays legible: light palettes like Valentine/Retro base on Light, every other on Dark.
        _ => ThemeCatalog.Get(preference).IsDark ? ThemeVariant.Dark : ThemeVariant.Light,
    };

    private static Uri? PaletteUri(ThemePreference preference) => preference switch
    {
        // System, Light and Dark are served by the base EmuShelfTheme theme-dictionaries; no override.
        ThemePreference.System or ThemePreference.Light or ThemePreference.Dark => null,
        // Every other theme is a flat override whose file is named for the enum member, so a new theme
        // needs only its enum value and a matching Styles/Palettes/<Name>.axaml — no edit here.
        _ => new Uri($"avares://EmuShelf/Styles/Palettes/{preference}.axaml"),
    };
}
