namespace EmuShelf.Core.Handheld;

/// <summary>A folder's sort chip. Only <see cref="Manual"/> allows reordering (H4).</summary>
public enum HandheldFolderSort
{
    Manual,
    Title,
    LastPlayed,
    MostPlayed,
}

public static class HandheldFolderSorting
{
    public static string Label(HandheldFolderSort sort) => sort switch
    {
        HandheldFolderSort.Title => "A–Z",
        HandheldFolderSort.LastPlayed => "Last played",
        HandheldFolderSort.MostPlayed => "Most played",
        _ => "Manual",
    };

    /// <summary>The chip's next value when pressed: Manual → A–Z → Last played → Most played → Manual.</summary>
    public static HandheldFolderSort Next(HandheldFolderSort sort) =>
        (HandheldFolderSort)(((int)sort + 1) % 4);

    /// <summary>
    /// Orders a folder's games. Manual keeps the folder's own order (a platform folder's is the library's
    /// title order). Last played puts the most recent first and never-played games last; Most played
    /// orders by play count. Ties, and every never-played game, fall back to the title, so the order is
    /// stable from one open to the next.
    /// </summary>
    public static IReadOnlyList<T> Apply<T>(
        IEnumerable<T> items,
        HandheldFolderSort sort,
        Func<T, string> title,
        Func<T, DateTimeOffset?> lastPlayed,
        Func<T, int> playCount)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        return sort switch
        {
            HandheldFolderSort.Title => items.OrderBy(title, comparer).ToList(),
            HandheldFolderSort.LastPlayed => items
                .OrderBy(item => lastPlayed(item) is null)
                .ThenByDescending(item => lastPlayed(item))
                .ThenBy(title, comparer)
                .ToList(),
            HandheldFolderSort.MostPlayed => items
                .OrderByDescending(playCount)
                .ThenBy(title, comparer)
                .ToList(),
            _ => items.ToList(),
        };
    }
}
