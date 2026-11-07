using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmuShelf.App.Views;

/// <summary>Handheld mode's top screen. A pure view onto <c>HandheldShellViewModel</c>.</summary>
public partial class HandheldStageView : UserControl
{
    public HandheldStageView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
