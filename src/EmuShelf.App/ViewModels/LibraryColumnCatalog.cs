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

        // Achievements split into a softcore (silver) and hardcore (gold) column so the list shows the
        // same distinction as the couch widget and the achievements menu. The softcore column keeps the
        // `Achievements` key for persistence continuity.
        new LibraryColumn(LibraryColumnKey.Achievements, "Softcore", header: "SOFTCORE",
            sortColumn: LibrarySortColumn.Achievements, defaultWidth: 96, minWidth: 88,
            isFlex: false, canHide: true, visibleByDefault: true),

        new LibraryColumn(LibraryColumnKey.HardcoreAchievements, "Hardcore", header: "HARDCORE",
            sortColumn: LibrarySortColumn.HardcoreAchievements, defaultWidth: 96, minWidth: 88,
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

        // Scraped-metadata columns (M40 Phase 4), fed by the bulk GameDetailsProjection. Completeness
        // is on by default as the single at-a-glance signal; the per-asset breakdown and scalar facts
        // are opt-in from the picker so the default view stays lean.
        new LibraryColumn(LibraryColumnKey.Completeness, "Metadata", header: "METADATA",
            sortColumn: LibrarySortColumn.MetadataCompleteness, defaultWidth: 92, minWidth: 74,
            isFlex: false, canHide: true, visibleByDefault: true),

        new LibraryColumn(LibraryColumnKey.ArtworkCover, "Has Cover", header: "COVER",
            sortColumn: LibrarySortColumn.ArtworkCover, defaultWidth: 74, minWidth: 60,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Screenshot, "Screenshot", header: "SCREENSHOT",
            sortColumn: LibrarySortColumn.Screenshot, defaultWidth: 104, minWidth: 80,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Fanart, "Fan Art", header: "FAN ART",
            sortColumn: LibrarySortColumn.Fanart, defaultWidth: 84, minWidth: 64,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Logo, "Logo", header: "LOGO",
            sortColumn: LibrarySortColumn.Logo, defaultWidth: 72, minWidth: 60,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Description, "Description", header: "DESCRIPTION",
            sortColumn: LibrarySortColumn.Description, defaultWidth: 108, minWidth: 84,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.TitleScreen, "Title Screen", header: "TITLE SCREEN",
            sortColumn: LibrarySortColumn.TitleScreen, defaultWidth: 104, minWidth: 80,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.BoxBack, "Box Back", header: "BOX BACK",
            sortColumn: LibrarySortColumn.BoxBack, defaultWidth: 84, minWidth: 64,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.BoxSpine, "Box Spine", header: "BOX SPINE",
            sortColumn: LibrarySortColumn.BoxSpine, defaultWidth: 84, minWidth: 64,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.PhysicalMedia, "Cartridge / Disc", header: "CARTRIDGE / DISC",
            sortColumn: LibrarySortColumn.PhysicalMedia, defaultWidth: 120, minWidth: 90,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.PhysicalMediaTexture, "Cartridge / Disc Texture", header: "CARTRIDGE / DISC TEXTURE",
            sortColumn: LibrarySortColumn.PhysicalMediaTexture, defaultWidth: 160, minWidth: 110,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Rating, "Rating", header: "RATING",
            sortColumn: LibrarySortColumn.Rating, defaultWidth: 80, minWidth: 60,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Genre, "Genre", header: "GENRE",
            sortColumn: LibrarySortColumn.Genre, defaultWidth: 140, minWidth: 90,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Year, "Year", header: "YEAR",
            sortColumn: LibrarySortColumn.Year, defaultWidth: 72, minWidth: 56,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Players, "Players", header: "PLAYERS",
            sortColumn: LibrarySortColumn.Players, defaultWidth: 84, minWidth: 64,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Developer, "Developer", header: "DEVELOPER",
            sortColumn: LibrarySortColumn.Developer, defaultWidth: 160, minWidth: 100,
            isFlex: false, canHide: true, visibleByDefault: false),

        new LibraryColumn(LibraryColumnKey.Publisher, "Publisher", header: "PUBLISHER",
            sortColumn: LibrarySortColumn.Publisher, defaultWidth: 160, minWidth: 100,
            isFlex: false, canHide: true, visibleByDefault: false),
    ];
}
