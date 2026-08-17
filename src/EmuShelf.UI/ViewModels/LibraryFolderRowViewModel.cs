using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Library;

namespace EmuShelf.App.ViewModels;

public partial class LibraryFolderRowViewModel : ViewModelBase
{
    private readonly Func<LibraryFolderRowViewModel, Task> _change;
    private readonly Func<LibraryFolderRowViewModel, Task> _forget;
    private readonly Func<string, bool> _directoryExists;
    public long Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    public partial string Path { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    public partial bool Exists { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    public partial bool IsAvailabilityKnown { get; set; }

    public bool IsMissing => IsAvailabilityKnown && !Exists;

    public string AvailabilityText => !IsAvailabilityKnown
        ? "Checking…"
        : Exists ? "Available" : "Missing";

    public LibraryFolderRowViewModel(
        LibraryFolder folder,
        Func<LibraryFolderRowViewModel, Task> change,
        Func<LibraryFolderRowViewModel, Task> forget,
        Func<string, bool>? directoryExists = null)
    {
        _change = change;
        _forget = forget;
        Id = folder.Id;
        Path = folder.Path;
        _directoryExists = directoryExists ?? Directory.Exists;
    }

    public async Task RefreshAvailabilityAsync()
    {
        bool exists;
        try
        {
            exists = await Task.Run(() => _directoryExists(Path));
        }
        catch
        {
            exists = false;
        }
        Exists = exists;
        IsAvailabilityKnown = true;
    }

    [RelayCommand]
    private Task ChangeAsync() => _change(this);

    [RelayCommand]
    private Task ForgetAsync() => _forget(this);
}
