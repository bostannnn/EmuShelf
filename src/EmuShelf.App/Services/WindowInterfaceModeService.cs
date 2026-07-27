using Avalonia.Controls;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>Persists the requested layout and owns its window-level fullscreen state.</summary>
public sealed class WindowInterfaceModeService : IInterfaceModeService
{
    private readonly ISettingsService _settingsService;
    private AppSettings _settings;
    private readonly Window _window;
    private WindowState _desktopWindowState = WindowState.Normal;

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

    /// <summary>
    /// Gamepad mode takes the window full screen. Returning to Desktop restores whatever state the
    /// window was in beforehand rather than assuming Normal — otherwise a maximized library that
    /// takes a trip through Gamepad mode comes back un-maximized.
    /// </summary>
    private void ApplyWindowState()
    {
        if (Current == InterfaceMode.Gamepad)
        {
            if (_window.WindowState != WindowState.FullScreen)
                _desktopWindowState = _window.WindowState;
            _window.WindowState = WindowState.FullScreen;
            return;
        }

        // Only bring the window *out* of full screen. In Desktop mode the window is already in the
        // state it should be in — at startup that is the maximized state restored from settings,
        // which assigning unconditionally here would discard.
        if (_window.WindowState == WindowState.FullScreen)
            _window.WindowState = _desktopWindowState;
    }
}
