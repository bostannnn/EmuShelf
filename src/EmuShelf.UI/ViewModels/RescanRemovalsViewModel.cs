using CommunityToolkit.Mvvm.Input;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Confirmation shown at the end of a rescan when games were found missing on disk. Lists the exact
/// titles that will be dropped so the removal is never silent; declining keeps every row.
/// </summary>
public partial class RescanRemovalsViewModel : ViewModelBase
{
    public IReadOnlyList<string> Titles { get; }
    public string Message { get; }

    public event Action<bool>? CloseRequested;

    public RescanRemovalsViewModel(IReadOnlyList<string> titles)
    {
        Titles = titles;
        Message = titles.Count == 1
            ? "1 game is no longer on disk. Remove it from EmuShelf? The game file and its cover will not be deleted."
            : $"{titles.Count} games are no longer on disk. Remove them from EmuShelf? The game files and covers will not be deleted.";
    }

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
