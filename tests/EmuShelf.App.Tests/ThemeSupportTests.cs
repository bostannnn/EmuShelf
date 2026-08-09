using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

public class ThemeSupportTests
{
    [Fact]
    public void Catalog_CoversEveryThemePreferenceWithParsableSwatches()
    {
        Assert.Equal(
            Enum.GetValues<ThemePreference>().OrderBy(id => id),
            ThemeCatalog.All.Select(theme => theme.Id).OrderBy(id => id));

        foreach (var theme in ThemeCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(theme.Name));
            // Every swatch must be a real color so the gallery never renders a hole.
            foreach (var hex in new[]
                     {
                         theme.PreviewBackground, theme.PreviewSurface,
                         theme.PreviewAccent, theme.PreviewText,
                     })
            {
                _ = Color.Parse(hex);
            }
        }
    }

    [Fact]
    public async Task ThemeChoice_SelectCommand_AppliesItsTheme()
    {
        ThemePreference? applied = null;
        var choice = new ThemeChoiceViewModel(
            ThemeCatalog.Get(ThemePreference.Oled),
            preference =>
            {
                applied = preference;
                return Task.CompletedTask;
            });

        await choice.SelectCommand.ExecuteAsync(null);

        Assert.Equal(ThemePreference.Oled, applied);
        Assert.Equal(ThemePreference.Oled, choice.Id);
    }

    [AvaloniaFact]
    public async Task AppThemeService_SwapsPaletteTokensAndPersists()
    {
        var settings = new InMemorySettingsService(new AppSettings());
        var service = new AppThemeService(settings, settings.Current);
        try
        {
            await service.SetThemeAsync(ThemePreference.Oled);
            Assert.Equal(ThemePreference.Oled, settings.Current.Theme);
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            Assert.Equal(Color.Parse("#5B58D9"), ResolveAccentColor());

            await service.SetThemeAsync(ThemePreference.Dracula);
            Assert.Equal(Color.Parse("#BD93F9"), ResolveAccentColor());

            // Returning to a base variant must drop the override and restore the base token.
            await service.SetThemeAsync(ThemePreference.Dark);
            Assert.Equal(Color.Parse("#5B58D9"), ResolveAccentColor());
        }
        finally
        {
            await service.SetThemeAsync(ThemePreference.System);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task AppThemeService_EveryCatalogThemeLoadsAndMatchesItsAccentSwatch()
    {
        // Applying each catalog theme must resolve its palette (a mistyped file name would only surface
        // when a user picks that theme) and land the accent the gallery advertised, so no palette can
        // drift from its swatch. System follows the OS with no fixed variant, so it is exercised
        // separately below.
        var settings = new InMemorySettingsService(new AppSettings());
        var service = new AppThemeService(settings, settings.Current);
        try
        {
            foreach (var theme in ThemeCatalog.All.Where(t => t.Id != ThemePreference.System))
            {
                await service.SetThemeAsync(theme.Id);
                var variant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
                Assert.True(
                    Application.Current!.TryGetResource("EmuAccentBrush", variant, out var value),
                    $"{theme.Id} did not resolve EmuAccentBrush");
                Assert.Equal(
                    Color.Parse(theme.PreviewAccent),
                    Assert.IsAssignableFrom<ISolidColorBrush>(value).Color);
            }
        }
        finally
        {
            await service.SetThemeAsync(ThemePreference.System);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task AppThemeService_AppliesSavedPaletteAtConstruction()
    {
        // A palette saved in settings must be applied when the service is built at startup, not only
        // via a later SetThemeAsync call.
        var settings = new InMemorySettingsService(new AppSettings { Theme = ThemePreference.Dracula });
        var service = new AppThemeService(settings, settings.Current);
        try
        {
            Assert.Equal(ThemePreference.Dracula, service.Current);
            Assert.Equal(Color.Parse("#BD93F9"), ResolveAccentColor());
        }
        finally
        {
            await service.SetThemeAsync(ThemePreference.System);
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public void GamepadThemeGallery_NavigatesGridAndAppliesFocusedTheme()
    {
        ThemePreference? applied = null;
        var choices = ThemeCatalog.All.Select(theme => new ThemeChoiceViewModel(theme)).ToArray();
        var gamepad = new GamepadSettingsViewModel(
            CreateSettings(),
            onScreenKeyboard: null,
            themeChoices: choices,
            applyTheme: preference =>
            {
                applied = preference;
                return Task.CompletedTask;
            });

        Assert.True(gamepad.ShowThemes);

        // Themes is the page after the model sections; LB/RB reaches it and back.
        Assert.False(gamepad.IsThemesSection);
        gamepad.SelectThemesCommand.Execute(null);
        Assert.True(gamepad.IsThemesSection);
        Assert.True(gamepad.IsThemesVisible);
        Assert.False(gamepad.IsRowsVisible);
        Assert.True(choices[gamepad.FocusedThemeIndex].IsFocused);

        // Right then down moves within the 3-column grid.
        gamepad.Dispatch(GamepadAction.NavigateRight);
        Assert.Equal(1, gamepad.FocusedThemeIndex);
        gamepad.Dispatch(GamepadAction.NavigateDown);
        Assert.Equal(4, gamepad.FocusedThemeIndex);
        Assert.True(choices[4].IsFocused);
        Assert.False(choices[1].IsFocused);

        gamepad.Dispatch(GamepadAction.Confirm);
        Assert.Equal(choices[4].Id, applied);

        // Leaving Themes shows the row list again without touching the model section.
        gamepad.Dispatch(GamepadAction.PreviousPlatform);
        Assert.False(gamepad.IsThemesSection);
        Assert.True(gamepad.IsRowsVisible);
    }

    [AvaloniaFact]
    public void ThemesSection_IsPresentInBothSettingsSurfaces()
    {
        var choices = ThemeCatalog.All.Select(theme => new ThemeChoiceViewModel(theme)).ToArray();

        // Desktop settings gains a Themes section only when theme choices are supplied.
        Assert.DoesNotContain(SettingsSection.Themes, CreateSettings().Sections);
        var desktop = CreateSettings(choices);
        Assert.Contains(SettingsSection.Themes, desktop.Sections);
        Assert.True(desktop.HasThemes);
        Assert.Equal(choices.Length, desktop.ThemeChoices.Count);

        // Gamepad presents themes as its own gallery page, so Themes is the one section that is not a
        // projected row (the gallery is still reachable via ShowThemes). Every other section, Emulators
        // included, is a projected rail section so both modes share one structure.
        var gamepad = new GamepadSettingsViewModel(desktop, null, choices, _ => Task.CompletedTask);
        Assert.DoesNotContain(SettingsSection.Themes, gamepad.Sections);
        Assert.Contains(SettingsSection.Emulators, gamepad.Sections);
        Assert.True(gamepad.ShowThemes);
    }

    [AvaloniaFact]
    public void GamepadSettings_RailFocus_MovesSectionsWithArrows()
    {
        var choices = ThemeCatalog.All.Select(theme => new ThemeChoiceViewModel(theme)).ToArray();
        var gamepad = new GamepadSettingsViewModel(
            CreateSettings(), null, choices, _ => Task.CompletedTask);

        // Content is focused first; Left steps out to the vertical section rail.
        Assert.False(gamepad.IsRailFocused);
        gamepad.Dispatch(GamepadAction.NavigateLeft);
        Assert.True(gamepad.IsRailFocused);

        // On the rail, Down/Up move between adjacent sections (Library <-> Emulators).
        Assert.Equal(SettingsSection.General, gamepad.SelectedSection);
        gamepad.Dispatch(GamepadAction.NavigateDown);
        Assert.Equal(SettingsSection.Emulators, gamepad.SelectedSection);
        gamepad.Dispatch(GamepadAction.NavigateUp);
        Assert.Equal(SettingsSection.General, gamepad.SelectedSection);

        // Paging Down through every section lands on the Themes gallery page appended at the end.
        for (var index = 0; index < gamepad.Sections.Count; index++)
            gamepad.Dispatch(GamepadAction.NavigateDown);
        Assert.True(gamepad.IsThemesSection);

        // Right (or A) returns to the content column.
        gamepad.Dispatch(GamepadAction.NavigateRight);
        Assert.False(gamepad.IsRailFocused);
    }

    [AvaloniaFact]
    public void GamepadThemeGallery_ClampsAtRowEdgesWithoutWrapping()
    {
        var choices = ThemeCatalog.All.Select(theme => new ThemeChoiceViewModel(theme)).ToArray();
        var gamepad = new GamepadSettingsViewModel(
            CreateSettings(), null, choices, _ => Task.CompletedTask);
        gamepad.SelectThemesCommand.Execute(null);

        // Left at column 0 stays put rather than wrapping to the previous row's end.
        gamepad.FocusedThemeIndex = 3;
        gamepad.MoveThemeFocus(-1, 0);
        Assert.Equal(3, gamepad.FocusedThemeIndex);

        // Down past the last row is ignored.
        gamepad.FocusedThemeIndex = choices.Length - 1;
        gamepad.MoveThemeFocus(0, 1);
        Assert.Equal(choices.Length - 1, gamepad.FocusedThemeIndex);
    }

    private static Color ResolveAccentColor()
    {
        Assert.True(Application.Current!.TryGetResource(
            "EmuAccentBrush", ThemeVariant.Dark, out var value));
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static EmulatorSettingsViewModel CreateSettings(
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null) => new(
        KnownSystems.All,
        KnownEmulators.All,
        KnownSystems.All.ToDictionary(
            system => system.Id, _ => (EmulatorConfiguration?)null, StringComparer.Ordinal),
        new NullEmulatorConfigurationStore(),
        new NullDialogService(),
        themeChoices: themeChoices);

    private sealed class InMemorySettingsService(AppSettings initial) : ISettingsService
    {
        public AppSettings Current { get; private set; } = initial;

        public AppSettings Load() => Current;

        public void Save(AppSettings settings) => Current = settings;
    }
}
