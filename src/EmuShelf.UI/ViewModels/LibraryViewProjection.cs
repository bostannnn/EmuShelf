using EmuShelf.App.ViewModels.Layout;

namespace EmuShelf.App.ViewModels;

// Captured on the UI thread. Workers read only these immutable values, never live VM properties.
internal readonly record struct LibraryViewEntry(
    GameViewModel Game, string Title, string DisplayTitle, IComparable SortKey, double AspectRatio)
{
    public static LibraryViewEntry Capture(GameViewModel game, LibrarySortColumn column)
    {
        IComparable key = column switch
        {
            LibrarySortColumn.Console => game.SystemName,
            LibrarySortColumn.Format => game.FormatLabel,
            LibrarySortColumn.Achievements => game.AchievementSortKey,
            LibrarySortColumn.HardcoreAchievements => game.HardcoreSortKey,
            LibrarySortColumn.Textures => game.TextureSortKey,
            LibrarySortColumn.Status => game.AvailabilityText,
            LibrarySortColumn.LastPlayed => game.LastPlayedSortKey,
            LibrarySortColumn.Playtime => game.PlaytimeSortKey,
            LibrarySortColumn.PlayCount => game.PlayCountSortKey,
            LibrarySortColumn.DateAdded => game.DateAddedSortKey,
            LibrarySortColumn.MetadataCompleteness => game.MetadataCompletenessSortKey,
            LibrarySortColumn.ArtworkCover => game.HasScrapedCover,
            LibrarySortColumn.Screenshot => game.HasScrapedScreenshot,
            LibrarySortColumn.Fanart => game.HasScrapedFanart,
            LibrarySortColumn.Logo => game.HasScrapedLogo,
            LibrarySortColumn.Description => game.HasScrapedDescription,
            LibrarySortColumn.TitleScreen => game.HasScrapedTitleScreen,
            LibrarySortColumn.BoxBack => game.HasScrapedBoxBack,
            LibrarySortColumn.BoxSpine => game.HasScrapedBoxSpine,
            LibrarySortColumn.PhysicalMedia => game.HasScrapedPhysicalMedia,
            LibrarySortColumn.PhysicalMediaTexture => game.HasScrapedPhysicalMediaTexture,
            LibrarySortColumn.Rating => game.RatingSortKey,
            LibrarySortColumn.Genre => game.GenreColumnText,
            LibrarySortColumn.Year => game.YearSortKey,
            LibrarySortColumn.Players => game.PlayersColumnText,
            LibrarySortColumn.Developer => game.DeveloperColumnText,
            LibrarySortColumn.Publisher => game.PublisherColumnText,
            _ => game.DisplayTitle,
        };
        return new(game, game.Title, game.DisplayTitle, key, game.CoverAspectRatio);
    }
}

internal readonly record struct LibraryGridSpec(
    double Width, double Spacing, double Height, int MinimumPerRow, bool Gamepad);

internal sealed record LibraryViewProjection(
    LibraryViewEntry[] Entries, IReadOnlyList<CoverPlacement> Placements, IReadOnlyList<GameViewModel>[] Rows)
{
    private static readonly IComparer<IComparable> KeyComparer = Comparer<IComparable>.Create(
        (left, right) => left is string a && right is string b
            ? StringComparer.OrdinalIgnoreCase.Compare(a, b) : left.CompareTo(right));

    public static LibraryViewProjection Build(
        LibraryViewEntry[] source, string query, bool descending, bool preserveOrder,
        LibraryGridSpec grid, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IEnumerable<LibraryViewEntry> filtered = source;
        if (query.Length > 0)
            filtered = source.Where(entry =>
            {
                token.ThrowIfCancellationRequested();
                return entry.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            });
        if (!preserveOrder)
            filtered = (descending
                    ? filtered.OrderByDescending(entry => entry.SortKey, KeyComparer)
                    : filtered.OrderBy(entry => entry.SortKey, KeyComparer))
                .ThenBy(entry => entry.DisplayTitle, StringComparer.OrdinalIgnoreCase);
        var entries = preserveOrder && query.Length == 0 ? source : filtered.ToArray();
        token.ThrowIfCancellationRequested();
        // No real viewport yet: publish games but no degenerate row containing the whole library.
        if (grid.Width <= 0)
            return new(entries, [], []);
        var placements = JustifiedCoverLayout.Pack(entries.Select(e => e.AspectRatio).ToArray(),
            grid.Width, grid.Spacing, grid.Height, grid.MinimumPerRow);
        token.ThrowIfCancellationRequested();
        var rows = new List<IReadOnlyList<GameViewModel>>();
        List<GameViewModel>? row = null;
        var previousRow = -1;
        for (var index = 0; index < entries.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (placements[index].RowIndex != previousRow)
            {
                row = [];
                rows.Add(row);
                previousRow = placements[index].RowIndex;
            }
            row!.Add(entries[index].Game);
        }
        return new(entries, placements, rows.ToArray());
    }
}
