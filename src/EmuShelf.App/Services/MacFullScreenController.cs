using Avalonia;
using Avalonia.Controls;

namespace EmuShelf.App.Services;

/// <summary>
/// macOS-only: makes <see cref="WindowState.FullScreen"/> actually fill the screen for EmuShelf's
/// borderless window.
///
/// EmuShelf's window has no system decorations (<c>WindowDecorations="None"</c> plus an extended
/// client area), and Avalonia's native macOS fullscreen is a no-op for such a window — the managed
/// <see cref="Window.WindowState"/> reports <c>FullScreen</c> while the NSWindow stays at its floating
/// size, so Gamepad mode (and any fullscreen request) opened as a small window. Every fullscreen entry
/// point — launching into Gamepad, switching modes, returning from a game, the desktop F11 /
/// Cmd+Ctrl+F toggle — funnels through <c>WindowState = FullScreen</c>, so this one observer mirrors
/// that state onto a manual borderless fill of the window's display and restores the previous size
/// when it leaves fullscreen. The window sits below the menu bar (macOS keeps a normal-level window
/// out from under it); true menu-bar-hiding fullscreen would need a native fullscreen space, which
/// AppKit will not grant a borderless window. Constructed and inert on every other platform.
/// </summary>
public sealed class MacFullScreenController
{
    private readonly Window _window;
    private double _restoreWidth;
    private double _restoreHeight;
    private PixelPoint? _restorePosition;
    private bool _filling;

    public MacFullScreenController(Window window)
    {
        _window = window;
        if (!OperatingSystem.IsMacOS())
            return;

        _window.PropertyChanged += OnWindowPropertyChanged;

        // A launch straight into Gamepad sets FullScreen before the window is shown; the native size
        // can only be applied once it exists, so fill on open. A window that is already visible and
        // fullscreen (defensive) fills immediately.
        if (_window.WindowState == WindowState.FullScreen)
        {
            if (_window.IsVisible)
                Fill();
            else
                _window.Opened += OnWindowOpened;
        }
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _window.Opened -= OnWindowOpened;
        if (_window.WindowState == WindowState.FullScreen)
            Fill();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Window.WindowStateProperty)
            return;

        if (_window.WindowState == WindowState.FullScreen)
        {
            // Only fill once the window exists; the launch case is handled by OnWindowOpened.
            if (_window.IsVisible)
                Fill();
        }
        else
        {
            Restore();
        }
    }

    // Size the borderless window to cover its display. Setting width/height/position does not change
    // WindowState, so this never re-enters the observer. macOS clamps the frame to the working area
    // (below the menu bar), which is the fill we want.
    private void Fill()
    {
        if (_filling)
            return;

        var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
        if (screen is null)
            return;

        // Remember the floating window to come back to when fullscreen is dismissed.
        _restoreWidth = _window.Width;
        _restoreHeight = _window.Height;
        _restorePosition = _window.Position;
        _filling = true;

        var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        _window.Position = screen.Bounds.Position;
        _window.Width = screen.Bounds.Width / scaling;
        _window.Height = screen.Bounds.Height / scaling;
    }

    private void Restore()
    {
        if (!_filling)
            return;
        _filling = false;

        if (_restoreWidth > 0)
            _window.Width = _restoreWidth;
        if (_restoreHeight > 0)
            _window.Height = _restoreHeight;
        if (_restorePosition is { } position)
            _window.Position = position;
    }
}
