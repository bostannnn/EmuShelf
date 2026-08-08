using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Picks a Desktop list-view cell template by its column's <see cref="LibraryColumn.Key"/> (M40).
/// Rows and the header bind to the same ordered visible-column collection; each realized cell is a
/// <see cref="LibraryColumn"/>, and this maps it to the matching keyed template so only the columns
/// actually shown build a visual. Templates stay authored in XAML — this only dispatches.
/// </summary>
public sealed class LibraryColumnCellSelector : IDataTemplate
{
    [Content]
    public List<LibraryColumnCellTemplate> Templates { get; } = [];

    public bool Match(object? data) => data is LibraryColumn;

    public Control? Build(object? data)
    {
        if (data is not LibraryColumn column)
            return null;

        var match = Templates.FirstOrDefault(template => template.Key == column.Key);
        return match?.Template?.Build(data);
    }
}

/// <summary>One keyed entry for <see cref="LibraryColumnCellSelector"/>: the column key plus the
/// template to build for it. The template is the element's content, so XAML reads naturally.</summary>
public sealed class LibraryColumnCellTemplate
{
    public LibraryColumnKey Key { get; set; }

    [Content]
    public IDataTemplate? Template { get; set; }
}
