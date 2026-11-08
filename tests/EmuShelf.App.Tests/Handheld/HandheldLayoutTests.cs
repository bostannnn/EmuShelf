using System;
using System.Linq;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public class HandheldLayoutTests
{
    [Fact]
    public void DefaultLayout_IsRecentThenPlatformFoldersInOrderThenSettingsThenEmptySlots()
    {
        var layout = HandheldHomeLayout.CreateDefault(["ps2", "gc", "wii"]);

        // Home is a place with slots: 40 of them, two pages, the rest drawn as empty slots.
        Assert.Equal(HandheldHomeLayout.MinimumPages, layout.Pages.Count);
        var page = layout.Pages[0];
        Assert.Equal(HandheldHomeLayout.SlotsPerPage, page.Slots.Count);
        Assert.Equal(HandheldSlot.Recent, page.Slots[0]);
        Assert.Equal(["ps2", "gc", "wii"], page.Slots.Skip(1).Take(3).Select(slot => slot.SystemId));
        Assert.All(page.Slots.Skip(1).Take(3), slot => Assert.Equal(HandheldSlotKind.PlatformFolder, slot.Kind));
        // Settings is an app on the grid, right after the platform folders (decision 18).
        Assert.Equal(HandheldSlot.Settings, page.Slots[4]);
        Assert.All(page.Slots.Skip(5).Concat(layout.Pages[1].Slots), slot => Assert.True(slot.IsEmpty));
    }

    [Fact]
    public void DefaultLayout_EmptyLibraryIsSettingsAndTwoPagesOfEmptySlots()
    {
        var layout = HandheldHomeLayout.CreateDefault([]);

        // There is always a way into Settings, even before there are any games.
        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(HandheldSlot.Settings, layout.SlotAt(0));
        Assert.All(layout.AllSlots().Skip(1), slot => Assert.True(slot.IsEmpty));
    }

    [Fact]
    public void DefaultLayout_OverflowsOntoMorePagesAndDropsDuplicates()
    {
        var ids = Enumerable.Range(0, 43).Select(i => $"sys{i}").Append("sys0").ToArray();

        var layout = HandheldHomeLayout.CreateDefault(ids);

        // Recent, 43 platforms and Settings: 45 tiles need a third page.
        Assert.Equal(3, layout.Pages.Count);
        Assert.Equal("sys18", layout.SlotAt(0, 19).SystemId);
        Assert.Equal("sys19", layout.SlotAt(1, 0).SystemId);
        Assert.Equal("sys42", layout.SlotAt(2, 3).SystemId);
        Assert.Equal(HandheldSlot.Settings, layout.SlotAt(2, 4));
        Assert.True(layout.SlotAt(2, 5).IsEmpty);
    }

    [Fact]
    public void Layout_RejectsPagesThatAreNotFullLength()
    {
        Assert.Throws<ArgumentException>(() =>
            new HandheldHomeLayout([new HandheldHomePage([HandheldSlot.Empty])]));
    }

    [Theory]
    [InlineData(0, HandheldDirection.Right, 1)]
    [InlineData(6, HandheldDirection.Up, 1)]
    [InlineData(6, HandheldDirection.Down, 11)]
    [InlineData(6, HandheldDirection.Left, 5)]
    public void HomeMove_LandsOnAnySlotIncludingEmptyOnes(int from, HandheldDirection direction, int expected)
    {
        var (position, outcome) = HandheldHomeNavigation.Move(new HandheldHomePosition(0, from), direction, pageCount: 1);

        Assert.Equal(HandheldMoveOutcome.Moved, outcome);
        Assert.Equal(new HandheldHomePosition(0, expected), position);
    }

    [Fact]
    public void HomeMove_RightFromLastColumnTurnsToTheNextPageSameRow()
    {
        var (position, outcome) = HandheldHomeNavigation.Move(new HandheldHomePosition(0, 9), HandheldDirection.Right, pageCount: 2);

        Assert.Equal(HandheldMoveOutcome.Moved, outcome);
        Assert.Equal(new HandheldHomePosition(1, 5), position);
    }

    [Fact]
    public void HomeMove_LeftFromFirstColumnTurnsBackToThePreviousPageLastColumn()
    {
        var (position, _) = HandheldHomeNavigation.Move(new HandheldHomePosition(1, 15), HandheldDirection.Left, pageCount: 2);

        Assert.Equal(new HandheldHomePosition(0, 19), position);
    }

    [Theory]
    [InlineData(4, HandheldDirection.Right)]
    [InlineData(0, HandheldDirection.Left)]
    [InlineData(2, HandheldDirection.Up)]
    public void HomeMove_EdgeWithNoPageBeyondIsAWall(int from, HandheldDirection direction)
    {
        var (position, outcome) = HandheldHomeNavigation.Move(new HandheldHomePosition(0, from), direction, pageCount: 1);

        Assert.Equal(HandheldMoveOutcome.Wall, outcome);
        Assert.Equal(from, position.Slot);
    }

    [Fact]
    public void HomeMove_DownFromTheBottomRowLeavesForTheDock()
    {
        var (_, outcome) = HandheldHomeNavigation.Move(new HandheldHomePosition(0, 17), HandheldDirection.Down, pageCount: 1);

        Assert.Equal(HandheldMoveOutcome.ExitDown, outcome);
    }

    [Fact]
    public void FolderMove_FillsColumnByColumn()
    {
        // 7 games: column 0 = 0,1,2; column 1 = 3,4,5; column 2 = 6.
        Assert.Equal((1, HandheldMoveOutcome.Moved), HandheldFolderNavigation.Move(0, 7, HandheldDirection.Down));
        Assert.Equal((4, HandheldMoveOutcome.Moved), HandheldFolderNavigation.Move(1, 7, HandheldDirection.Right));
        Assert.Equal((1, HandheldMoveOutcome.Moved), HandheldFolderNavigation.Move(4, 7, HandheldDirection.Left));
    }

    [Fact]
    public void FolderMove_IntoAShortLastColumnLandsOnItsLastGame()
    {
        Assert.Equal((6, HandheldMoveOutcome.Moved), HandheldFolderNavigation.Move(5, 7, HandheldDirection.Right));
    }

    [Fact]
    public void FolderMove_ExitsUpToTheHeaderAndDownToTheDock()
    {
        Assert.Equal(HandheldMoveOutcome.ExitUp, HandheldFolderNavigation.Move(3, 7, HandheldDirection.Up).Outcome);
        Assert.Equal(HandheldMoveOutcome.ExitDown, HandheldFolderNavigation.Move(2, 7, HandheldDirection.Down).Outcome);
        // The last game of a short column has nothing below it.
        Assert.Equal(HandheldMoveOutcome.ExitDown, HandheldFolderNavigation.Move(6, 7, HandheldDirection.Down).Outcome);
        Assert.Equal(HandheldMoveOutcome.Wall, HandheldFolderNavigation.Move(6, 7, HandheldDirection.Right).Outcome);
        Assert.Equal(HandheldMoveOutcome.Wall, HandheldFolderNavigation.Move(1, 7, HandheldDirection.Left).Outcome);
    }

    [Fact]
    public void FolderMove_EmptyFolderOnlyLeaves()
    {
        Assert.Equal(HandheldMoveOutcome.ExitUp, HandheldFolderNavigation.Move(0, 0, HandheldDirection.Up).Outcome);
        Assert.Equal(HandheldMoveOutcome.ExitDown, HandheldFolderNavigation.Move(0, 0, HandheldDirection.Down).Outcome);
        Assert.Equal(HandheldMoveOutcome.Wall, HandheldFolderNavigation.Move(0, 0, HandheldDirection.Right).Outcome);
    }

    [Theory]
    // 30 games = 10 columns; four shown at once.
    [InlineData(2, 0, 0)]   // still inside the window
    [InlineData(4, 0, 1)]   // one past the right edge scrolls by one column
    [InlineData(9, 0, 6)]   // a jump keeps the selection at the right edge
    [InlineData(1, 5, 1)]   // left of the window scrolls back to it
    public void FolderWindow_ScrollsByAsLittleAsPossible(int selectedColumn, int currentFirst, int expectedFirst)
    {
        Assert.Equal(expectedFirst, HandheldFolderNavigation.FirstVisibleColumn(selectedColumn, currentFirst, 30));
    }

    [Fact]
    public void FolderWindow_NeverScrollsAShortFolder()
    {
        Assert.Equal(0, HandheldFolderNavigation.FirstVisibleColumn(2, 0, 9));
    }

    private sealed record Item(string Title, DateTimeOffset? LastPlayed, int Plays);

    private static readonly Item[] Items =
    [
        new("Okami", new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), 2),
        new("Bully", null, 0),
        new("Ape Escape", new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), 2),
        new("Zone of the Enders", null, 0),
        new("Katamari", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 9),
    ];

    private static string[] Sorted(HandheldFolderSort sort) =>
        HandheldFolderSorting.Apply(Items, sort, item => item.Title, item => item.LastPlayed, item => item.Plays)
            .Select(item => item.Title)
            .ToArray();

    [Fact]
    public void Sort_ManualKeepsTheFolderOrder() =>
        Assert.Equal(Items.Select(item => item.Title), Sorted(HandheldFolderSort.Manual));

    [Fact]
    public void Sort_TitleIsAlphabetical() =>
        Assert.Equal(["Ape Escape", "Bully", "Katamari", "Okami", "Zone of the Enders"], Sorted(HandheldFolderSort.Title));

    [Fact]
    public void Sort_LastPlayedPutsNewestFirstAndNeverPlayedLastByTitle() =>
        Assert.Equal(["Ape Escape", "Okami", "Katamari", "Bully", "Zone of the Enders"], Sorted(HandheldFolderSort.LastPlayed));

    [Fact]
    public void Sort_MostPlayedBreaksTiesByTitle() =>
        Assert.Equal(["Katamari", "Ape Escape", "Okami", "Bully", "Zone of the Enders"], Sorted(HandheldFolderSort.MostPlayed));

    [Fact]
    public void Sort_ChipCyclesThroughAllFourAndBack()
    {
        var sort = HandheldFolderSort.Manual;
        var seen = Enumerable.Range(0, 4).Select(_ => sort = HandheldFolderSorting.Next(sort)).ToArray();
        Assert.Equal([HandheldFolderSort.Title, HandheldFolderSort.LastPlayed, HandheldFolderSort.MostPlayed, HandheldFolderSort.Manual], seen);
    }
}
