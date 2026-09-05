using EmuShelf.App.ViewModels.Layout;

namespace EmuShelf.App.Tests;

/// <summary>
/// The cover-grid packer: full rows fill the width edge-to-edge by scaling (no gaps, no side
/// gutter), every row stays dense at ~target height so nothing balloons, and the last partial row is
/// left-packed. One freak scan cannot dominate a row.
/// </summary>
public class JustifiedCoverLayoutTests
{
    private const double Spacing = 28;
    private const double Target = 250;

    // The horizontal span a row occupies (first cover's left edge to the last cover's right edge).
    private static double RowSpan(IReadOnlyList<CoverPlacement> placements, int rowIndex)
    {
        var row = placements.Where(placement => placement.RowIndex == rowIndex).ToList();
        var left = row.Min(placement => placement.CenterX - placement.Width / 2);
        var right = row.Max(placement => placement.CenterX + placement.Width / 2);
        return right - left;
    }

    [Fact]
    public void Pack_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(JustifiedCoverLayout.Pack([], 1000, Spacing, Target));
    }

    [Fact]
    public void Pack_FullRowsFillTheAvailableWidth()
    {
        const double available = 1000;
        var ratios = Enumerable.Repeat(0.708, 14).ToList(); // several full rows plus a remainder

        var placements = JustifiedCoverLayout.Pack(ratios, available, Spacing, Target);

        var rowCount = placements.Max(placement => placement.RowIndex) + 1;
        Assert.True(rowCount >= 2, "expected the covers to wrap into multiple rows");

        // Every row EXCEPT the last spans the full width (scaled to fill, no gaps).
        for (var row = 0; row < rowCount - 1; row++)
            Assert.True(Math.Abs(available - RowSpan(placements, row)) <= 4.0,
                $"row {row} span {RowSpan(placements, row)} did not fill {available}");
    }

    [Fact]
    public void Pack_RowsStayDense_NoRowBalloonsAboveTarget()
    {
        // The fix for "arcade covers are too big": a wide-cover row holds fewer covers but its height
        // never exceeds the target — it is committed the moment it is full enough.
        var placements = JustifiedCoverLayout.Pack(Enumerable.Repeat(1.333, 12).ToList(), 1800, Spacing, Target);

        Assert.All(placements, placement => Assert.True(placement.Height <= Target + 0.5,
            $"a cover was {placement.Height} tall, above the {Target} target"));
        Assert.True(placements.Max(placement => placement.RowIndex) >= 1, "expected multiple rows");
    }

    [Fact]
    public void Pack_CoversInARowShareOneHeight()
    {
        var placements = JustifiedCoverLayout.Pack(
            [1.0, 0.708, 1.4, 0.6, 1.0, 0.708, 1.4, 0.6, 1.0, 0.708], 900, Spacing, Target);

        foreach (var row in placements.GroupBy(placement => placement.RowIndex))
            Assert.Single(row.Select(placement => placement.Height).Distinct());
    }

    [Fact]
    public void Pack_ShortLastRowMatchesTheRowAboveIt()
    {
        // A partial final row must not render taller than the full rows above it: many same-ratio covers
        // wrap so the last row holds fewer, and it must share the previous row's height.
        var placements = JustifiedCoverLayout.Pack(Enumerable.Repeat(0.708, 13).ToList(), 1000, Spacing, Target);

        var lastRow = placements.Max(placement => placement.RowIndex);
        Assert.True(lastRow >= 1, "expected the covers to wrap so there is a row above the last");
        var lastRowHeight = placements.First(placement => placement.RowIndex == lastRow).Height;
        var priorRowHeight = placements.First(placement => placement.RowIndex == lastRow - 1).Height;
        Assert.Equal(priorRowHeight, lastRowHeight);
    }

    [Fact]
    public void Pack_LastRowIsLeftPacked_NotUpscaled()
    {
        // Two covers easily fit one row on a wide viewport, so the only (last) row keeps the target
        // height and is left-packed rather than blown up to fill the width.
        var placements = JustifiedCoverLayout.Pack([0.708, 0.708], 2000, Spacing, Target);

        Assert.All(placements, placement => Assert.True(placement.Height <= Target + 0.5));
        Assert.True(RowSpan(placements, 0) < 2000); // left-packed, ragged right
    }

    [Fact]
    public void Pack_AssignsContiguousRowIndicesInOrder()
    {
        var placements = JustifiedCoverLayout.Pack(Enumerable.Repeat(1.0, 20).ToList(), 800, Spacing, Target);

        var current = 0;
        foreach (var placement in placements)
        {
            Assert.True(placement.RowIndex == current || placement.RowIndex == current + 1);
            current = placement.RowIndex;
        }
    }

    [Fact]
    public void Pack_ClampsAFreakRatio_SoItCannotDominateARow()
    {
        // A degenerate 0.05 banner is clamped to the min aspect, so at the target height its width is
        // bounded rather than a hair-thin sliver that skews the justification.
        var placements = JustifiedCoverLayout.Pack([0.05], 1000, Spacing, Target);

        var expectedWidth = Math.Round(Target * JustifiedCoverLayout.MinAspectRatio);
        Assert.Equal(expectedWidth, placements[0].Width);
    }

    [Fact]
    public void Pack_ZeroWidth_StillProducesSaneSizes()
    {
        // Before the viewport is measured, everything lands in row 0 at the natural target height.
        var placements = JustifiedCoverLayout.Pack([1.0, 0.708], 0, Spacing, Target);

        Assert.All(placements, placement => Assert.Equal(0, placement.RowIndex));
        Assert.All(placements, placement => Assert.Equal(Target, placement.Height));
    }

    [Fact]
    public void Pack_LandscapeCovers_HonourTheMinimumPerRow()
    {
        // The couch case: SNES boxes (1.434) fill a Thor-width row after three covers, which reads as a
        // sparse shelf of huge covers next to the five a portrait platform fits. The minimum packs them
        // four to a row instead — shorter covers, denser shelf.
        const double available = 1096; // the Thor's couch grid, gutters removed
        var ratios = Enumerable.Repeat(1.434, 12).ToList();

        var loose = JustifiedCoverLayout.Pack(ratios, available, Spacing, 300);
        var packed = JustifiedCoverLayout.Pack(ratios, available, Spacing, 300, minCoversPerRow: 4);

        Assert.Equal(3, loose.Count(placement => placement.RowIndex == 0));
        Assert.Equal(4, packed.Count(placement => placement.RowIndex == 0));
        // Still justified: the denser row fills the same width, edge to edge.
        Assert.True(Math.Abs(available - RowSpan(packed, 0)) <= 4.0);
    }

    [Fact]
    public void Pack_MixedRatios_LeftoverRowHeldBackByTheMinimumStillFitsTheWidth()
    {
        // All Games / search on the couch: a portrait row commits at ~289 px, then three SNES covers
        // remain. Without the minimum they could never have filled the width; with it they are held
        // back by the count, and rendering them at the portrait row's height put ~130 px past the
        // right gutter. The leftover row must shrink to fit, and stay no taller than the row above.
        const double available = 1200;
        var ratios = Enumerable.Repeat(0.708, 5).Concat(Enumerable.Repeat(1.434, 3)).ToList();

        var placements = JustifiedCoverLayout.Pack(ratios, available, 44, 300, minCoversPerRow: 4);

        Assert.Equal(5, placements.Count(placement => placement.RowIndex == 0));
        Assert.Equal(3, placements.Count(placement => placement.RowIndex == 1));
        Assert.True(RowSpan(placements, 1) <= available + 1.0,
            $"leftover row spans {RowSpan(placements, 1):F0} in a {available:F0} viewport");
        Assert.True(placements[5].Height <= placements[0].Height,
            "a partial last row must not render taller than the full row above it");
    }

    [Fact]
    public void Pack_PortraitCovers_AreUnchangedByAMinimumTheyAlreadyExceed()
    {
        var ratios = Enumerable.Repeat(0.708, 12).ToList();

        var loose = JustifiedCoverLayout.Pack(ratios, 1096, Spacing, 300);
        var packed = JustifiedCoverLayout.Pack(ratios, 1096, Spacing, 300, minCoversPerRow: 4);

        Assert.Equal(loose, packed);
    }

    [Fact]
    public void Pack_NarrowViewport_DropsTheMinimumRatherThanPackingSlivers()
    {
        // Four covers cannot each reach MinimumColumnCoverWidth here, so the minimum lapses and the
        // height rule packs the row alone — a small window shows fewer, readable covers.
        var available = (2 * JustifiedCoverLayout.MinimumColumnCoverWidth) + Spacing;
        var ratios = Enumerable.Repeat(1.434, 8).ToList();

        var placements = JustifiedCoverLayout.Pack(ratios, available, Spacing, 300, minCoversPerRow: 4);

        Assert.Equal(2, placements.Count(placement => placement.RowIndex == 0));
    }
}
