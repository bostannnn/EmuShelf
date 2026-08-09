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
    Rename,
    DiscSelection,
    RemoveConfirmation,
    CoverDesktopHandoff,
    Scraper,
    BatchScraper,
    SystemMenu,
    Settings,
    Hotkeys,
    DesktopModeConfirmation,
    QuitConfirmation,
}

/// <summary>A large, controller-selectable action in a Gamepad overlay.</summary>
public partial class GamepadOverlayOptionViewModel : ObservableObject
{
    public string Label { get; }
    public ICommand Command { get; }
    public bool IsDestructive { get; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public GamepadOverlayOptionViewModel(string label, ICommand command, bool isDestructive = false)
    {
        Label = label;
        Command = command;
        IsDestructive = isDestructive;
    }
}
