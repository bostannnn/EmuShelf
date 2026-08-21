using Android.Views;
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
/// Buttons and the D-pad are deliberately <b>not</b> reported here — those arrive as key events and are
/// already routed through <see cref="AndroidGamepadInput"/>/<c>DispatchKeyEvent</c>, so reporting
/// <see cref="GamepadButtons.None"/> keeps every button on exactly one path (no double-firing). Axes are kept
/// raw (undeadzoned) as the interface requires; the two consumers apply their own dead zones.
///
/// Fed and read on the same thread (Android's main thread, which is Avalonia's UI thread), so the plain
/// fields need no synchronisation.
/// </summary>
public sealed class AndroidGamepadReader : IGamepadReader
{
    /// <summary>The single instance the Activity feeds; created and published by the Android application.</summary>
    public static AndroidGamepadReader? Current { get; set; }

    private float _leftX;
    private float _leftY;
    private float _rightX;
    private float _rightY;

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
    }

    // IsConnected must be true or the shared GamepadNavigationController drops the whole reading; when no pad
    // is present the axes simply stay zero and every consumer no-ops, so reporting connected is safe.
    public GamepadReading Read() =>
        new(GamepadButtons.None, _leftX, _leftY, IsConnected: true, _rightX, _rightY);
}
