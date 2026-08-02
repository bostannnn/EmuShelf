using Avalonia.Controls;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>Persists the requested layout and owns its window-level fullscreen state.</summary>
public sealed class WindowInterfaceModeService : IInterfaceModeService
{
    private readonly ISettingsService _settingsService;
    private AppSettings _settings;
    private readonly Window _window;

    // The desktop window to restore when leaving Gamepad mode. Null until a real desktop session
    // exists: an app launched straight into Gamepad has none, so its first trip to Desktop maximizes
    // rather than restoring the transient startup window.
    private WindowState? _desktopWindowState;

    public InterfaceMode Current { get; private set; }
    public bool IsCommandLineOverride { get; }
    public event EventHandler<InterfaceMode>? ModeChanged;

    public WindowInterfaceModeService(
        ISettingsService settingsService,
        AppSettings settings,
        Window window,
        InterfaceMode? interfaceModeOverride)
    {
        _settingsService = settingsService;
        _settings = settings;
        _window = window;
        IsCommandLineOverride = interfaceModeOverride is not null;
        Current = interfaceModeOverride ?? settings.InterfaceMode;

        if (Current == InterfaceMode.Gamepad)
        {
            // Go full screen without recording the startup window as a desktop session — there is
            // none yet, so a later switch to Desktop maximizes instead of restoring it.
            _window.WindowState = WindowState.FullScreen;
        }
        else
        {
            // Launched into Desktop: the current window is the user's desktop window; remember it so
            // a later trip through Gamepad restores it exactly (maximized stays maximized).
            _desktopWindowState = _window.WindowState;
        }
    }

    public async Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
    {
        Current = mode;
        ApplyWindowState();
        ModeChanged?.Invoke(this, mode);
        if (IsCommandLineOverride)
            return;

        _settings = await Task.Run(
            () => _settingsService.Update(latest => latest with { InterfaceMode = mode }),
            cancellationToken);
    }

    /// <summary>
    /// Gamepad mode takes the window full screen. Returning to Desktop restores whatever state the
    /// window was in beforehand rather than assuming Normal — otherwise a maximized library that
    /// takes a trip through Gamepad mode comes back un-maximized. When the app launched straight
    /// into Gamepad (a handheld/TV), there is no such window, so the first return to Desktop
    /// maximizes rather than dropping to a small floating window that reads as a "weird size."
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
        // state it should be in — at startup that is the state restored from settings, which
        // assigning unconditionally here would discard.
        if (_window.WindowState == WindowState.FullScreen)
            _window.WindowState = _desktopWindowState ?? WindowState.Maximized;
    }
}
