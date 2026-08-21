using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.Views;
using Avalonia.Android;
using EmuShelf.App.Android.Services;

namespace EmuShelf.App.Android;

/// <summary>
/// The launcher Activity. In Avalonia 12 the app-builder customization lives on the
/// <see cref="EmuShelfAndroidApplication"/> (an <c>AvaloniaAndroidApplication&lt;TApp&gt;</c>), so this
/// is a thin <see cref="AvaloniaMainActivity"/>. An explicit <c>Name</c> pins the activity so .NET's
/// <c>crc64…</c> name mangling cannot rename the launcher out from under the manifest (Development-setup
/// trap 3 in the plan) — and any activity EmuShelf later exposes to emulator intents needs the same.
/// </summary>
[Activity(
    Name = "com.emushelf.app.MainActivity",
    Label = "EmuShelf",
    // AvaloniaActivity derives from AppCompatActivity, so the theme MUST be a Theme.AppCompat
    // descendant (a plain @android Material theme aborts with "You need to use a Theme.AppCompat
    // theme"). AndroidX AppCompat ships with Avalonia.Android, so this built-in resolves without a
    // custom styles.xml.
    Theme = "@style/Theme.AppCompat.NoActionBar",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity
{
    // The couch shell is laid out in device-independent pixels tuned for the Steam Deck's 1280×800.
    // A handheld like the AYN Thor packs 1920×1080 physical pixels behind a ~2.31× display density, so
    // Avalonia only sees ~833×468 **dip** — and the Deck-sized shell is then far too big for the panel.
    // We re-derive the effective density so the shell gets roughly this many dip across, i.e. a
    // Deck-class canvas, and everything scales down to fit. Width, because the couch layout is landscape.
    private const double CouchTargetDipWidth = 1280.0;

    /// <summary>
    /// Overrides the activity's resource density before Avalonia reads it, so a dense handheld panel
    /// presents the couch shell at a comfortable Deck-class dip size instead of an oversized ~833 dip.
    /// Guarded to only ever *lower* density (never enlarge the UI on an already low-dpi display), and to
    /// no-op on panels that are already ≤ the target width.
    /// </summary>
    protected override void AttachBaseContext(Context? @base)
    {
        base.AttachBaseContext(WithCouchDensity(@base));
    }

    private static Context? WithCouchDensity(Context? context)
    {
        if (context?.Resources is not { Configuration: { } configuration, DisplayMetrics: { } metrics })
            return context;

        // Landscape couch UI: the long edge is the width regardless of the panel's reported orientation.
        var widthPx = Math.Max(metrics.WidthPixels, metrics.HeightPixels);
        if (widthPx <= 0)
            return context;

        var targetDensity = widthPx / CouchTargetDipWidth;
        // Only shrink the UI: if the panel is already at/under the target dip width, leave it alone.
        if (targetDensity >= metrics.Density)
            return context;

        // DisplayMetrics.DENSITY_DEFAULT: the dpi at which 1 dip == 1 px (a 1.0× density baseline).
        const double DensityBaselineDpi = 160.0;
        var densityDpi = (int)Math.Round(targetDensity * DensityBaselineDpi);
        var overridden = new Configuration(configuration) { DensityDpi = densityDpi };
        return context.CreateConfigurationContext(overridden);
    }

    /// <summary>
    /// The head's couch input surface. Gamepad buttons and the D-pad arrive here as Android key events
    /// even though Avalonia reports them as <c>Key.None</c>, so this is where they are mapped to logical
    /// couch actions and routed to the shared view model. Only key-down is dispatched (repeats included,
    /// so held D-pad still scrolls); unmapped keys and key-up fall through to Avalonia and the system, so
    /// text fields, the Back gesture, and volume keys behave normally.
    /// </summary>
    public override bool DispatchKeyEvent(KeyEvent e)
    {
        if (e.Action == KeyEventActions.Down &&
            AndroidGamepadInput.Map(e.KeyCode) is { } action &&
            AndroidGamepadInput.Dispatch?.Invoke(action) == true)
        {
            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    /// <summary>
    /// The head's analog-stick surface. Joystick stick movement arrives as generic <see cref="MotionEvent"/>s
    /// (not key events, and Avalonia does not consume them), so this feeds each one into the reader the shared
    /// controller poll loop samples — driving left-stick navigation and right-stick 3D-hero rotation through
    /// the same logic desktop uses. Only joystick-source move events are taken; mouse, touchpad and hover
    /// events fall through to Avalonia and the system unchanged.
    /// </summary>
    public override bool DispatchGenericMotionEvent(MotionEvent? e)
    {
        if (e is { } motion &&
            motion.ActionMasked == MotionEventActions.Move &&
            motion.Source.HasFlag(InputSourceType.Joystick) &&
            AndroidGamepadReader.Current is { } reader)
        {
            reader.Update(motion);
            return true;
        }

        return base.DispatchGenericMotionEvent(e);
    }

    /// <summary>
    /// The head's return signal. Fires when EmuShelf gains (or loses) the single top-resumed activity
    /// slot; on gaining it — i.e. the user came back from a launched emulator — the shell completes the
    /// pending play session (play-time accrual, save sync). Preferred over <c>OnResume</c> because since
    /// Android 10 multiple activities can be resumed at once (the Thor is multi-display), so this is the
    /// accurate "EmuShelf is now in front" edge. Available on API 29+; the Thor is 33.
    /// </summary>
    public override void OnTopResumedActivityChanged(bool isTopResumedActivity)
    {
        base.OnTopResumedActivityChanged(isTopResumedActivity);
        if (isTopResumedActivity)
            AndroidActivityLifecycle.ReturnedToForeground?.Invoke();
    }
}
