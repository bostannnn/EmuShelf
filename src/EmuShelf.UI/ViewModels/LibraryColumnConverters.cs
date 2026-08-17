using Avalonia.Data.Converters;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.ViewModels;

/// <summary>View-only converters for the Desktop list-view scraped columns (M40).</summary>
public static class LibraryColumnConverters
{
    /// <summary>A per-asset presence bool → a check mark when present, otherwise the shared em dash
    /// the other columns use for "nothing here".</summary>
    public static readonly IValueConverter Presence =
        new FuncValueConverter<bool, string>(static present => present ? "✓" : RetroAchievementsDisplay.Dash);
}
