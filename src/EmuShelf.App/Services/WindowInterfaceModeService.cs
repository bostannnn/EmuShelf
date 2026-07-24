using Avalonia.Controls;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>Persists the requested layout and owns its window-level fullscreen state.</summary>
public sealed class WindowInterfaceModeService : IInterfaceModeService
{
    private readonly ISettingsService _settingsService;
    private AppSettings _settings;
    private readonly Window _window;

    public InterfaceMode Current { get; private set; }
    public bool IsCommandLineOverride { get; }
    public event EventHandler<InterfaceMode>? ModeChanged;

    public WindowInterfaceModeService(
        ISettingsService settingsService,
        AppSettings settings,
        Window window,
        bool gamepadUiRequested)
    {
        _settingsService = settingsService;
        _settings = settings;
        _window = window;
        IsCommandLineOverride = gamepadUiRequested;
        Current = gamepadUiRequested ? InterfaceMode.Gamepad : settings.InterfaceMode;
        ApplyWindowState();
    }

    public async Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
    {
        Current = mode;
        ApplyWindowState();
        ModeChanged?.Invoke(this, mode);
        if (IsCommandLineOverride)
            return;

        _settings = (await Task.Run(_settingsService.Load, cancellationToken)) with { InterfaceMode = mode };
        var snapshot = _settings;
        await Task.Run(() => _settingsService.Save(snapshot), cancellationToken);
    }

    private void ApplyWindowState() =>
        _window.WindowState = Current == InterfaceMode.Gamepad
            ? WindowState.FullScreen
            : WindowState.Normal;
}
