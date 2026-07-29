using Avalonia;
using Avalonia.Controls;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>
/// Restores the main window's size, position, and maximized state at launch, and writes them back
/// when it closes.
///
/// Only the window's <em>restored</em> bounds are ever persisted. Maximized and full-screen bounds
/// describe the display, not a choice the user made about the window, so they are tracked
/// separately: quitting from Gamepad mode (which is full screen) still restores the desktop
/// window the user last sized by hand.
/// </summary>
public sealed class WindowLayoutService
{
    private readonly ISettingsService _settingsService;
    private readonly Window _window;
    private readonly IAppLogger _logger;

    private double _restoredWidth;
    private double _restoredHeight;
    private PixelPoint? _restoredPosition;
    private bool _isMaximized;

    public WindowLayoutService(
        ISettingsService settingsService,
        AppSettings settings,
        Window window,
        IAppLogger? logger = null)
    {
        _settingsService = settingsService;
        _window = window;
        _logger = logger ?? NullAppLogger.Instance;

        var layout = settings.WindowLayout;
        _restoredWidth = layout.Width;
        _restoredHeight = layout.Height;
        _isMaximized = layout.IsMaximized;

        Apply(layout);

        _window.PropertyChanged += OnWindowPropertyChanged;
        _window.PositionChanged += OnWindowPositionChanged;
        _window.Closing += (_, _) => Save();
    }

    private void Apply(WindowLayoutSettings layout)
    {
        var position = TryResolveOnScreenPosition(layout);
        var (maxWidth, maxHeight) = WorkingAreaSize(position);

        // Clamp to the display as well as to the window's own minimum: a size saved on a large
        // external monitor must not reopen larger than the laptop panel it is restored on.
        _window.Width = Math.Clamp(layout.Width, _window.MinWidth, maxWidth);
        _window.Height = Math.Clamp(layout.Height, _window.MinHeight, maxHeight);

        if (position is { } onScreen)
        {
            _window.WindowStartupLocation = WindowStartupLocation.Manual;
            _window.Position = onScreen;
            _restoredPosition = onScreen;
        }

        if (layout.IsMaximized)
            _window.WindowState = WindowState.Maximized;
    }

    /// <summary>
    /// The usable size, in DIPs, of the screen the window is being restored onto. Falls back to
    /// the saved size's own bounds when no screen can be resolved, so an unknown display never
    /// shrinks the window.
    /// </summary>
    private (double Width, double Height) WorkingAreaSize(PixelPoint? position)
    {
        try
        {
            var screen = position is { } point
                ? _window.Screens.ScreenFromPoint(point)
                : _window.Screens.Primary;
            if (screen is null)
                return (double.MaxValue, double.MaxValue);

            var scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
            return (screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not measure the target display: {ex.Message}");
            return (double.MaxValue, double.MaxValue);
        }
    }

    /// <summary>
    /// A position saved on a monitor that is no longer attached would open the window somewhere the
    /// user cannot reach it. Returns null in that case so the window falls back to centring.
    /// </summary>
    private PixelPoint? TryResolveOnScreenPosition(WindowLayoutSettings layout)
    {
        if (layout.Left is not { } left || layout.Top is not { } top)
            return null;

        try
        {
            var position = new PixelPoint(left, top);
            // Test a point just inside the title bar rather than the exact corner: a window flush
            // against the left edge of a secondary monitor can have a corner one pixel outside it.
            var probe = new PixelPoint(left + 8, top + 8);
            return _window.Screens.ScreenFromPoint(probe) is null ? null : position;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not validate the saved window position: {ex.Message}");
            return null;
        }
    }

    // Size and position are only meaningful while the window is in its restored state. Sampling
    // them then means the values written at close describe the window, not the monitor it was
    // maximized or made full screen on.
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            if (_window.WindowState is WindowState.Normal or WindowState.Maximized)
                _isMaximized = _window.WindowState == WindowState.Maximized;
            return;
        }

        if (e.Property == Visual.BoundsProperty && _window.WindowState == WindowState.Normal)
        {
            _restoredWidth = _window.Width;
            _restoredHeight = _window.Height;
        }
    }

    private void OnWindowPositionChanged(object? sender, PixelPointEventArgs e)
    {
        if (_window.WindowState == WindowState.Normal)
            _restoredPosition = e.Point;
    }

    private void Save()
    {
        try
        {
            _settingsService.Update(latest => latest with
            {
                WindowLayout = new WindowLayoutSettings
                {
                    Width = _restoredWidth,
                    Height = _restoredHeight,
                    Left = _restoredPosition?.X,
                    Top = _restoredPosition?.Y,
                    IsMaximized = _isMaximized,
                },
            });
        }
        catch (Exception ex)
        {
            // The window is already closing; there is nowhere useful to report this.
            _logger.Warning($"Could not persist the window layout: {ex.Message}");
        }
    }
}
