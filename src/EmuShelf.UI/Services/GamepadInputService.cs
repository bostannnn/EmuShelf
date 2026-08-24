using System;
using System.Diagnostics;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Input;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>
/// Hosts the controller poll loop. It reads the physical pad ~60 times a second while the app is in
/// Gamepad mode and routes each resulting <see cref="GamepadAction"/> through the same
/// <see cref="MainViewModel.DispatchGamepadAction"/> entry point that Steam-Input keyboard mapping
/// uses, so both input paths behave identically. Polling only runs in Gamepad mode.
/// </summary>
/// <remarks>
/// When the reader is an <see cref="IPushGamepadSource"/> (the Android head, fed by MotionEvents), the
/// loop stops ticking once the pad is fully at rest and restarts from <see cref="OnInputReceived"/> on
/// the next event. That removes the couch shell's resting UI-thread CPU cost: a 60 Hz timer that reads
/// nothing and draws nothing still burns a core's worth on Android's dispatcher. A reader that is not a
/// push source (the desktop SDL reader) has no event to wake on, so it keeps polling continuously,
/// exactly as before.
/// </remarks>
public sealed class GamepadInputService : IDisposable
{
    private readonly IGamepadReader _reader;
    private readonly MainViewModel _viewModel;
    private readonly IInterfaceModeService _modeService;
    private readonly IAppLogger _logger;
    private readonly GamepadNavigationController _navigation = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // Set when the reader delivers input by events, letting the loop stop at rest and wake on input
    // (see class remarks). Null on desktop's SDL reader, which is polled continuously.
    private readonly IPushGamepadSource? _pushSource;
    private long? _previousTickMs;
    private bool _loggedAvailability;
    private bool _active;
    private bool _disposed;

    public GamepadInputService(
        IGamepadReader reader,
        MainViewModel viewModel,
        IInterfaceModeService modeService,
        IAppLogger? logger = null)
    {
        _reader = reader;
        _viewModel = viewModel;
        _modeService = modeService;
        _logger = logger ?? NullAppLogger.Instance;
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16),
            DispatcherPriority.Input,
            OnTick);

        _pushSource = reader as IPushGamepadSource;
        if (_pushSource is not null)
            _pushSource.InputReceived += OnInputReceived;

        _modeService.ModeChanged += OnModeChanged;
        SetActive(_modeService.Current == InterfaceMode.Gamepad);
    }

    private void OnModeChanged(object? sender, InterfaceMode mode) =>
        SetActive(mode == InterfaceMode.Gamepad);

    private void SetActive(bool active)
    {
        _active = active;
        if (active)
            _timer.Start();
        else
            _timer.Stop();
    }

    // A push reader (Android) fires this on the UI thread when input arrives. Wake the loop if it went
    // idle; the next tick reads the fresh input. The timebase is dropped so the first frame after an
    // idle gap starts from dt 0 rather than one huge delta that would spin the hero.
    private void OnInputReceived()
    {
        if (!_active || _timer.IsEnabled)
            return;
        _previousTickMs = null;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // An emulator may be closed with B/Escape while this process is minimized. Do not let that
        // held input reach the restored Gamepad library: reset makes the first post-session read a
        // deliberate no-op and the view model consumes late Steam-Input key events as well.
        if (_viewModel.IsGamepadInputSuspended)
        {
            _navigation.Reset();
            // Drop the timebase too: resuming after a long emulator session would otherwise hand
            // the rotation model one enormous dt and spin the hero on the first frame back.
            _previousTickMs = null;
            return;
        }

        var now = _clock.ElapsedMilliseconds;
        var deltaMs = _previousTickMs is { } previous ? now - previous : 0L;
        _previousTickMs = now;

        var reading = _reader.Read();
        LogAvailabilityOnce();

        foreach (var action in _navigation.Poll(reading, now))
            _viewModel.DispatchGamepadAction(action);

        // Analog rotation runs beside the discrete routing rather than through it: the navigation
        // controller exists to turn continuous input into repeated discrete edges, which is the
        // opposite of what a rotation wants.
        _viewModel.ApplyRightStickRotation(reading.RightStickX, reading.RightStickY, deltaMs);

        // With a push reader, once nothing is being held there is nothing to read until the next event —
        // so stop, instead of spinning the UI thread 60×/s. OnInputReceived restarts the loop. This poll
        // is only for *input*: the resting hero's idle sway runs on its own timer in the view model, so
        // stopping here at rest does not freeze it. Desktop's SDL reader is not a push source, so
        // _pushSource is null and it keeps polling. See class remarks.
        if (_pushSource is not null && IsReadingNeutral(reading))
        {
            _timer.Stop();
            _previousTickMs = null;
        }
    }

    // Whether the pad is physically at rest this frame: nothing held (d-pad/buttons), the left stick
    // inside the navigation dead zone, and the right stick inside the rotation dead zone. Read from the
    // raw reading, not from post-Poll navigation state, on purpose: a held direction is latched in the
    // reading every tick (Android sends no further MotionEvents while it is held), so a still-held input
    // keeps the loop alive here even on the frame Poll swallows after a resume/reconnect reset — which
    // post-Poll state would miss, dropping the input until release. A neutral reading guarantees no
    // future work without a new event, so the loop is free to stop (the idle-sway animation is handled
    // separately by the caller).
    private static bool IsReadingNeutral(GamepadReading reading) =>
        reading.Buttons == GamepadButtons.None
        && MathF.Abs(reading.LeftStickX) <= GamepadNavigationController.StickDeadZone
        && MathF.Abs(reading.LeftStickY) <= GamepadNavigationController.StickDeadZone
        && !IsRightStickDeflected(reading);

    private static bool IsRightStickDeflected(GamepadReading reading)
    {
        var magnitude = MathF.Sqrt(
            (reading.RightStickX * reading.RightStickX) + (reading.RightStickY * reading.RightStickY));
        return magnitude > MediaRotationModel.Deadzone;
    }

    private void LogAvailabilityOnce()
    {
        if (_loggedAvailability)
            return;
        _loggedAvailability = true;
        _logger.Information(_reader.IsAvailable
            ? "Native controller input active (SDL2)."
            : "Native controller input unavailable; using keyboard/Steam Input only.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _modeService.ModeChanged -= OnModeChanged;
        if (_pushSource is not null)
            _pushSource.InputReceived -= OnInputReceived;
        _timer.Stop();
        (_reader as IDisposable)?.Dispose();
    }
}
