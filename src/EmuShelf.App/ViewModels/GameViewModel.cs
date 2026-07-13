using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.Core.Library;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Presentation wrapper around a <see cref="Game"/>. Availability is observable so the
/// startup check can flip a game to "unavailable" in place. Cover art and title editing
/// arrive in M7; for now the grid shows a branded placeholder built from the title.
/// </summary>
public partial class GameViewModel : ObservableObject
{
    public long Id { get; }
    public string SystemId { get; }
    public string Path { get; }
    public string SystemName { get; }
    public string AccentColor { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    public GameViewModel(Game game, string systemName, string accentColor)
    {
        Id = game.Id;
        SystemId = game.SystemId;
        Path = game.Path;
        Title = game.Title;
        IsAvailable = game.IsAvailable;
        SystemName = systemName;
        AccentColor = accentColor;
    }

    /// <summary>Up-to-two-letter monogram for the placeholder cover.</summary>
    public string Initials
    {
        get
        {
            var trimmed = Title.Trim();
            if (trimmed.Length == 0)
                return "?";

            var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return string.Concat(char.ToUpperInvariant(parts[0][0]), char.ToUpperInvariant(parts[1][0]));
            return trimmed[..Math.Min(2, trimmed.Length)].ToUpperInvariant();
        }
    }
}
