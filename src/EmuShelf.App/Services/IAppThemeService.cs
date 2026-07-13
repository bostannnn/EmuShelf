using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

public interface IAppThemeService
{
    ThemePreference Current { get; }

    Task SetThemeAsync(
        ThemePreference preference,
        CancellationToken cancellationToken = default);
}
