using System;
using Android.Views;
using EmuShelf.App.Services;
using EmuShelf.Core.Input;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The Android analog-stick source for the shared controller poll loop (<c>GamepadInputService</c>), which
/// on desktop reads SDL2. SDL cannot read Android input, so this bridges the gap: Android delivers stick
/// motion as <see cref="MotionEvent"/>s at the Activity (event-driven), while <see cref="IGamepadReader"/> is
/// polled (<see cref="Read"/> returns the latest snapshot). The Activity feeds each joystick move into
/// <see cref="Update"/>; the poll loop samples the stored axes ~60×/s and turns them into left-stick
/// navigation and right-stick 3D-hero rotation through the same shared logic desktop uses.
///
/// Face/shoulder buttons are deliberately <b>not</b> reported here — those arrive as key events and are
/// already routed through <see cref="AndroidGamepadInput"/>/<c>DispatchKeyEvent</c>, so leaving them out
/// keeps every button on exactly one path (no double-firing). <b>The D-pad is the exception:</b> on the Thor
/// controller it is a hat <em>axis</em> (`ABS_HAT0X/Y`), delivered as a <see cref="MotionEvent"/> — not as
/// D-pad key events — so it never reaches <c>DispatchKeyEvent</c>. It is read here and surfaced as the
/// <c>Dpad*</c> buttons, which the shared <c>GamepadNavigationController</c> already turns into auto-repeating
/// navigation. (A device that instead sends real D-pad key events would double-fire; none in the target set
/// does — the hat is the observed shape.) Stick axes are kept raw (undeadzoned) as the interface requires;
/// the two consumers apply their own dead zones.
///
/// Fed and read on the same thread (Android's main thread, which is Avalonia's UI thread), so the plain
/// fields need no synchronisation.
/// </summary>
public sealed class AndroidGamepadReader : IGamepadReader, IPushGamepadSource
{
    /// <summary>The single instance the Activity feeds; created and published by the Android application.</summary>
    public static AndroidGamepadReader? Current { get; set; }

    /// <summary>
    /// Raised after each stick/hat <see cref="MotionEvent"/> is stored, so the shared poll loop can stop
    /// ticking while the pad rests and wake here instead of spinning ~60×/s. Fires on the Activity's
    /// thread (the main/UI thread), where the loop also runs, so no marshalling is needed.
    /// </summary>
    public event Action? InputReceived;

    /// <summary>
    /// Where a D-pad/stick direction goes while no shell poll loop exists to read this reader — the
    /// pre-boot setup page. The Thor's D-pad is a hat axis, not key events, so without this the page's
    /// A/B/START worked and nothing moved. Edge-triggered (one action per press), like a key-down. The
    /// head points it at the same dispatcher the key-event bridge uses; once the shell is up the
    /// App-level onboarding hook is null, so this becomes a no-op and the poll loop owns the pad.
    /// </summary>
    public static Func<GamepadAction, bool>? PreShellNavigate { get; set; }

    private GamepadAction? _lastPreShellDirection;

    private float _leftX;
    private float _leftY;
    private float _rightX;
    private float _rightY;
    private GamepadButtons _dpad;

    /// <summary>Whether a joystick/gamepad source device is currently attached. Read once, for logging.</summary>
    public bool IsAvailable
    {
        get
        {
            foreach (var id in InputDevice.GetDeviceIds())
            {
                var sources = InputDevice.GetDevice(id)?.Sources ?? 0;
                if (sources.HasFlag(InputSourceType.Joystick) || sources.HasFlag(InputSourceType.Gamepad))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Stores the current stick positions from a joystick <see cref="MotionEvent"/>. Android's axis sign
    /// convention matches SDL's (right/down positive), and the right stick maps to Z/RZ on the standard
    /// Xbox-style layout the Thor's controller reports, so the axes pass straight through to the same
    /// consumers as the desktop reader.
    /// </summary>
    public void Update(MotionEvent motion)
    {
        _leftX = motion.GetAxisValue(Axis.X);
        _leftY = motion.GetAxisValue(Axis.Y);
        _rightX = motion.GetAxisValue(Axis.Z);
        _rightY = motion.GetAxisValue(Axis.Rz);

        // The D-pad is a hat axis here (see class remarks): HAT_X/HAT_Y are normalised to -1/0/+1, so a held
        // direction stays latched between events (the poll loop keeps sampling it) and a release event brings
        // it back to 0. Surface it as the Dpad* buttons for the shared navigation controller.
        var hatX = motion.GetAxisValue(Axis.HatX);
        var hatY = motion.GetAxisValue(Axis.HatY);
        _dpad = GamepadButtons.None;
        if (hatX < -0.5f)
            _dpad |= GamepadButtons.DpadLeft;
        else if (hatX > 0.5f)
            _dpad |= GamepadButtons.DpadRight;
        if (hatY < -0.5f)
            _dpad |= GamepadButtons.DpadUp;
        else if (hatY > 0.5f)
            _dpad |= GamepadButtons.DpadDown;

        // Wake the poll loop (it stops itself when the pad is at rest). A release event lands here too —
        // its axes are zero — so the loop gets one more tick to process the release, then quiets again.
        InputReceived?.Invoke();

        DispatchPreShellNavigation();
    }

    private void DispatchPreShellNavigation()
    {
        if (PreShellNavigate is not { } navigate)
            return;

        // Hat first, then the left stick past half travel. Vertical wins over horizontal because the
        // setup page is a vertical list; Left/Right are still delivered for choice rows.
        GamepadAction? direction =
            _dpad.HasFlag(GamepadButtons.DpadUp) || _leftY < -0.5f ? GamepadAction.NavigateUp
            : _dpad.HasFlag(GamepadButtons.DpadDown) || _leftY > 0.5f ? GamepadAction.NavigateDown
            : _dpad.HasFlag(GamepadButtons.DpadLeft) || _leftX < -0.5f ? GamepadAction.NavigateLeft
            : _dpad.HasFlag(GamepadButtons.DpadRight) || _leftX > 0.5f ? GamepadAction.NavigateRight
            : null;

        if (direction == _lastPreShellDirection)
            return;
        _lastPreShellDirection = direction;
        if (direction is { } action)
            navigate(action);
    }

    // IsConnected must be true or the shared GamepadNavigationController drops the whole reading; when no pad
    // is present the axes simply stay zero and every consumer no-ops, so reporting connected is safe.
    public GamepadReading Read() =>
        new(_dpad, _leftX, _leftY, IsConnected: true, _rightX, _rightY);
}
