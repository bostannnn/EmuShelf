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
/// reading over <c>MotionEvent</c> and an IME are the rest of Milestone C.
/// </summary>
public static class AndroidGamepadInput
{
    /// <summary>Routes a mapped controller action to the view model; returns true if it was handled.</summary>
    public static Func<GamepadAction, bool>? Dispatch { get; set; }

    /// <summary>The logical couch action for an Android keycode, or null if it is not a couch button.</summary>
    public static GamepadAction? Map(Keycode keycode) => keycode switch
    {
        Keycode.DpadUp => GamepadAction.NavigateUp,
        Keycode.DpadDown => GamepadAction.NavigateDown,
        Keycode.DpadLeft => GamepadAction.NavigateLeft,
        Keycode.DpadRight => GamepadAction.NavigateRight,
        // A / D-pad-centre / Enter all confirm; B / Escape cancel. Android BACK is deliberately left to
        // the system (back-gesture vs B-button arbitration is Milestone C), so it still exits normally.
        Keycode.ButtonA or Keycode.DpadCenter or Keycode.Enter or Keycode.NumpadEnter => GamepadAction.Confirm,
        Keycode.ButtonB or Keycode.Escape => GamepadAction.Cancel,
        Keycode.ButtonX => GamepadAction.Search,
        Keycode.ButtonY => GamepadAction.Actions,
        Keycode.ButtonStart or Keycode.Menu => GamepadAction.Menu,
        // LB / RB switch platform, matching the couch rail's shoulder-button affordances.
        Keycode.ButtonL1 => GamepadAction.PreviousPlatform,
        Keycode.ButtonR1 => GamepadAction.NextPlatform,
        _ => null,
    };
}
