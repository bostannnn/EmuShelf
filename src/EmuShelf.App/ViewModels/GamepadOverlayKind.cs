using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace EmuShelf.App.ViewModels;

/// <summary>Every controller-owned surface hosted inside the Gamepad library window.</summary>
public enum GamepadOverlayKind
{
    None,
    Actions,
    Achievements,
    Search,
    Collections,
    Rename,
    DiscSelection,
    RemoveConfirmation,
    CoverDesktopHandoff,
    SystemMenu,
    DesktopModeConfirmation,
    SettingsDesktopHandoff,
    QuitConfirmation,
}

/// <summary>A large, controller-selectable action in a Gamepad overlay.</summary>
public partial class GamepadOverlayOptionViewModel : ObservableObject
{
    public string Label { get; }
    public ICommand Command { get; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public GamepadOverlayOptionViewModel(string label, ICommand command)
    {
        Label = label;
        Command = command;
    }
}
