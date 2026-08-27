namespace EmuShelf.App.ViewModels.Layout;

/// <summary>Placement the packer computes for one cover: which row it lands in, its rendered
/// size (already scaled to justify the row and rounded to whole pixels), and its horizontal
/// centre within the row's coordinate space — the geometry both the grid view and the gamepad
/// nearest-centre navigation read.</summary>
public readonly record struct CoverPlacement(int RowIndex, double Width, double Height, double CenterX);

/// <summary>
/// Packs cover frames of the given aspect ratios (each the platform's canonical ratio) into justified
/// rows: fill a row left-to-right and commit it the moment the height needed to fill the width drops to
/// the target, then scale that row so it fills the width edge-to-edge. No gaps and no side gutters — the
/// row is always flush on both sides. Because a row is committed as soon as it is "full enough", every
/// row is dense (a wide-cover platform like arcade simply holds fewer covers per row) and its height
/// stays at ~target, so covers read as a consistent size and none balloon. The last, incomplete row is
/// left-packed at the target height (only shrunk if a lone over-wide cover would overflow). Pure and
/// deterministic, so the view and controller navigation share one geometry. (The view crops off-ratio
/// art into the frame with UniformToFill; that is a rendering concern, not the packer's.)
/// </summary>
public static class JustifiedCoverLayout
{
    /// <summary>Clamp on a cover's width:height so one corrupt banner or sliver scan cannot dominate
    /// a row. The library's real covers (square ~1.0 to tall UMD ~0.6) sit well inside this band.</summary>
    public const double MinAspectRatio = 0.4;
    public const double MaxAspectRatio = 2.4;

    /// <param name="aspectRatios">Each cover's width ÷ height, in display order.</param>
    /// <param name="availableWidth">Content width the rows fill (gutters already removed).</param>
    /// <param name="spacing">Gap between covers in a row.</param>
    /// <param name="targetRowHeight">The height the packer keeps every full row at (or just under).</param>
    public static IReadOnlyList<CoverPlacement> Pack(
        IReadOnlyList<double> aspectRatios,
        double availableWidth,
        double spacing,
        double targetRowHeight)
    {
        var placements = new CoverPlacement[aspectRatios.Count];
        if (aspectRatios.Count == 0)
            return placements;

        // Degenerate viewport (not measured yet): lay everything out at the natural target height so
        // callers still get sane sizes; the real pack runs once a width arrives.
        if (availableWidth <= 0 || targetRowHeight <= 0)
        {
            for (var i = 0; i < aspectRatios.Count; i++)
            {
                var r = Clamp(aspectRatios[i]);
                placements[i] = new CoverPlacement(0, Math.Round(targetRowHeight * r), Math.Round(targetRowHeight), 0);
            }
            return placements;
        }

        var rowIndex = 0;
        var start = 0;
        var ratioSum = 0d; // sum of clamped aspect ratios (widths at height = 1) in the current row

        for (var i = 0; i < aspectRatios.Count; i++)
        {
            ratioSum += Clamp(aspectRatios[i]);
            var count = i - start + 1;
            var gaps = spacing * (count - 1);
            // The height this row would be if scaled to fill the width. It only shrinks as more covers
            // join, so once it reaches the target the row is full — committing here keeps every row
            // dense and at ~target height instead of a sparse row that has to stretch or gap.
            var filledHeight = (availableWidth - gaps) / ratioSum;

            if (filledHeight <= targetRowHeight)
            {
                FinalizeRow(aspectRatios, placements, start, i + 1, rowIndex, filledHeight, spacing);
                rowIndex++;
                start = i + 1;
                ratioSum = 0;
            }
        }

        // Leftover covers form a final, left-packed row at the target height — never scaled up. A lone
        // over-wide cover that would overflow the width is shrunk to fit instead.
        if (start < aspectRatios.Count)
        {
            var count = aspectRatios.Count - start;
            var gaps = spacing * (count - 1);
            var rowHeight = Math.Min(targetRowHeight, (availableWidth - gaps) / ratioSum);
            FinalizeRow(aspectRatios, placements, start, aspectRatios.Count, rowIndex, rowHeight, spacing);
        }

        return placements;
    }

    private static void FinalizeRow(
        IReadOnlyList<double> aspectRatios,
        CoverPlacement[] placements,
        int start,
        int end,
        int rowIndex,
        double rowHeight,
        double spacing)
    {
        var height = Math.Round(rowHeight);
        var x = 0d;
        for (var i = start; i < end; i++)
        {
            var width = Math.Round(height * Clamp(aspectRatios[i]));
            placements[i] = new CoverPlacement(rowIndex, width, height, x + width / 2);
            x += width + spacing;
        }
    }

    private static double Clamp(double aspectRatio) =>
        double.IsFinite(aspectRatio) && aspectRatio > 0
            ? Math.Clamp(aspectRatio, MinAspectRatio, MaxAspectRatio)
            : 0.708;
}
