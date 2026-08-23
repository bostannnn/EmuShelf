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
[Service(
    Name = "com.emushelf.app.SecondScreenReturnWatcher",
    Permission = "android.permission.BIND_ACCESSIBILITY_SERVICE",
    Exported = false)]
[IntentFilter(new[] { "android.accessibilityservice.AccessibilityService" })]
[MetaData("android.accessibilityservice", Resource = "@xml/second_screen_accessibility")]
public sealed class SecondScreenReturnWatcher : AccessibilityService
{
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
}
