namespace EmuShelf.Core.Handheld;

public enum HandheldDirection
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>What a move inside one zone did.</summary>
public enum HandheldMoveOutcome
{
    /// <summary>The selection moved within the zone.</summary>
    Moved,

    /// <summary>Nothing is that way; the view answers with an edge bump.</summary>
    Wall,

    /// <summary>The move left the zone upward (to a folder's header bar).</summary>
    ExitUp,

    /// <summary>The move left the zone downward (to the dock).</summary>
    ExitDown,
}

/// <summary>A place on home: a page and a slot on it (0–19, row by row).</summary>
public readonly record struct HandheldHomePosition(int Page, int Slot)
{
    public int Column => Slot % HandheldHomeLayout.Columns;
    public int Row => Slot / HandheldHomeLayout.Columns;
}

/// <summary>
/// D-pad movement on the home grid. Every slot is a landing place, empty or not (decision 11), so moves
/// are purely geometric: left and right cross page edges onto the neighbouring page's facing column,
/// down from the bottom row leaves for the dock, and up from the top row has nowhere to go.
/// </summary>
public static class HandheldHomeNavigation
{
    public static (HandheldHomePosition Position, HandheldMoveOutcome Outcome) Move(
        HandheldHomePosition position, HandheldDirection direction, int pageCount)
    {
        const int columns = HandheldHomeLayout.Columns;
        const int rows = HandheldHomeLayout.Rows;
        var (column, row) = (position.Column, position.Row);
        switch (direction)
        {
            case HandheldDirection.Left when column > 0:
                return (position with { Slot = position.Slot - 1 }, HandheldMoveOutcome.Moved);
            case HandheldDirection.Left when position.Page > 0:
                return (new HandheldHomePosition(position.Page - 1, row * columns + columns - 1), HandheldMoveOutcome.Moved);
            case HandheldDirection.Right when column < columns - 1:
                return (position with { Slot = position.Slot + 1 }, HandheldMoveOutcome.Moved);
            case HandheldDirection.Right when position.Page < pageCount - 1:
                return (new HandheldHomePosition(position.Page + 1, row * columns), HandheldMoveOutcome.Moved);
            case HandheldDirection.Up when row > 0:
                return (position with { Slot = position.Slot - columns }, HandheldMoveOutcome.Moved);
            case HandheldDirection.Down when row < rows - 1:
                return (position with { Slot = position.Slot + columns }, HandheldMoveOutcome.Moved);
            case HandheldDirection.Down:
                return (position, HandheldMoveOutcome.ExitDown);
            default:
                return (position, HandheldMoveOutcome.Wall);
        }
    }
}

/// <summary>
/// D-pad movement inside a folder: a grid three rows tall that scrolls sideways a column at a time.
/// Games fill it column by column, so index <c>i</c> sits in column <c>i / 3</c>, row <c>i % 3</c>.
/// Four columns are shown at once, with the next one peeking in.
/// </summary>
public static class HandheldFolderNavigation
{
    public const int Rows = 3;
    public const int VisibleColumns = 4;

    public static int ColumnCount(int itemCount) => (itemCount + Rows - 1) / Rows;

    public static (int Index, HandheldMoveOutcome Outcome) Move(int index, int itemCount, HandheldDirection direction)
    {
        if (itemCount <= 0)
        {
            return direction switch
            {
                HandheldDirection.Up => (0, HandheldMoveOutcome.ExitUp),
                HandheldDirection.Down => (0, HandheldMoveOutcome.ExitDown),
                _ => (0, HandheldMoveOutcome.Wall),
            };
        }

        index = Math.Clamp(index, 0, itemCount - 1);
        var column = index / Rows;
        var row = index % Rows;
        var lastColumn = ColumnCount(itemCount) - 1;
        return direction switch
        {
            HandheldDirection.Up when row == 0 => (index, HandheldMoveOutcome.ExitUp),
            HandheldDirection.Up => (index - 1, HandheldMoveOutcome.Moved),
            // The last game of a short final column has nothing below it either.
            HandheldDirection.Down when row == Rows - 1 || index + 1 >= itemCount => (index, HandheldMoveOutcome.ExitDown),
            HandheldDirection.Down => (index + 1, HandheldMoveOutcome.Moved),
            HandheldDirection.Left when column == 0 => (index, HandheldMoveOutcome.Wall),
            HandheldDirection.Left => (index - Rows, HandheldMoveOutcome.Moved),
            HandheldDirection.Right when column == lastColumn => (index, HandheldMoveOutcome.Wall),
            // Into a short final column: land on its last game rather than an empty cell.
            HandheldDirection.Right => (Math.Min(index + Rows, itemCount - 1), HandheldMoveOutcome.Moved),
            _ => (index, HandheldMoveOutcome.Wall),
        };
    }

    /// <summary>
    /// The first column to show so the selected column stays inside the four visible ones, scrolling by
    /// as little as possible, and never past the point where the last column sits at the right edge.
    /// </summary>
    public static int FirstVisibleColumn(int selectedColumn, int currentFirstColumn, int itemCount)
    {
        var maxFirst = Math.Max(0, ColumnCount(itemCount) - VisibleColumns);
        var first = currentFirstColumn;
        if (selectedColumn < first)
            first = selectedColumn;
        else if (selectedColumn > first + VisibleColumns - 1)
            first = selectedColumn - (VisibleColumns - 1);
        return Math.Clamp(first, 0, maxFirst);
    }
}
