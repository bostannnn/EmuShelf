using Avalonia;
using Avalonia.Styling;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

public sealed class AppThemeService : IAppThemeService
{
    private readonly ISettingsService _settingsService;
    private AppSettings _settings;

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
        // Merge against the latest file so saving appearance cannot revert metadata
        // consent changed by the independent preferences service.
        _settings = (await Task.Run(_settingsService.Load, cancellationToken)) with
        {
            Theme = preference,
        };
        var snapshot = _settings;
        await Task.Run(() => _settingsService.Save(snapshot), cancellationToken);
    }

    private static void ApplyToApplication(ThemePreference preference)
    {
        if (Application.Current is not { } application)
            return;

        application.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
