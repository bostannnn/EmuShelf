using Avalonia.Data.Converters;

namespace EmuShelf.App.ViewModels;

/// <summary>View-only text shaping helpers for bindings.</summary>
public static class TextConverters
{
    /// <summary>Uppercases a bound string — Avalonia has no text-transform, so letterspaced
    /// small-caps labels (the couch dock's system eyebrow) shape their text here.</summary>
    public static readonly IValueConverter Uppercase =
        new FuncValueConverter<string?, string?>(static s => s?.ToUpperInvariant());
}
