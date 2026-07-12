using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<GameSystem> Systems { get; } = new(KnownSystems.All);

    [ObservableProperty]
    public partial GameSystem? SelectedSystem { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = true;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    public MainViewModel()
    {
        SelectedSystem = Systems.FirstOrDefault();
    }

    [RelayCommand]
    private void AddGames()
    {
        // Import flow arrives with the game-importing milestone.
    }

    [RelayCommand]
    private void OpenSettings()
    {
        // Settings UI arrives with the emulator-configuration milestone.
    }
}
