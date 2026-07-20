using CommunityToolkit.Mvvm.Input;

namespace EmuShelf.App.ViewModels;

public partial class RemoveGameViewModel : ViewModelBase
{
    public string GameTitle { get; }
    public string Message { get; }

    public event Action<bool>? CloseRequested;

    public RemoveGameViewModel(string gameTitle)
    {
        GameTitle = gameTitle;
        Message = $"Remove {GameTitle} from EmuShelf? The game file and its cover will not be deleted.";
    }

    public RemoveGameViewModel(int gameCount)
    {
        GameTitle = $"{gameCount} {(gameCount == 1 ? "game" : "games")}";
        Message = $"Remove {GameTitle} from EmuShelf? The game files and covers will not be deleted.";
    }

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
