using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmuShelf.App.Android.Views;

/// <summary>
/// The Android head's single-view root. A thin host for the shared <c>GamepadShellView</c>; all couch
/// UI, GL, and view-model wiring live in that shared view. Couch input is handled at the Activity level
/// (see <c>MainActivity.DispatchKeyEvent</c>), because Android gamepad buttons arrive as Activity key
/// events that Avalonia's own <c>KeyDown</c> does not surface.
/// </summary>
public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
