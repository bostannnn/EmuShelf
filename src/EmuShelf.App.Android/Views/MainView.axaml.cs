using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmuShelf.App.Android.Views;

/// <summary>
/// The Android head's single-view root. A thin host for the shared <c>GamepadShellView</c>; all couch
/// UI, GL, and view-model wiring live in that shared view, so there is nothing platform-specific here.
/// </summary>
public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
