using Avalonia.Headless.XUnit;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public sealed class LibraryViewProjectionTests
{
    private static readonly LibraryGridSpec Grid = new(900, 28, 260, 1, false);
    private static GameViewModel Game(string title, int count = 0) => new(new Game
    {
        SystemId = "playstation", Path = "/game.chd", Title = title, PlayCount = count,
    }, "PlayStation", "PS1", "#FFFFFF");

    [AvaloniaFact]
    public async Task WorkerUsesCapturedTitlesAndKeysDespiteLaterEdits()
    {
        using var a = Game("alpha", 2);
        using var b = Game("Beta", 1);
        var entries = new[] { a, b }.Select(g => LibraryViewEntry.Capture(g, LibrarySortColumn.PlayCount)).ToArray();
        a.Title = "Changed after capture";
        var result = await Task.Run(() => LibraryViewProjection.Build(entries, "ALPHA", false, false, Grid, default));
        Assert.Same(a, Assert.Single(result.Entries).Game);
        Assert.Equal("alpha", result.Entries[0].DisplayTitle);
    }

    [AvaloniaFact]
    public void NumericSortUsesNumericKeysAndAscendingTitleTieBreaker()
    {
        using var a = Game("alpha", 10);
        using var b = Game("Beta", 10);
        using var c = Game("Charlie", 2);
        var entries = new[] { b, c, a }.Select(g => LibraryViewEntry.Capture(g, LibrarySortColumn.PlayCount)).ToArray();
        var result = LibraryViewProjection.Build(entries, "", true, false, Grid, default);
        Assert.Equal(new[] { a, b, c }, result.Entries.Select(e => e.Game));
        Assert.Equal(new[] { a, b, c }, result.Rows.SelectMany(row => row));
    }

    [AvaloniaFact]
    public void RecencyOrderSurvivesFilteringAndUnknownViewportPublishesNoRows()
    {
        using var a = Game("Beta");
        using var b = Game("Alpha");
        var entries = new[] { a, b }.Select(g => LibraryViewEntry.Capture(g, LibrarySortColumn.Title)).ToArray();
        var result = LibraryViewProjection.Build(entries, "a", false, true, Grid with { Width = 0 }, default);
        Assert.Equal(new[] { a, b }, result.Entries.Select(e => e.Game));
        Assert.Empty(result.Rows);
        Assert.Empty(result.Placements);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            LibraryViewProjection.Build(entries, "", false, false, Grid, canceled.Token));
    }
}
