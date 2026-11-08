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

/// <summary>A place on home: a page and a slot on it (row by row, in the grid the Launcher shows).</summary>
public readonly record struct HandheldHomePosition(int Page, int Slot);

/// <summary>
/// The shape the Launcher draws home in. The saved layout is one flat run of slots (stored as 5×4 pages);
/// the Launcher cuts it into pages of its own shape: 5×4 on the Thor's 1240×1080 screen, 7×3 at the same
/// tile size on the 16:9 screen once the screens are swapped (M45 H5). Reading order is kept, so a swap
/// reflows home the way a phone's launcher reflows on rotation.
/// </summary>
public readonly record struct HandheldHomeGrid(int Columns, int Rows)
{
    public static HandheldHomeGrid Standard { get; } = new(HandheldHomeLayout.Columns, HandheldHomeLayout.Rows);

    public static HandheldHomeGrid Wide { get; } = new(7, 3);

    public int SlotsPerPage => Columns * Rows;

    /// <summary>Pages needed to show <paramref name="slotCount"/> slots (at least one).</summary>
    public int PageCount(int slotCount) => Math.Max(1, (slotCount + SlotsPerPage - 1) / SlotsPerPage);

    public HandheldHomePosition PositionOf(int index) => new(index / SlotsPerPage, index % SlotsPerPage);

    public int IndexOf(HandheldHomePosition position) => (position.Page * SlotsPerPage) + position.Slot;
}

/// <summary>
/// D-pad movement on the home grid. Every slot is a landing place, empty or not (decision 11), so moves
/// are purely geometric: left and right cross page edges onto the neighbouring page's facing column,
/// down from the bottom row leaves for the dock, and up from the top row has nowhere to go.
/// </summary>
public static class HandheldHomeNavigation
{
    public static (HandheldHomePosition Position, HandheldMoveOutcome Outcome) Move(
        HandheldHomePosition position, HandheldDirection direction, int pageCount) =>
        Move(position, direction, pageCount, HandheldHomeGrid.Standard);

    public static (HandheldHomePosition Position, HandheldMoveOutcome Outcome) Move(
        HandheldHomePosition position, HandheldDirection direction, int pageCount, HandheldHomeGrid grid)
    {
        var (columns, rows) = (grid.Columns, grid.Rows);
        var (column, row) = (position.Slot % columns, position.Slot / columns);
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
/// Four columns are shown at once on the 1240×1080 screen (seven on the 16:9 one), with the next one peeking in.
/// </summary>
public static class HandheldFolderNavigation
{
    public const int Rows = 3;
    public const int VisibleColumns = 4;

    /// <summary>Columns shown at once on the 16:9 screen: seven at the same 264 px pitch fit 1920 px (48 + 7 × 264 = 1896).</summary>
    public const int WideVisibleColumns = 7;

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
    public static int FirstVisibleColumn(int selectedColumn, int currentFirstColumn, int itemCount,
        int visibleColumns = VisibleColumns)
    {
        var maxFirst = Math.Max(0, ColumnCount(itemCount) - visibleColumns);
        var first = currentFirstColumn;
        if (selectedColumn < first)
            first = selectedColumn;
        else if (selectedColumn > first + visibleColumns - 1)
            first = selectedColumn - (visibleColumns - 1);
        return Math.Clamp(first, 0, maxFirst);
    }
}
