using CommunityToolkit.Mvvm.ComponentModel;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// One Desktop list-view column (M40). The list stayed a virtualized <c>ListBox</c> (so marquee,
/// multi-select, the per-row context menu, inline edit, and the async cover hooks are untouched);
/// this model drives which columns show, their order, and their width. The header row and every
/// game row bind to the same ordered column collection, so a reorder/hide/resize is a data change,
/// not a control-internal one. See DECISIONS 2026-08-08.
/// </summary>
public partial class LibraryColumn : ObservableObject
{
    public LibraryColumn(
        LibraryColumnKey key,
        string displayName,
        string header,
        LibrarySortColumn? sortColumn,
        double defaultWidth,
        double minWidth,
        bool isFlex,
        bool canHide,
        bool visibleByDefault)
    {
        Key = key;
        DisplayName = displayName;
        Header = header;
        SortColumn = sortColumn;
        DefaultWidth = defaultWidth;
        MinWidth = minWidth;
        IsFlex = isFlex;
        CanHide = canHide;
        VisibleByDefault = visibleByDefault;
        IsVisible = visibleByDefault;
        Width = defaultWidth;
    }

    /// <summary>Stable identity, persisted by name and used to pick the cell template.</summary>
    public LibraryColumnKey Key { get; }

    /// <summary>Friendly name for the column picker checklist (e.g. "Last Played").</summary>
    public string DisplayName { get; }

    /// <summary>Upper-case label shown in the sort header ("" for the cover column).</summary>
    public string Header { get; }

    /// <summary>The sort this column maps to, or null when the column cannot be sorted (Cover).</summary>
    public LibrarySortColumn? SortColumn { get; }

    public bool IsSortable => SortColumn is not null;

    /// <summary>Smallest width a resize drag may leave this column, and the floor for the flex
    /// column when the viewport is narrow.</summary>
    public double MinWidth { get; }

    public double DefaultWidth { get; }

    /// <summary>The single column (Title) that absorbs the remaining row width; its
    /// <see cref="Width"/> is computed from the viewport, never resized directly.</summary>
    public bool IsFlex { get; }

    /// <summary>False for Title, so the picker can never hide the last identifying column.</summary>
    public bool CanHide { get; }

    public bool VisibleByDefault { get; }

    /// <summary>Whether the column is shown. Two-way bound by the picker checklist.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Resolved pixel width. Fixed columns keep their (optionally resized) width; the flex
    /// column's is computed by the view model from the viewport.</summary>
    [ObservableProperty]
    public partial double Width { get; set; }

    /// <summary>The active-sort arrow shown after the header label ("▲"/"▼"/""), pushed by the view
    /// model so the header never disagrees with the current sort.</summary>
    [ObservableProperty]
    public partial string SortGlyph { get; set; } = string.Empty;
}
