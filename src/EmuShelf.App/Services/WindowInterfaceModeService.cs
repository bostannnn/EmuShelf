using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Threading;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>Persists the requested layout and owns its window-level fullscreen state.</summary>
public sealed class WindowInterfaceModeService : IInterfaceModeService
{
    // Gamepad mode is a controller/TV surface with no pointer, so it hides the cursor outright. A
    // single shared instance is reused across every mode switch; the value is compared by reference
    // in tests, so it must stay the same object.
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);

    /// <summary>Avalonia log area for the couch mode-switch geometry trace; captured to Logs/.</summary>
    internal const string ModeSwitchLogArea = "EmuShelf.InterfaceMode";

    /// <summary>
    /// How long the switch into Gamepad waits for the window to actually resize to full screen before
    /// giving up and proceeding anyway. Bounded so a window manager that never reports the resize (or a
    /// maximized window whose size already equals full screen) cannot stall the mode switch.
    /// </summary>
    private static readonly TimeSpan FullScreenSettleTimeout = TimeSpan.FromMilliseconds(400);

    private readonly ISettingsService _settingsService;
    private AppSettings _settings;
    private readonly Window _window;

    // The desktop window to restore when leaving Gamepad mode. Null until a real desktop session
    // exists: an app launched straight into Gamepad has none, so its first trip to Desktop maximizes
    // rather than restoring the transient startup window.
    private WindowState? _desktopWindowState;

    public InterfaceMode Current { get; private set; }
    public bool IsCommandLineOverride { get; }

    // Desktop always has a window shell — even under a forced-Gamepad command-line override (Steam
    // Gaming Mode), the desktop shell exists and is reachable, so "switch to Desktop" stays offered.
    public bool SupportsDesktopMode => true;

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
            // none yet, so a later switch to Desktop maximizes instead of restoring it. On macOS this
            // FullScreen state is turned into a real screen-filling window by MacFullScreenController,
            // where Avalonia's native fullscreen is a no-op for our borderless window.
            _window.WindowState = WindowState.FullScreen;
        }
        else
        {
            // Launched into Desktop: the current window is the user's desktop window; remember it so
            // a later trip through Gamepad restores it exactly (maximized stays maximized).
            _desktopWindowState = _window.WindowState;
        }

        ApplyCursor();
    }

    public async Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
    {
        // The couch UI is presented through a full-window GL "tube" that captures GamepadRoot (rail and
        // all) into a texture and draws it, warped, over the top — the live rail is meant to sit hidden
        // behind the opaque tube. When we enter Gamepad from a windowed/maximized desktop, the window
        // goes full screen; on Linux/gamescope that resize is applied asynchronously, so if the tube's
        // GL surface is stood up at the old (smaller) geometry it can fail to cover the now-full-screen
        // live rail, and both the flat live rail and the tube's warped copy show at once — the "doubled
        // platform row" seen only on the desktop→gamepad path (an app launched straight into Gamepad is
        // full screen from birth and never hits it). So hold the mode change until the window has
        // actually resized, so the tube and its capture are both built at final geometry.
        var awaitFullScreenResize = mode == InterfaceMode.Gamepad
            && _window.WindowState != WindowState.FullScreen;
        var sizeBeforeFullScreen = _window.ClientSize;

        Current = mode;
        ApplyWindowState();
        ApplyCursor();

        if (awaitFullScreenResize)
        {
            await WaitForFullScreenResizeAsync(sizeBeforeFullScreen);

            // A second switch could have landed during the wait and already fired its own
            // ModeChanged; don't fire this now-stale one on top of it and flip the mode back.
            if (Current != mode)
                return;
        }

        ModeChanged?.Invoke(this, mode);
        if (IsCommandLineOverride)
            return;

        _settings = await Task.Run(
            () => _settingsService.Update(latest => latest with { InterfaceMode = mode }),
            cancellationToken);
    }

    /// <summary>
    /// Completes once the window's client size has actually changed from <paramref name="sizeBefore"/>
    /// (the full-screen resize has landed) or a short timeout elapses, whichever comes first. Avalonia
    /// flips <see cref="Window.WindowState"/> to full screen synchronously, but on Linux/gamescope the
    /// real resize arrives a few frames later — so this waits on the resize itself, not the state flag.
    /// The timeout keeps a window whose size does not change (a maximized window already at full-screen
    /// size, or a WM that never reports it) from stalling the switch.
    /// </summary>
    private Task WaitForFullScreenResizeAsync(Size sizeBefore)
    {
        if (_window.ClientSize != sizeBefore)
        {
            LogModeSwitch("full-screen size already settled", sizeBefore, _window.ClientSize);
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = FullScreenSettleTimeout };
        EventHandler<AvaloniaPropertyChangedEventArgs>? onWindowChanged = null;

        void Finish(string reason)
        {
            if (!completion.TrySetResult())
                return;
            timer.Stop();
            _window.PropertyChanged -= onWindowChanged;
            LogModeSwitch(reason, sizeBefore, _window.ClientSize);
        }

        onWindowChanged = (_, change) =>
        {
            if (change.Property == Window.ClientSizeProperty && _window.ClientSize != sizeBefore)
                Finish("full-screen resize landed");
        };

        timer.Tick += (_, _) => Finish("full-screen resize wait timed out");
        _window.PropertyChanged += onWindowChanged;
        timer.Start();
        return completion.Task;
    }

    private static void LogModeSwitch(string reason, Size before, Size after) =>
        Logger.TryGet(LogEventLevel.Information, ModeSwitchLogArea)?.Log(
            null,
            "Gamepad entry: {Reason} (client size {Before} -> {After}).",
            reason,
            $"{before.Width:0}x{before.Height:0}",
            $"{after.Width:0}x{after.Height:0}");

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

    // Hide the pointer in Gamepad mode so an accidental mouse or trackpad bump can't park a visible
    // cursor over the controller UI; Desktop restores the normal arrow. Mouse *input* is separately
    // disabled by making the gamepad surface non-hit-testable (see GamepadRoot in MainWindow.axaml).
    private void ApplyCursor() =>
        _window.Cursor = Current == InterfaceMode.Gamepad ? HiddenCursor : Cursor.Default;
}
