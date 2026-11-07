using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmuShelf.App.Views;

/// <summary>Handheld mode's bottom screen. A pure view onto <c>HandheldShellViewModel</c>.</summary>
public partial class HandheldLauncherView : UserControl
{
    public HandheldLauncherView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
