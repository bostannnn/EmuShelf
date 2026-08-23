using Avalonia.Controls;

namespace EmuShelf.App.Views;

/// <summary>
/// The app-owned couch keyboard surface. Purely data-bound to <see cref="ViewModels.GamepadKeyboardViewModel"/>;
/// the same control is hosted both as a main-screen strip and, on the Thor, mirrored onto the second screen.
/// </summary>
public partial class GamepadKeyboardView : UserControl
{
    public GamepadKeyboardView() => InitializeComponent();
}
