using System;
using Android.Content;
using Android.Views.InputMethods;
using EmuShelf.Core.Input;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Raises Android's soft keyboard (IME) for couch text entry. The desktop head launches an on-screen
/// keyboard process; here the system IME is requested through <see cref="InputMethodManager"/> for the
/// currently focused view — which is Avalonia's own text-input target once a couch <c>TextBox</c> has
/// focus, so characters route back into the field normally. This explicit request matters because
/// gamepad-driven (directional) focus does not auto-raise the IME the way a screen tap does, so without
/// it gamepad search / rename cannot type. Hiding the keyboard again is left to Avalonia, which dismisses
/// the IME when the text client loses focus (the overlay closes).
/// </summary>
public sealed class AndroidOnScreenKeyboardService : IOnScreenKeyboardService
{
    private readonly Func<MainActivity?> _activity;

    public AndroidOnScreenKeyboardService(Func<MainActivity?> activity) => _activity = activity;

    public bool IsSupported => true;

    public bool TryShow(OnScreenKeyboardRequest request)
    {
        var activity = _activity();
        if (activity is null)
            return false;

        // The focused native view is Avalonia's input target when a couch TextBox holds focus; fall back to
        // the decor view so the IME still raises if focus resolution lags a frame behind this request.
        var view = activity.CurrentFocus ?? activity.Window?.DecorView;
        if (view is null)
            return false;

        if (activity.GetSystemService(Context.InputMethodService) is not InputMethodManager imm)
            return false;

        // Forced, not Implicit: a game controller registers as a hardware keyboard (which is why the Activity
        // declares ConfigChanges.Keyboard), and Android suppresses a SHOW_IMPLICIT request whenever a hardware
        // keyboard is present — so on the Thor an Implicit show silently no-ops and the field is left
        // un-typeable. SHOW_FORCED bypasses that heuristic; the show is always a deliberate couch summon here.
        return imm.ShowSoftInput(view, ShowFlags.Forced);
    }
}
