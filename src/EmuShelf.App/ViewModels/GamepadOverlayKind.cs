using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    RemoveConfirmation,
    CoverDesktopHandoff,
}

/// <summary>A large, controller-selectable action in a Gamepad overlay.</summary>
public partial class GamepadOverlayOptionViewModel : ObservableObject
{
    public string Label { get; }
    public IRelayCommand Command { get; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public GamepadOverlayOptionViewModel(string label, IRelayCommand command)
    {
        Label = label;
        Command = command;
    }
}
