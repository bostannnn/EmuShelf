using System;
using EmuShelf.App.Services;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Routes gamepad input to the Thor companion when the second screen owns input focus. The Thor follows the
/// standard "whichever screen you last touched owns the gamepad" model: a touch on the companion sets
/// <see cref="IsActive"/> (see <c>ThorSecondScreenPresentation.DispatchTouchEvent</c>), a touch on the main
/// screen clears it (see <c>MainActivity.DispatchTouchEvent</c>). While active,
/// <see cref="MainActivity.DispatchKeyEvent"/> offers each mapped action to <see cref="Dispatch"/> before
/// the couch view model, so the D-pad walks the second-screen achievements grid instead of the library.
/// The controller wires <see cref="Dispatch"/> to the live companion view model and clears everything on
/// teardown; all access is on the Android main thread, so the fields need no synchronisation.
/// </summary>
internal static class SecondScreenInputFocus
{
    /// <summary>True while the second screen was the last surface touched, so it owns the gamepad.</summary>
    public static bool IsActive { get; set; }

    /// <summary>
    /// Offers a mapped action to the companion; returns true when it was consumed (grid move, or Back
    /// closing an overlay). False lets the caller fall back to the couch, so the gamepad is never dead when
    /// the companion has nothing navigable up.
    /// </summary>
    public static Func<GamepadAction, bool>? Dispatch { get; set; }

    /// <summary>Clears focus and unwires the dispatcher — called when the companion is torn down.</summary>
    public static void Reset()
    {
        IsActive = false;
        Dispatch = null;
    }
}
