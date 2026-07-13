using CommunityToolkit.Mvvm.Input;

namespace EmuShelf.App.ViewModels;

public partial class RemoveGameViewModel : ViewModelBase
{
    public string GameTitle { get; }
    public string Message =>
        $"Remove {GameTitle} from EmuShelf? The game file and its cover will not be deleted.";

    public event Action<bool>? CloseRequested;

    public RemoveGameViewModel(string gameTitle)
    {
        GameTitle = gameTitle;
    }

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
