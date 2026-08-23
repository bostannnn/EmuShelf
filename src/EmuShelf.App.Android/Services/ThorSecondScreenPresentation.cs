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
    private bool _released;

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
        _avaloniaView = new global::Avalonia.Android.AvaloniaView(Context)
        {
            Content = new global::EmuShelf.App.Views.SecondScreenView { DataContext = Model },
        };
        SetContentView(_avaloniaView);
    }

    // Back on Screen-2 must behave like a launcher's Back, not a dialog's: close an open overlay
    // (all-apps drawer / achievements), otherwise swallow. Never call base.OnBackPressed — that cancels
    // the Presentation and reveals the stock app drawer underneath (the reported "press Back → app
    // drawer appears" bug). Mirrors NeoStation, whose Screen-2 Presentation never finishes on Back.
    public override void OnBackPressed()
    {
        if (Model.Overlay != SecondScreenOverlayKind.None)
            Model.CloseOverlayCommand.Execute(null);
        // else: swallow.
    }

    internal void ReleaseResources()
    {
        if (_released)
            return;
        _released = true;
        // Dispose the fan-art/logo bitmaps the model owns (they are loaded per focus, not shared), so
        // tearing the presentation down — e.g. the second screen being unplugged — does not leak them.
        Model.SetSpotlight(null, null);
        // Detach the embedded Avalonia top level before the Presentation window is torn down, so its
        // render loop and input handlers do not outlive the display.
        _avaloniaView.Content = null;
    }
}
