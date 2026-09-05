using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Display;
using Android.Views;
using Android.Widget;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// What the companion display shows while the pre-boot setup page is up. The real Screen-2 companion
/// belongs to the composed shell, which does not exist yet, and a dual-screen device with one screen
/// simply black reads as broken. This is the smallest honest stand-in: the app name and where to look.
/// Shown by the Activity while the App-level onboarding hook is live, dismissed on pause.
/// </summary>
internal sealed class SetupPresentation : Presentation
{
    private SetupPresentation(Context context, Display display)
        : base(context, display)
    {
    }

    /// <summary>The presentation for the first companion display, or null when the device has none.</summary>
    public static SetupPresentation? CreateFor(Activity activity)
    {
        if (activity.GetSystemService(Context.DisplayService) is not DisplayManager manager)
            return null;
        var displays = manager.GetDisplays(DisplayManager.DisplayCategoryPresentation);
        if (displays is not { Length: > 0 })
            return null;
        return new SetupPresentation(activity, displays[0]);
    }

    // If the user touches this screen it becomes the focused display and the pad lands here; hand every
    // controller key to the setup page on the other screen (Back as B) so the page never goes dead.
    public override bool DispatchKeyEvent(KeyEvent? e)
    {
        if (e is null)
            return base.DispatchKeyEvent(e!);
        if (e.KeyCode == Keycode.Back)
        {
            if (e.Action == KeyEventActions.Up)
                AndroidGamepadInput.Dispatch?.Invoke(EmuShelf.App.Services.GamepadAction.Cancel);
            return true;
        }
        if (AndroidGamepadInput.Map(e.KeyCode) is { } action)
        {
            if (e.Action == KeyEventActions.Down && e.RepeatCount == 0)
                AndroidGamepadInput.Dispatch?.Invoke(action);
            return true;
        }
        return base.DispatchKeyEvent(e);
    }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var context = Context!;
        var layout = new LinearLayout(context) { Orientation = Orientation.Vertical };
        layout.SetGravity(GravityFlags.Center);
        layout.SetBackgroundColor(Color.ParseColor("#1E1E2A"));

        var title = new TextView(context) { Text = "EmuShelf", Gravity = GravityFlags.Center };
        title.SetTextColor(Color.ParseColor("#E8E8F0"));
        title.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 34);

        var hint = new TextView(context) { Text = "Setup continues on the other screen.", Gravity = GravityFlags.Center };
        hint.SetTextColor(Color.ParseColor("#9A9AB0"));
        hint.SetTextSize(global::Android.Util.ComplexUnitType.Sp, 18);
        hint.SetPadding(48, 24, 48, 0);

        layout.AddView(title);
        layout.AddView(hint);
        SetContentView(layout);
    }
}
