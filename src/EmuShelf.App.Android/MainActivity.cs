using Android.App;
using Android.Content.PM;
using Avalonia.Android;

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
}
