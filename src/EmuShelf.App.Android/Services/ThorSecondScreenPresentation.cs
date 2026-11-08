using System.Collections.Generic;
using Android.App;
using Android.Content;
using Android.Views;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Hosts the Thor companion surface as an embedded Avalonia top level on the Presentation display, so it
/// renders with the app's theme, Inter font, and controls. All state and behaviour live on
/// <see cref="Model"/> (pushed in and wired by <see cref="SecondScreenController"/>); this type is just
/// the Android window that carries the Avalonia view onto Screen-2.
/// </summary>
internal sealed class ThorSecondScreenPresentation : Presentation
{
    private readonly global::Avalonia.Android.AvaloniaView _avaloniaView;
    private readonly global::EmuShelf.App.Views.SecondScreenView _companionView;
    private bool _released;

    /// <summary>
    /// True while Screen-2 shows Handheld mode's Launcher rather than the companion. The pad then belongs to
    /// the shared shell (the one view model both screens drive), so keys and motion are forwarded to it.
    /// </summary>
    public bool IsShowingLauncher { get; private set; }

    public SecondScreenViewModel Model { get; } = new();

    public ThorSecondScreenPresentation(Context outerContext, Display display)
        : base(outerContext, display)
    {
        // No system dim, and keep Screen-2 awake while the companion is up.
        Window?.SetDimAmount(0);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.TurnScreenOn);

        // The companion is Screen-2's home: it must never be dismissable, the way NeoStation's own
        // Presentation is sticky. A Presentation is a Dialog, so an outside tap or Back would otherwise
        // cancel it and drop Screen-2 to the stock secondary-display launcher (the "app drawer"). Block
        // both here; Back is additionally routed through OnBackPressed below so it can still close an
        // open overlay instead of doing nothing.
        SetCancelable(false);
        SetCanceledOnTouchOutside(false);

        // A second Avalonia top level, hosted on the Presentation's display context so it renders on
        // Screen-2. It shares Application.Current's styles/resources, so the app palette and Inter font
        // apply without any per-view theming. Proven to render on the Thor before this replaced the
        // hand-rolled native surface.
        _companionView = new global::EmuShelf.App.Views.SecondScreenView { DataContext = Model };
        _avaloniaView = new global::Avalonia.Android.AvaloniaView(Context)
        {
            Content = _companionView,
        };
        SetContentView(_avaloniaView);
    }

    // The companion's gamepad surface. Android routes hardware key events to the focused window of the
    // *top-focused display* (dumpsys: mTopFocusedDisplayId), and touching Screen-2 makes this Presentation
    // that window — so the D-pad arrives here, NOT at MainActivity (Screen 1). A Presentation is a Dialog,
    // whose DispatchKeyEvent is called at the top of the decor view before the event descends into the
    // embedded AvaloniaView, so mapping it here intercepts before Avalonia's default focus navigation would
    // otherwise wander the ring onto the Refresh/Close chrome and the invisible, still-focusable closed
    // overlays. Mirrors MainActivity.DispatchKeyEvent, which is the same pattern for the couch on Screen 1.
    /// <summary>
    /// Shows Handheld mode's pair for this display (a Stage and a Launcher, one of them in front, M45 H5) with their
    /// native surfaces, or the companion when <paramref name="handheld"/> is null. The pair stays here; a swap only
    /// changes which of the two is in front (<see cref="HandheldScreenHost"/>).
    /// </summary>
    internal void ShowHandheld(global::Avalonia.Controls.Control? handheld, IReadOnlyList<NativeSceneSurface> surfaces)
    {
        if (_released)
            return;
        if (handheld is null)
        {
            IsShowingLauncher = false;
            if (!ReferenceEquals(_avaloniaView.Content, _companionView))
                _avaloniaView.Content = _companionView;
            return;
        }
        // The views draw natively over the Avalonia view (M45 H4, option 5); they lend their surfaces pictures. Whether
        // a surface shows is the host's say (SetAllowed, Park).
        foreach (var surface in surfaces)
        {
            surface.AttachTo(this, Context, native => AddContentView(native,
                new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent)));
        }
        IsShowingLauncher = true;
        if (!ReferenceEquals(_avaloniaView.Content, handheld))
            _avaloniaView.Content = handheld;
    }

    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        // While the main screen has Settings or the setup wizard open, the pad belongs to it even though
        // this window is the focused one (the user touched Screen-2, or a settings page opened here).
        // Forward instead of handling: buttons on key-down, Back on key-up as the shell's Back edge.
        if (e is not null && (IsShowingLauncher || AndroidGamepadInput.MainScreenClaimsPad?.Invoke() == true))
        {
            if (e.KeyCode == Keycode.Back)
            {
                if (e.Action == KeyEventActions.Up)
                    AndroidGamepadInput.DispatchBack?.Invoke();
                return true;
            }
            if (AndroidGamepadInput.Map(e.KeyCode) is { } forwarded)
            {
                if (e.Action == KeyEventActions.Down && (e.RepeatCount == 0 || AndroidGamepadInput.RepeatsWhileHeld(forwarded)))
                    AndroidGamepadInput.Dispatch?.Invoke(forwarded);
                return true;
            }
        }

        // Every controller key is consumed here (both edges) so Avalonia never runs focus nav on Screen-2;
        // the action is dispatched on key-down only (repeats included, so a held D-pad keeps walking the
        // grid). Back is deliberately NOT in the map, so it falls through to OnBackPressed below (close an
        // open overlay, else swallow) — matching B, which maps to Cancel and closes the overlay too.
        if (e is not null && AndroidGamepadInput.Map(e.KeyCode) is { } action)
        {
            if (e.Action == KeyEventActions.Down)
                Model.DispatchGamepadAction(action);
            return true;
        }

        // The binding types the base parameter non-null, but Android may pass null; forward as-is.
        return base.DispatchKeyEvent(e!);
    }

    // The motion half of the same hand-off. Sticks and hat-axis D-pads arrive as generic motion, which
    // Android also delivers to the focused display's window — so while the main screen claims the pad,
    // this window has to feed the shared reader or Settings/the wizard keeps its buttons and loses every
    // direction. Otherwise motion belongs to nobody here: the companion is driven by key events.
    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if ((IsShowingLauncher || AndroidGamepadInput.MainScreenClaimsPad?.Invoke() == true) && AndroidGamepadInput.TryFeedMotion(e))
            return true;

        // The binding types the base parameter non-null, but Android may pass null; forward as-is.
        return base.DispatchGenericMotionEvent(e!);
    }

    // Back on Screen-2 must behave like a launcher's Back, not a dialog's: close an open overlay
    // (all-apps drawer / achievements), otherwise swallow. Never call base.OnBackPressed — that cancels
    // the Presentation and reveals the stock app drawer underneath (the reported "press Back → app
    // drawer appears" bug). Mirrors NeoStation, whose Screen-2 Presentation never finishes on Back.
    public override void OnBackPressed()
    {
        // The Launcher is the shell's: Back closes what it has open (never the Presentation itself).
        if (IsShowingLauncher)
        {
            AndroidGamepadInput.DispatchBack?.Invoke();
            return;
        }
        if (Model.Overlay != SecondScreenOverlayKind.None)
            Model.CloseOverlayCommand.Execute(null);
        // else: swallow.
    }

    internal void ReleaseResources()
    {
        if (_released)
            return;
        _released = true;
        // Dispose the fan-art/logo bitmaps and the achievement badge bitmaps the model owns (all loaded
        // per focus, not shared), so tearing the presentation down — e.g. the second screen being
        // unplugged — does not leak them.
        Model.SetSpotlight(null, null);
        Model.ClearAchievements();
        Model.ClearMedia();
        // Detach the embedded Avalonia top level before the Presentation window is torn down, so its
        // render loop and input handlers do not outlive the display.
        _avaloniaView.Content = null;
    }
}
