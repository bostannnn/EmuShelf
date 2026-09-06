using Avalonia;
using Avalonia.Controls;

namespace EmuShelf.App.Views;

/// <summary>
/// The shared controller-sized settings row (see the markup). <see cref="IsControllerInputActive"/> is
/// forwarded to the inner button's <c>controller-input</c> class so the pointer hover ring stays hidden
/// while a controller drives, exactly as the inline template did.
/// </summary>
public partial class GamepadSettingsRowView : UserControl
{
    public static readonly StyledProperty<bool> IsControllerInputActiveProperty =
        AvaloniaProperty.Register<GamepadSettingsRowView, bool>(nameof(IsControllerInputActive));

    public bool IsControllerInputActive
    {
        get => GetValue(IsControllerInputActiveProperty);
        set => SetValue(IsControllerInputActiveProperty, value);
    }

    public GamepadSettingsRowView()
    {
        InitializeComponent();
    }
}
