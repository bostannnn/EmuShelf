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
    LaunchScreen,
    ImportSystem,
    RemoveConfirmation,
    CoverDesktopHandoff,
    CoverSearch,
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
    /// <summary>Optional couch-distance glyph shown before the label; overlays whose options are
    /// verbs (the system menu, the game-actions sheet) set it, list-like overlays (discs, screens)
    /// leave it null and render text-only rows exactly as before.</summary>
    public Avalonia.Media.Geometry? Icon { get; }
    public bool HasIcon => Icon is not null;
    /// <summary>The dismiss half of a confirmation dialog. Drives the red (B-button) focus ring, versus
    /// the green (A-button) ring on the confirm half — mirroring the controller prompts.</summary>
    public bool IsCancel { get; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public GamepadOverlayOptionViewModel(
        string label, ICommand command, bool isDestructive = false, bool isCancel = false,
        Avalonia.Media.Geometry? icon = null)
    {
        Label = label;
        Command = command;
        IsDestructive = isDestructive;
        IsCancel = isCancel;
        Icon = icon;
    }
}
