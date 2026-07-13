using Avalonia.Data.Converters;

namespace EmuShelf.App.ViewModels;

/// <summary>Maps a game's availability flag to the label shown in the list view.</summary>
public static class AvailabilityText
{
    public static readonly IValueConverter Instance =
        new FuncValueConverter<bool, string>(available => available ? "Available" : "File missing");
}
