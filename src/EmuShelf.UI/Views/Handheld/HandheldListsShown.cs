using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// What the Big Type lists view shows (M47 B7): the shell's open list, except that a list that closed (or that another sheet replaced)
/// stays shown while its rows leave, as the mock's <c>closeSheet</c>, <c>closeSettings</c>, <c>closePanel</c> and <c>openSheet</c> keep
/// theirs. The Launcher's motion decides when it follows the shell (<see cref="HandheldLauncherMotion"/>); the view binds to this.
/// </summary>
public sealed partial class HandheldListsShown : ObservableObject
{
    [ObservableProperty]
    public partial bool IsMenuOpen { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<HandheldMenuItemViewModel>? MenuItems { get; set; }

    [ObservableProperty]
    public partial int MenuIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    public partial HandheldSheetViewModel? Sheet { get; set; }

    public bool IsSheetOpen => Sheet is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsOpen))]
    public partial HandheldSettingsViewModel? Settings { get; set; }

    public bool IsSettingsOpen => Settings is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelOpen))]
    public partial HandheldPanelViewModel? Panel { get; set; }

    public bool IsPanelOpen => Panel is not null;

    [ObservableProperty]
    public partial IBrush? LauncherGroundBrush { get; set; }

    /// <summary>Shows what the shell has open now.</summary>
    public void Follow(HandheldShellViewModel shell)
    {
        MenuItems = shell.MenuItems;
        MenuIndex = shell.MenuIndex;
        // Only one list at a time: whatever the shell puts first (Settings over a panel over the menu over a sheet).
        Settings = shell.Settings;
        Panel = Settings is null ? shell.Panel : null;
        IsMenuOpen = Settings is null && Panel is null && shell.IsMenuOpen;
        Sheet = Settings is null && Panel is null && !IsMenuOpen ? shell.Sheet : null;
        LauncherGroundBrush = shell.LauncherGroundBrush;
    }

    /// <summary>Which list is shown: its view model (the menu is the shell's list of rows); null for none.</summary>
    public object? Identity => (object?)Settings ?? (object?)Panel ?? (IsMenuOpen ? (object?)MenuItems : null) ?? Sheet;

    /// <summary>Which list the shell has open (as <see cref="Identity"/> would be after <see cref="Follow"/>).</summary>
    public static object? IdentityOf(HandheldShellViewModel shell) =>
        (object?)shell.Settings ?? (object?)shell.Panel ?? (shell.IsMenuOpen ? (object?)shell.MenuItems : null) ?? shell.Sheet;
}
