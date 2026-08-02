using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// One selectable appearance, projected from <see cref="AppTheme"/> for both the Desktop settings
/// gallery and the controller theme gallery. The catalog's hex swatches are parsed into brushes once so
/// the gallery can render a live preview without a value converter.
/// </summary>
public partial class ThemeChoiceViewModel : ObservableObject
{
    private readonly Func<ThemePreference, Task>? _apply;

    public ThemeChoiceViewModel(AppTheme theme, Func<ThemePreference, Task>? apply = null)
    {
        _apply = apply;
        Id = theme.Id;
        Name = theme.Name;
        Description = theme.Description;
        IsDark = theme.IsDark;
        PreviewBackground = Brush(theme.PreviewBackground);
        PreviewSurface = Brush(theme.PreviewSurface);
        PreviewAccent = Brush(theme.PreviewAccent);
        PreviewText = Brush(theme.PreviewText);
    }

    public ThemePreference Id { get; }
    public string Name { get; }
    public string Description { get; }
    public bool IsDark { get; }
    public IBrush PreviewBackground { get; }
    public IBrush PreviewSurface { get; }
    public IBrush PreviewAccent { get; }
    public IBrush PreviewText { get; }

    /// <summary>True when this is the applied theme; drives the selected marker in both surfaces.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Controller focus within the gallery grid; independent of <see cref="IsSelected"/>.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    /// <summary>Applies this theme. Used by the Desktop settings gallery; the controller gallery routes
    /// through the settings view model so it can also move gallery focus.</summary>
    [RelayCommand]
    private Task SelectAsync() => _apply?.Invoke(Id) ?? Task.CompletedTask;

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
