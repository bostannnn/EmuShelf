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
public sealed class GamepadInputService : IDisposable
{
    private readonly IGamepadReader _reader;
    private readonly MainViewModel _viewModel;
    private readonly IInterfaceModeService _modeService;
    private readonly IAppLogger _logger;
    private readonly GamepadNavigationController _navigation = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long? _previousTickMs;
    private bool _loggedAvailability;
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

        _modeService.ModeChanged += OnModeChanged;
        SetActive(_modeService.Current == InterfaceMode.Gamepad);
    }

    private void OnModeChanged(object? sender, InterfaceMode mode) =>
        SetActive(mode == InterfaceMode.Gamepad);

    private void SetActive(bool active)
    {
        if (active)
            _timer.Start();
        else
            _timer.Stop();
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
        _timer.Stop();
        (_reader as IDisposable)?.Dispose();
    }
}
