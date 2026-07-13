using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

public partial class MetadataConsentViewModel : ViewModelBase
{
    public string Message { get; }
    public event Action<MetadataConsentChoice>? CloseRequested;

    public MetadataConsentViewModel(int gameCount)
    {
        Message = gameCount == 1
            ? "1 game was added. Fetch its title and cover art from third-party sources?"
            : $"{gameCount} games were added. Fetch their titles and cover art from third-party sources?";
    }

    [RelayCommand]
    private void FetchOnce() => CloseRequested?.Invoke(MetadataConsentChoice.FetchOnce);

    [RelayCommand]
    private void Always() => CloseRequested?.Invoke(MetadataConsentChoice.Always);

    [RelayCommand]
    private void NotNow() => CloseRequested?.Invoke(MetadataConsentChoice.NotNow);
}
