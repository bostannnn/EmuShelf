using System;
using Android.AccessibilityServices;
using Android.App;
using Android.Content.PM;
using Android.Views.Accessibility;

namespace EmuShelf.App.Android;

/// <summary>
/// Static, same-process bridge from <see cref="SecondScreenReturnWatcher"/> to the second-screen
/// controller. The controller sets <see cref="ForegroundWindowChanged"/> while it is driving Screen-2; the
/// service invokes it on every window-state change with the new foreground window's package and class.
/// </summary>
public static class SecondScreenAccessibility
{
    /// <summary>(package, className) of the window that just came to the front, on any display. May be null.</summary>
    public static Action<string?, string?>? ForegroundWindowChanged { get; set; }

    /// <summary>
    /// True while the user has enabled the return-watcher and the system has bound it, so its
    /// window-state events are the authoritative "the dock app closed" signal. The controller uses this to
    /// suppress its coarser <c>TopResumedChanged</c> fallback: when the watcher is live, merely returning to
    /// the main screen must NOT re-show the companion over a dock app that is still open on Screen-2.
    /// </summary>
    public static bool IsConnected { get; set; }
}

/// <summary>
/// Re-shows EmuShelf's Screen-2 companion the instant a dock-launched app is dismissed — the same mechanism
/// NeoStation uses. The companion is a <c>Presentation</c> at window layer 31000; when the dock launches an
/// app (an ordinary 21000 window) onto Screen-2 the companion is hidden so the app can be seen, but nothing
/// else signals when that app is closed, so backing out of it would drop Screen-2 to the stock
/// secondary-display launcher. This service reports each foreground window change to
/// <see cref="Services.SecondScreenController"/>, which re-shows the companion when that launcher returns.
///
/// It reads no screen content (canRetrieveWindowContent is false in second_screen_accessibility.xml) — it
/// only needs the foreground window's package/class, which arrive on the event itself.
/// </summary>
// Exported = true: an AccessibilityService is bound by system_server (a different uid), and a non-exported
// component can't be bound across uids even by the system — it would never appear in Settings → Accessibility
// nor bind, silently disabling the re-show. BIND_ACCESSIBILITY_SERVICE keeps the binder restricted to the
// system. This is the standard accessibility-service declaration.
[Service(
    Name = "com.emushelf.app.SecondScreenReturnWatcher",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE",
    Exported = true)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/second_screen_accessibility")]
public sealed class SecondScreenReturnWatcher : AccessibilityService
{
    protected override void OnServiceConnected()
    {
        base.OnServiceConnected();
        SecondScreenAccessibility.IsConnected = true;
    }

    public override void OnAccessibilityEvent(AccessibilityEvent? e)
    {
        if (e is null || e.EventType != EventTypes.WindowStateChanged)
            return;

        SecondScreenAccessibility.ForegroundWindowChanged?.Invoke(
            e.PackageName?.ToString(),
            e.ClassName?.ToString());
    }

    public override void OnInterrupt()
    {
        // Nothing to reset — the service holds no state.
    }

    public override bool OnUnbind(global::Android.Content.Intent? intent)
    {
        // The user disabled the service (or the system unbound it): the coarse TopResumed fallback becomes
        // the only re-show path again.
        SecondScreenAccessibility.IsConnected = false;
        return base.OnUnbind(intent);
    }
}
