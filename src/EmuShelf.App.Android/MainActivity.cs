using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using EmuShelf.App.Android.Services;
using EmuShelf.App.Diagnostics;

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
    // The couch shell is landscape-only, and the Thor's panel is natively portrait (1080x1920) —
    // without an explicit lock, going edge-to-edge fills that portrait panel instead of the rotated
    // landscape the handheld is used in. SensorLandscape pins landscape while still allowing a 180°
    // flip for a clamshell held either way.
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    // Keyboard/KeyboardHidden/Navigation are handled in-process too: attaching or removing a game
    // controller registers a navigation/keyboard input device, which raises CONFIG_NAVIGATION/
    // CONFIG_KEYBOARD. Without these flags Android would destroy and recreate the Activity — tearing
    // down and rebuilding the EGL surface and the whole Avalonia tree mid-session (a visible flash) —
    // every time a pad connects or disconnects.
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.Density
        | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden
        | ConfigChanges.Navigation)]
public class MainActivity : AvaloniaMainActivity
{
    // The couch shell is laid out in device-independent pixels tuned for the Steam Deck's 1280×800.
    // A handheld like the AYN Thor packs 1920×1080 physical pixels behind a ~2.31× display density, so
    // Avalonia only sees ~833×468 **dip** — and the Deck-sized shell is then far too big for the panel.
    // We re-derive the effective density so the shell gets roughly this many dip across, i.e. a
    // Deck-class canvas, and everything scales down to fit. Width, because the couch layout is landscape.
    private const double CouchTargetDipWidth = 1280.0;

    /// <summary>
    /// The live foreground activity, for the head's services that need an <c>Activity</c>/<c>Context</c> and
    /// the focused native view — currently <see cref="Services.AndroidOnScreenKeyboardService"/>, which raises
    /// the IME. Set while resumed and cleared on destroy; null between runs. Single-Activity app, so there is
    /// only ever one.
    /// </summary>
    internal static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        ApplyImmersiveMode();
    }

    protected override void OnResume()
    {
        base.OnResume();
        Current = this;
        AndroidActivityLifecycle.NotifyActivityAvailable(this);
    }

    /// <summary>
    /// Re-hides the system bars whenever the activity regains focus. Immersive mode is cleared by the
    /// system after a dialog, a bar swipe, or the IME showing, so a one-shot in <see cref="OnCreate"/>
    /// is not enough — the couch shell is a full-screen gamepad UI and the status bar and gesture-nav
    /// pill otherwise sit in reserved bands that eat a strip of the panel and never return the space.
    /// </summary>
    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
            ApplyImmersiveMode();
    }

    /// <summary>
    /// Draws edge-to-edge and hides the status and navigation bars, leaving the transient-swipe
    /// behaviour so the user can still reveal them. API 30+ only, which every supported device is
    /// (the Thor is 33).
    /// </summary>
    private void ApplyImmersiveMode()
    {
        // The WindowInsetsController API is API 30+. Every supported device clears it (the Thor is 33);
        // an older one simply keeps the system bars rather than crashing.
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
        AndroidActivityLifecycle.NotifyActivityDestroyed(this, IsFinishing);
        if (ReferenceEquals(Current, this))
            Current = null;
        base.OnDestroy();
    }

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

    // Triple-L3 diagnostics gesture: three left-stick clicks whose gaps each stay within this window count
    // as one activation, advancing the render-overlay cycle. KeyEvent.EventTime is a monotonic uptime clock,
    // so no wall-clock skew, and the fields need no locking — DispatchKeyEvent is the UI thread.
    private const long TripleClickWindowMs = 700;
    private long _lastL3ClickMs;
    private int _l3ClickCount;

    /// <summary>
    /// The head's couch input surface. Gamepad buttons and the D-pad arrive here as Android key events
    /// even though Avalonia reports them as <c>Key.None</c>, so this is where they are mapped to logical
    /// couch actions and routed to the shared view model. Only key-down is dispatched, and only the first
    /// event of a discrete press: auto-repeat is dropped for discrete actions (see
    /// <see cref="AndroidGamepadInput.RepeatsWhileHeld"/>) and kept only for the directional ones so a held
    /// D-pad still scrolls. Unmapped keys and key-up fall through to Avalonia and the system, so text
    /// fields, the Back gesture, and volume keys behave normally.
    /// </summary>
    public override bool DispatchKeyEvent(KeyEvent e)
    {
        // Back arbitration: if a couch overlay/menu is open, Back closes it (like B); at the root library it
        // is NOT consumed here, so it falls through to the platform and exits the app. Handled on key-up —
        // Android's canonical Back edge — so it fires once, and the soft keyboard (when showing) still
        // dismisses on Back before the event reaches the activity at all.
        //
        // This only runs while Screen 1 is the top-focused display. When the user touches the companion,
        // Screen-2 becomes top-focused and its gamepad input is handled entirely by ThorSecondScreenPresentation
        // (Android delivers keys to the focused display's window, not here).
        if (e.KeyCode == Keycode.Back)
        {
            if (e.Action == KeyEventActions.Up && AndroidGamepadInput.DispatchBack?.Invoke() == true)
                return true;

            return base.DispatchKeyEvent(e);
        }

        // Diagnostics: L3 (left-stick click) is unmapped in the couch input map, so a *triple* L3 — three
        // clicks within TripleClickWindowMs — advances the renderer debug-overlay cycle (off -> fps+render
        // time -> +dirty rects -> all) and, via RenderOverlayDiagnostics.Cycle, gates the matching logcat
        // perf sampler on the same step. Requiring a triple gesture (not a single click) keeps a stray
        // stick press from ever switching the diagnostics on. Nothing is on by default; this is the only way
        // in, in Debug and Release alike, so the Debug vs Release/AOT difference can be read on the panel.
        // Every L3 click is consumed here so it does nothing else.
        if (e.Action == KeyEventActions.Down && e.KeyCode == Keycode.ButtonThumbl)
        {
            // Count discrete presses only (RepeatCount == 0); a held L3 auto-repeats and must not self-trigger.
            if (e.RepeatCount != 0)
                return true;

            _l3ClickCount = e.EventTime - _lastL3ClickMs <= TripleClickWindowMs ? _l3ClickCount + 1 : 1;
            _lastL3ClickMs = e.EventTime;
            if (_l3ClickCount >= 3)
            {
                _l3ClickCount = 0;
                var label = RenderOverlayDiagnostics.Cycle(ResolveTopLevel());
                // A one-line trace so the current mode is confirmable over adb logcat without watching the
                // panel. Same tag as the perf sampler so `logcat -s EmuShelfPerf` sees the whole diagnostic.
                global::Android.Util.Log.Info("EmuShelfPerf", $"Render overlays: {label ?? "(no top level)"}");
            }
            return true;
        }

        if (e.Action == KeyEventActions.Down &&
            AndroidGamepadInput.Map(e.KeyCode) is { } action)
        {
            // Held-button auto-repeat must not re-fire a discrete action: the shared controller edge-triggers
            // these, so each key-repeat would land as a fresh press — holding B would back out of several
            // overlays at once and holding A would repeat-type on the couch keyboard. Only the directional
            // actions keep their repeats (a held D-pad still scrolls); every other repeat is swallowed here so
            // it neither re-fires nor falls through to Avalonia/the system.
            if (e.RepeatCount == 0 || AndroidGamepadInput.RepeatsWhileHeld(action))
            {
                if (AndroidGamepadInput.Dispatch?.Invoke(action) == true)
                    return true;
            }
            else
            {
                return true;
            }
        }

        return base.DispatchKeyEvent(e);
    }

    // The head's live top level, reached through this Activity's content (the view the lifetime's
    // MainViewFactory produced). Not ISingleViewApplicationLifetime.MainView: Avalonia's Android lifetime
    // leaves that null on the factory path, which is why the overlay cycle used to report "(no top level)".
    // Null before the view is shown.
    private static TopLevel? ResolveTopLevel() =>
        Current?.Content is Control view ? TopLevel.GetTopLevel(view) : null;

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
        AndroidActivityLifecycle.NotifyTopResumedChanged(isTopResumedActivity);
        if (isTopResumedActivity)
            AndroidActivityLifecycle.ReturnedToForeground?.Invoke();
    }
}
