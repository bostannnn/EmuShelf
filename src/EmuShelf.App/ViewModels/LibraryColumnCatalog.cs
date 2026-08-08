using System.Collections.Generic;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The Desktop list-view column set and their defaults (M40). One place defines every column, its
/// friendly picker name, its sort mapping, and its default width/visibility, so the view model, the
/// column picker, and persistence all agree. Widths match the pre-M40 fixed layout so the default
/// view renders identically. New columns are appended here as their cell templates land — a column
/// is only listed once the list view can render it.
/// </summary>
internal static class LibraryColumnCatalog
{
    public static IReadOnlyList<LibraryColumn> CreateDefault() =>
    [
        // Cover thumbnail — not sortable, always available, shown by default.
        new LibraryColumn(LibraryColumnKey.Cover, "Cover", header: "",
            sortColumn: null, defaultWidth: 84, minWidth: 84,
            isFlex: false, canHide: true, visibleByDefault: true),

        // Title is the flex column (absorbs the remaining row width) and can never be hidden, so the
        // table always keeps one identifying column.
        new LibraryColumn(LibraryColumnKey.Title, "Title", header: "TITLE",
            sortColumn: LibrarySortColumn.Title, defaultWidth: 320, minWidth: 200,
            isFlex: true, canHide: false, visibleByDefault: true),

        new LibraryColumn(LibraryColumnKey.Console, "Console", header: "CONSOLE",
            sortColumn: LibrarySortColumn.Console, defaultWidth: 150, minWidth: 90,
            isFlex: false, canHide: true, visibleByDefault: true),

        new LibraryColumn(LibraryColumnKey.Format, "Format", header: "FORMAT",
            sortColumn: LibrarySortColumn.Format, defaultWidth: 90, minWidth: 60,
            isFlex: false, canHide: true, visibleByDefault: true),

        new LibraryColumn(LibraryColumnKey.Achievements, "Achievements", header: "ACHIEVEMENTS",
            sortColumn: LibrarySortColumn.Achievements, defaultWidth: 96, minWidth: 90,
            isFlex: false, canHide: true, visibleByDefault: true),

        new LibraryColumn(LibraryColumnKey.Textures, "Textures", header: "TEXTURES",
            sortColumn: LibrarySortColumn.Textures, defaultWidth: 92, minWidth: 80,
            isFlex: false, canHide: true, visibleByDefault: true),

        new LibraryColumn(LibraryColumnKey.Status, "Status", header: "STATUS",
            sortColumn: LibrarySortColumn.Status, defaultWidth: 100, minWidth: 80,
            isFlex: false, canHide: true, visibleByDefault: true),

        // Off by default — opt-in from the column picker. Sourced straight from the Game record
        // (M38 LastPlayedAt, DateAdded), so no metadata read is needed.
        new LibraryColumn(LibraryColumnKey.LastPlayed, "Last Played", header: "LAST PLAYED",
            sortColumn: LibrarySortColumn.LastPlayed, defaultWidth: 130, minWidth: 100,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.DateAdded, "Date Added", header: "DATE ADDED",
            sortColumn: LibrarySortColumn.DateAdded, defaultWidth: 130, minWidth: 100,
            isFlex: false, canHide: true, visibleByDefault: false),
    ];
}
