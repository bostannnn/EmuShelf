using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Library;

namespace EmuShelf.App.ViewModels;

/// <summary>A selectable disc entry shared by the Desktop context menu and its launch command.</summary>
public partial class GameDiscOptionViewModel : ObservableObject
{
    public GameDisc Disc { get; }
    /// <summary>A parameterless command for a concrete disc entry. Context-menu popups are
    /// detached from the library item's visual tree, so they must not rely on generic command
    /// parameter conversion to identify the selected disc.</summary>
    public IAsyncRelayCommand SelectDiscCommand { get; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    public string Label => IsCurrent ? $"Disc {Disc.Number} (current)" : $"Disc {Disc.Number}";

    public GameDiscOptionViewModel(GameDisc disc, Func<GameDisc, Task> selectDiscAsync, bool isCurrent)
    {
        Disc = disc;
        ArgumentNullException.ThrowIfNull(selectDiscAsync);
        SelectDiscCommand = new AsyncRelayCommand(() => selectDiscAsync(Disc));
        IsCurrent = isCurrent;
    }

    partial void OnIsCurrentChanged(bool value) => OnPropertyChanged(nameof(Label));
}
