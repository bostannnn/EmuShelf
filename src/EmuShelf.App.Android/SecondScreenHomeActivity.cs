using System;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Window;
using Avalonia.Android;
using AndroidX.AppCompat.App;
using EmuShelf.App.Android.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;

namespace EmuShelf.App.Android;

/// <summary>
/// The Thor companion surface. It hosts an embedded Avalonia view (<see cref="SecondScreenView"/>) bound to
/// the shared <see cref="SecondScreenController.Model"/>, so the companion inherits the app's palette, Inter
/// font, and controls.
///
/// It is an ordinary Activity — not the always-on-top <c>Presentation</c> the first cut used — which is what
/// makes the dock work: an app launched onto Screen-2 draws in front of it, and Back returns to it. It
/// declares the <c>CATEGORY_SECONDARY_HOME</c> filter so it is eligible to be Screen-2's home, but on the
/// Thor the stock launcher is the elected home, so it is <see cref="SecondScreenController"/> that explicitly
/// launches this onto the presentation display while EmuShelf is the active frontend. The Back handling here
/// makes it behave like that display's home regardless (Back never finishes it). See DECISIONS 2026-08-23.
/// </summary>
[Activity(
    Name = "com.emushelf.app.SecondScreenHomeActivity",
    Label = "EmuShelf companion",
    // AvaloniaView needs an AppCompat context, same as the launcher activity.
    Theme = "@style/Theme.AppCompat.NoActionBar",
    Exported = true,
    // The home is a single, reused surface for its display; never a recents entry.
    LaunchMode = LaunchMode.SingleTask,
    ExcludeFromRecents = true,
    StateNotNeeded = true,
    ResizeableActivity = true,
    // Screen-2 is a physically landscape panel whose natural framebuffer is portrait; pin landscape so the
    // companion is upright and so returning from a portrait-locked app (which flips the display) restores
    // landscape. Handle the config changes in-process so a rotation/pad event does not tear the view down.
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.Density
        | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden
        | ConfigChanges.Navigation)]
[IntentFilter(
    new[] { global::Android.Content.Intent.ActionMain },
    Categories = new[]
    {
        "android.intent.category.SECONDARY_HOME",
        global::Android.Content.Intent.CategoryDefault,
    })]
public class SecondScreenHomeActivity : AppCompatActivity
{
    /// <summary>The live companion home for this process, or null when Screen-2 has no companion up.</summary>
    internal static SecondScreenHomeActivity? Current { get; private set; }

    private AvaloniaView? _avaloniaView;
    private SecondScreenView? _view;
    private CompanionBackHandler? _backHandler;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            _view = new SecondScreenView();
            _avaloniaView = new AvaloniaView(this) { Content = _view };
            SetContentView(_avaloniaView);
        }
        catch (Exception ex)
        {
            // The companion is optional chrome. If Avalonia cannot host a second view (e.g. a cold home
            // start before the platform is ready), finish rather than crash-loop as the display's home.
            global::Android.Util.Log.Warn("EmuShelfSecondScreen", $"Companion home could not attach: {ex}");
            Finish();
            return;
        }

        Current = this;
        RegisterBackHandler();
        // Bind to the controller's shared model if the app has composed; otherwise show the placeholder
        // until AttachHome swaps in the real one.
        var controller = SecondScreenController.Active;
        BindModel(controller?.Model ?? new SecondScreenViewModel());
        controller?.AttachHome(this);
    }

    // The Thor targets a modern SDK, so predictive back is dispatched through OnBackInvokedCallback rather
    // than the legacy OnBackPressed — and Avalonia's own callback would finish the activity, dropping
    // Screen-2 to the system launcher. Register a companion callback at overlay priority so it is invoked
    // ahead of Avalonia's and consumes Back: close an open overlay, otherwise swallow it (a display home
    // never exits on Back). API 33+ (the Thor is 33); older devices use the OnBackPressed override below.
    private void RegisterBackHandler()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33) || OnBackInvokedDispatcher is not { } dispatcher)
            return;
        _backHandler = new CompanionBackHandler(this);
        dispatcher.RegisterOnBackInvokedCallback(IOnBackInvokedDispatcher.PriorityOverlay, _backHandler);
    }

    internal void HandleHomeBack()
    {
        if (_view?.DataContext is SecondScreenViewModel { Overlay: not SecondScreenOverlayKind.None } model)
            model.CloseOverlayCommand.Execute(null);
        // else: swallow. A display home does not finish itself on Back.
    }

    /// <summary>Points the embedded view at a companion view model. Called on the main thread.</summary>
    internal void BindModel(SecondScreenViewModel model)
    {
        if (_view is not null)
            _view.DataContext = model;
    }

    // Legacy Back path. This is the one actually invoked whenever predictive back is NOT enabled for the
    // app — which is the case on the Thor (Android 13, opt-in) and any build that does not set
    // enableOnBackInvokedCallback. RegisterBackHandler covers devices where predictive back IS dispatched
    // (33+ with the flag on); only one of the two fires per device. Same rule either way: close an open
    // overlay, otherwise swallow so the home never finishes itself on Back.
#pragma warning disable CA1422 // OnBackPressed is deprecated by predictive back; still the live path when it is off.
    public override void OnBackPressed() => HandleHomeBack();
#pragma warning restore CA1422

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            ApplyImmersiveMode();
    }

    // Draw edge-to-edge and hide the system bars on Screen-2, so the companion's own dock is not squeezed
    // by the gesture pill / status band. Mirrors the launcher activity; API 30+ (the Thor is 33).
    private void ApplyImmersiveMode()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || Window is not { } window)
            return;

        window.SetDecorFitsSystemWindows(false);
        if (window.InsetsController is { } controller)
        {
            controller.Hide(WindowInsets.Type.SystemBars());
            controller.SystemBarsBehavior =
                (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        if (_backHandler is not null && OperatingSystem.IsAndroidVersionAtLeast(33) &&
            OnBackInvokedDispatcher is { } dispatcher)
        {
            dispatcher.UnregisterOnBackInvokedCallback(_backHandler);
        }
        _backHandler = null;
        SecondScreenController.Active?.DetachHome(this);
        if (_avaloniaView is not null)
            _avaloniaView.Content = null;
        base.OnDestroy();
    }
}

/// <summary>Consumes the predictive-back gesture on the companion home (API 33+).</summary>
internal sealed class CompanionBackHandler(SecondScreenHomeActivity activity)
    : Java.Lang.Object, IOnBackInvokedCallback
{
    public void OnBackInvoked() => activity.HandleHomeBack();
}
