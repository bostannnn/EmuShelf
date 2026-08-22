using Avalonia.Controls;

namespace EmuShelf.App.Views;

/// <summary>
/// The Thor companion surface, hosted as an embedded Avalonia top level on the second display so it
/// inherits the app's theme, fonts, and controls. First cut is static — it proves the embed and
/// theming; the dock, app drawer, achievements panel, and game-idle become data-bound against a
/// SecondScreenViewModel once the embed is confirmed on hardware.
/// </summary>
public partial class SecondScreenView : UserControl
{
    public SecondScreenView()
    {
        InitializeComponent();
    }
}
