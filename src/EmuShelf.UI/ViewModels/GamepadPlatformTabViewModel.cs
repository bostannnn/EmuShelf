using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

/// <summary>Presentation-only state for the controller platform rail.</summary>
public partial class GamepadPlatformTabViewModel : ObservableObject
{
    public GameSystem System { get; }
    public string Name => System.Name;
    public string ShortName => System.ShortName;
    public IImage? Artwork { get; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public GamepadPlatformTabViewModel(GameSystem system)
    {
        System = system;
        Artwork = PlatformArtwork.ForSystem(system.Id);
    }
}
