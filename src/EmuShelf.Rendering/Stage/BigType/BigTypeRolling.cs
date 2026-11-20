namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// One character's window in a picture of text, on the canvas (as a <see cref="StageImage"/>'s X and Y are): its advance across
/// and its line's box down, and its font size (the em a rolling character travels by).
/// </summary>
public readonly record struct StageTextCell(char Character, float X, float Y, float Width, float Height, float Em);

/// <summary>
/// Where each character of a pictured text sits (M47 B5): what lets the native screens roll the characters that changed between
/// two pictures, each clipped to its own cell, with no new drawing. <see cref="Text"/> is the cells' characters in order.
/// </summary>
public sealed record StageText(IReadOnlyList<StageTextCell> Cells)
{
    public string Text { get; } = new(Cells.Select(cell => cell.Character).ToArray());
}

/// <summary>Which picture a piece of a roll is cut from.</summary>
public enum BigTypeRollSource
{
    Old,
    New,
}

/// <summary>One piece of a rolling text to draw: a picture, the canvas rectangle it is clipped to, moved down by <paramref name="OffsetY"/>.</summary>
public readonly record struct BigTypeRollPiece(BigTypeRollSource Source, float ClipX, float ClipY, float ClipWidth, float ClipHeight, float OffsetY,
    float Opacity);

/// <summary>
/// How a pictured text rolls from its old picture to its new one (M47 B5, the mock's move 5): every part of the new picture that
/// is not a changed character is drawn as it is; each changed character's old glyph leaves upward and the new one rises in, each
/// clipped to its own cell; a cell the new text lost fades. Pure: the compositors draw the pieces with their own scissor and quad.
/// </summary>
public static class BigTypeRollDraw
{
    /// <summary>
    /// The pieces of the roll <paramref name="since"/> ms in, for the old picture's text and bounds and the new one's. Null when
    /// the roll has finished (draw the new picture as it is).
    /// </summary>
    public static IReadOnlyList<BigTypeRollPiece>? Pieces(StageText old, StageText next, float newX, float newY, float newWidth, float newHeight,
        float since)
    {
        var plan = BigTypeMotion.RollPlan(old.Text, next.Text);
        if (since >= BigTypeMotion.RollMilliseconds(plan.Length) || !plan.Any(cell => cell != BigTypeRollCell.Same))
            return null;
        var pieces = new List<BigTypeRollPiece>();
        // The parts of the new picture left alone: everything across it but the changed cells' columns, old and new.
        var taken = new List<(float From, float To)>();
        for (var i = 0; i < plan.Length; i++)
        {
            if (plan[i] == BigTypeRollCell.Same)
                continue;
            if (i < next.Cells.Count)
                taken.Add((next.Cells[i].X, next.Cells[i].X + next.Cells[i].Width));
            if (i < old.Cells.Count)
                taken.Add((old.Cells[i].X, old.Cells[i].X + old.Cells[i].Width));
        }
        var x = newX;
        var pictureRight = newX + newWidth;
        foreach (var (from, to) in taken.OrderBy(range => range.From))
        {
            // A moving cell uses every pixel its scissor touches. Reserve those whole
            // columns before drawing static gaps, including the last glyph's fractional
            // edge where the bitmap width was rounded up to an integer.
            var gapEnd = Math.Min(MathF.Floor(from), pictureRight);
            if (x < gapEnd)
                pieces.Add(new BigTypeRollPiece(BigTypeRollSource.New, x, newY, gapEnd - x, newHeight, 0f, 1f));
            x = Math.Max(x, MathF.Ceiling(to));
        }
        if (x < pictureRight)
            pieces.Add(new BigTypeRollPiece(BigTypeRollSource.New, x, newY, pictureRight - x, newHeight, 0f, 1f));

        for (var i = 0; i < plan.Length; i++)
        {
            var (oldOffset, newOffset, oldOpacity) = BigTypeMotion.Roll(plan[i], i, since);
            switch (plan[i])
            {
                case BigTypeRollCell.Changed:
                    Add(BigTypeRollSource.Old, old.Cells[i], oldOffset, 1f);
                    Add(BigTypeRollSource.New, next.Cells[i], newOffset, 1f);
                    break;
                case BigTypeRollCell.Arriving:
                    Add(BigTypeRollSource.New, next.Cells[i], newOffset, 1f);
                    break;
                case BigTypeRollCell.Leaving:
                    Add(BigTypeRollSource.Old, old.Cells[i], 0f, oldOpacity);
                    break;
            }
        }
        return pieces;

        void Add(BigTypeRollSource source, StageTextCell cell, float offsetEm, float opacity)
        {
            // The mock's travel is 1.1 em, which clears a browser's line box (1.15 em). A cell here is its text block's whole height
            // (the face's own line, about 1.5 em; a message on two lines, twice that), so at 1.1 em the bottom of a leaving glyph stayed
            // in the top of its window until the roll ended, and the top of an arriving one sat in the bottom of its window before it
            // rose (M47 B11, the owner on the Thor). The travel is the mock's, or the window's height where that is more, so a glyph
            // that has left, or not yet come, is wholly out of its window.
            var travel = Math.Max(BigTypeMotion.DigitTravelEm * cell.Em, cell.Height);
            var offset = offsetEm / BigTypeMotion.DigitTravelEm * travel;
            if (opacity <= 0f || Math.Abs(offset) >= cell.Height)
                return;
            pieces.Add(new BigTypeRollPiece(source, cell.X, cell.Y, cell.Width, cell.Height, offset, opacity));
        }
    }
}
