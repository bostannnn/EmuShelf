using System;
using Android.Views;
using EmuShelf.App.Services;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The bridge from the Activity's Android key events to the shared view-model's couch dispatcher.
/// Android gamepad buttons (BUTTON_A/B/X/Y/START, the D-pad) arrive at <c>Activity.DispatchKeyEvent</c>
/// but Avalonia's own <c>KeyDown</c> reports them as <c>Key.None</c>, so the Activity — not a control —
/// is the head's couch input surface. <see cref="SingleViewShell"/> points <see cref="Dispatch"/> at the
/// live <c>MainViewModel.DispatchGamepadAction</c> once the view is shown; the Activity calls it on the
/// Android main thread (the same thread Avalonia's UI runs on), so no marshalling is needed. Analog-stick
/// motion takes a separate path (<c>AndroidGamepadReader</c> via <c>DispatchGenericMotionEvent</c>).
/// </summary>
public static class AndroidGamepadInput
{
    /// <summary>Routes a mapped controller action to the view model; returns true if it was handled.</summary>
    public static Func<GamepadAction, bool>? Dispatch { get; set; }

    /// <summary>
    /// Handles the Android system Back button / gesture: returns true when a couch overlay was closed
    /// (Back is consumed), false at the root library so the Activity lets the platform exit. Distinct from
    /// <see cref="Dispatch"/> because the library-level Cancel swallows B, which would otherwise trap Back.
    /// <see cref="SingleViewShell"/> points this at <c>MainViewModel.DispatchBackButton</c>.
    /// </summary>
    public static Func<bool>? DispatchBack { get; set; }

    /// <summary>The logical couch action for an Android keycode, or null if it is not a couch button.</summary>
    public static GamepadAction? Map(Keycode keycode) => keycode switch
    {
        Keycode.DpadUp => GamepadAction.NavigateUp,
        Keycode.DpadDown => GamepadAction.NavigateDown,
        Keycode.DpadLeft => GamepadAction.NavigateLeft,
        Keycode.DpadRight => GamepadAction.NavigateRight,
        // A / D-pad-centre / Enter all confirm; B / Escape cancel. Android BACK is handled separately in
        // the Activity (back-vs-B arbitration via DispatchBack), so it is deliberately absent here.
        Keycode.ButtonA or Keycode.DpadCenter or Keycode.Enter or Keycode.NumpadEnter => GamepadAction.Confirm,
        Keycode.ButtonB or Keycode.Escape => GamepadAction.Cancel,
        Keycode.ButtonX => GamepadAction.Search,
        Keycode.ButtonY => GamepadAction.Actions,
        Keycode.ButtonStart or Keycode.Menu => GamepadAction.Menu,
        // LB / RB switch platform, matching the couch rail's shoulder-button affordances.
        Keycode.ButtonL1 => GamepadAction.PreviousPlatform,
        Keycode.ButtonR1 => GamepadAction.NextPlatform,
        // R3 (right-stick click) recentres the 3D hero, matching the desktop native-pad mapping. The stick
        // click is a digital button, so it arrives here even though the stick motion goes through the reader.
        Keycode.ButtonThumbr => GamepadAction.ResetRotation,
        _ => null,
    };

    /// <summary>
    /// Whether a held button should keep re-firing this action on key auto-repeat. Only the directional
    /// actions repeat (a held D-pad that arrives as key events — rather than the hat axis the reader polls
    /// — still needs to scroll); every discrete action is edge-triggered by the shared controller and must
    /// fire exactly once per physical press, so a repeat is dropped rather than treated as a second press.
    /// </summary>
    public static bool RepeatsWhileHeld(GamepadAction action) => action is
        GamepadAction.NavigateUp or GamepadAction.NavigateDown or
        GamepadAction.NavigateLeft or GamepadAction.NavigateRight;
}
