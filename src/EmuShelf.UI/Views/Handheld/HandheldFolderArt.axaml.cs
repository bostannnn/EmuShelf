using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace EmuShelf.App.Views.Handheld;

/// <summary>A Handheld folder's art: back plate, the covers inside, the hinged front flap.</summary>
public partial class HandheldFolderArt : UserControl
{
    public HandheldFolderArt() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
