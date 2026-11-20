using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B5: the Big Type look's motion that needs no new drawing, as maths that renders nothing: the ring's path between targets,
/// which neighbours make room and by how much, which characters of a changed text roll, the idle hop's stagger, and that nothing
/// moves while the device asks for stillness. The numbers are held to tokens.js.
/// </summary>
public class HandheldBigTypeMotionTests
{
    // Two home tiles side by side (176 px tiles on a 224 px pitch, the ring 26 out from each), a dock button, a row.
    private static readonly BigTypeRing TileA = new(100, 100, 228, 228, 50);
    private static readonly BigTypeRing TileB = new(324, 100, 228, 228, 50);
    private static readonly BigTypeRing TileBelow = new(100, 296, 228, 228, 50);
    private static readonly BigTypeRing Dock = new(400, 900, 140, 140, 38);
    private static readonly BigTypeRing Row = new(80, 300, 900, 96, 32);

    // ---- tokens.js ----

    [Fact]
    public void TheNumbers_AreTokensJs()
    {
        var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
        double Entry(string name, string field)
        {
            var match = Regex.Match(tokens, $@"\b{name}:\s*\{{[^}}]*\b{field}:\s*(?<value>-?[0-9.]+)");
            Assert.True(match.Success, $"tokens.js has no {name}.{field}");
            return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }
        double Number(string name)
        {
            var match = Regex.Match(tokens, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
            Assert.True(match.Success, $"tokens.js has no {name}");
            return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }
        Assert.Equal(Entry("Ring", "ms"), BigTypeMotion.RingMilliseconds);
        Assert.Equal(Entry("Ring", "coversBothAt"), BigTypeMotion.RingCoversBothAt, 4);
        Assert.Equal(Entry("FocusLift", "ms"), BigTypeMotion.LiftMilliseconds);
        Assert.Equal(Entry("RingBreath", "periodMs"), BigTypeMotion.BreathPeriodMilliseconds);
        Assert.Equal(Entry("RingBreath", "scale"), BigTypeMotion.BreathScale, 4);
        Assert.Equal(Entry("DigitIn", "ms"), BigTypeMotion.DigitInMilliseconds);
        Assert.Equal(Entry("DigitIn", "delay"), BigTypeMotion.DigitInDelay);
        Assert.Equal(Entry("DigitIn", "stagger"), BigTypeMotion.DigitStagger);
        Assert.Equal(Entry("DigitIn", "travelEm"), BigTypeMotion.DigitTravelEm, 4);
        Assert.Equal(Entry("DigitOut", "ms"), BigTypeMotion.DigitOutMilliseconds);
        Assert.Equal(Entry("DigitOut", "travelEm"), BigTypeMotion.DigitTravelEm, 4);
        Assert.Equal(Entry("IdleHop", "afterMs"), BigTypeMotion.HopAfterMilliseconds);
        Assert.Equal(Entry("IdleHop", "everyMs"), BigTypeMotion.HopEveryMilliseconds);
        Assert.Equal(Entry("IdleHop", "ms"), BigTypeMotion.HopMilliseconds);
        Assert.Equal(Entry("IdleHop", "staggerPerTile"), BigTypeMotion.HopStagger);
        Assert.Equal(Entry("IdleHop", "rise"), BigTypeMotion.HopRise);
        Assert.Equal(Number("NeighbourPush"), BigTypeMotion.NeighbourPush);
        // Land: cubic-bezier(.2, 1.5, .35, 1), overshooting before it settles.
        Assert.Contains("Land:  'cubic-bezier(.2, 1.5, .35, 1)'", tokens);
        Assert.True(BigTypeMotion.Land(0.5f) > 1f);
        Assert.Equal(1f, BigTypeMotion.Land(1f), 4);
    }

    // ---- The ring's path ----

    [Fact]
    public void Ring_BetweenTwoTilesOfOneSize_CoversBothAt38Percent_SquashesAcrossItsTravel_ThenLands()
    {
        Assert.Equal(TileA, BigTypeMotion.RingAt(TileA, TileB, 0f));

        // At 38% it covers both: from the left one's left edge to the right one's right edge.
        var both = BigTypeMotion.RingAt(TileA, TileB, BigTypeMotion.RingCoversBothAt);
        AssertRing(new BigTypeRing(100, 100, 452, 228, 0), both, radius: false);

        // At 74% it is on the new tile, shorter along the travel (10 px each side) and taller across it (6 px each side).
        var squash = BigTypeMotion.RingAt(TileA, TileB, BigTypeMotion.RingSquashAt);
        AssertRing(new BigTypeRing(334, 94, 208, 240, 50), squash);

        Assert.Equal(TileB, BigTypeMotion.RingAt(TileA, TileB, 1f));

        // Moving down, the squash turns: narrower across, shorter down.
        AssertRing(new BigTypeRing(94, 306, 240, 208, 50), BigTypeMotion.RingAt(TileA, TileBelow, BigTypeMotion.RingSquashAt));
    }

    [Fact]
    public void Ring_BetweenTargetsOfDifferentSizes_ChangesSizeAndRadius_WithNoSquash()
    {
        // A tile to a dock button: covers both, then is the dock button's ring by 74% (no squash), radius 50 → 38 on the way.
        Assert.Equal(TileA, BigTypeMotion.RingAt(TileA, Dock, 0f));
        AssertRing(new BigTypeRing(100, 100, 440, 940, 0), BigTypeMotion.RingAt(TileA, Dock, BigTypeMotion.RingCoversBothAt), radius: false);
        AssertRing(Dock with { Radius = 0 }, BigTypeMotion.RingAt(TileA, Dock, BigTypeMotion.RingSquashAt), radius: false);
        var landed = BigTypeMotion.RingAt(TileA, Dock, 1f);
        Assert.Equal(Dock, landed);
        var halfway = BigTypeMotion.RingAt(TileA, Dock, 0.5f).Radius;
        Assert.InRange(halfway, 38f, 50f);

        // A tile to a row of a sheet (radius 32): the same ring, widening, never a different highlight.
        Assert.Equal(Row, BigTypeMotion.RingAt(TileA, Row, 1f));
        Assert.False(BigTypeMotion.SameSize(TileA, Row));
    }

    [Fact]
    public void Ring_State_StartsFromWhereItWasDrawn_ThenBreathesAtRest_AndPopsOntoAnotherPage()
    {
        var motion = new BigTypeLauncherMotion();
        motion.Input(0f);
        motion.MoveRing(0f, instant: true, pop: false);
        Assert.Equal(TileA, motion.Ring(TileA, 0f, out _, out var moving));
        Assert.False(moving);

        // Moving on: at 38% of 300 ms it covers both.
        motion.MoveRing(100f, instant: false, pop: false);
        var both = motion.Ring(TileB, 100f + (0.38f * 300f), out _, out moving);
        Assert.True(moving);
        AssertRing(new BigTypeRing(100, 100, 452, 228, 0), both, radius: false);

        // Turned back half way: the new path starts from where the ring was drawn, not from the old tile.
        motion.MoveRing(214f, instant: false, pop: false);
        Assert.Equal(both, motion.Ring(TileA, 214f, out _, out _));

        // Landed, it breathes: 1.03 at half its 2.2 s period.
        var rest = motion.Ring(TileA, 514f, out _, out moving);
        Assert.False(moving);
        Assert.Equal(TileA, rest);
        var breath = motion.Ring(TileA, 514f + 1100f, out _, out _);
        Assert.Equal(TileA.Width * 1.03f, breath.Width, 2);
        Assert.Equal(TileA.CentreX, breath.CentreX, 2);

        // Onto another page: it lands at once and pops in from 1.25×.
        motion.MoveRing(3000f, instant: false, pop: true);
        var popped = motion.Ring(TileB, 3000f, out var opacity, out moving);
        Assert.True(moving);
        Assert.Equal(TileB.Width * BigTypeMotion.PopFromScale, popped.Width, 2);
        Assert.Equal(0f, opacity, 3);
        Assert.Equal(TileB, motion.Ring(TileB, 3000f + BigTypeMotion.PopMilliseconds, out opacity, out _));
        Assert.Equal(1f, opacity);
    }

    // ---- The lift and the neighbours ----

    [Fact]
    public void Neighbours_TheEightAroundTheFocusedTile_MoveEightAwayFromIt()
    {
        // Focused at column 2, row 1: each neighbour moves 8 along each axis it neighbours on.
        Assert.Equal((-8f, -8f), BigTypeMotion.Push(2, 1, 1, 0));
        Assert.Equal((0f, -8f), BigTypeMotion.Push(2, 1, 2, 0));
        Assert.Equal((8f, -8f), BigTypeMotion.Push(2, 1, 3, 0));
        Assert.Equal((-8f, 0f), BigTypeMotion.Push(2, 1, 1, 1));
        Assert.Equal((8f, 0f), BigTypeMotion.Push(2, 1, 3, 1));
        Assert.Equal((-8f, 8f), BigTypeMotion.Push(2, 1, 1, 2));
        Assert.Equal((0f, 8f), BigTypeMotion.Push(2, 1, 2, 2));
        Assert.Equal((8f, 8f), BigTypeMotion.Push(2, 1, 3, 2));
        // The focused tile itself and anything two steps away stay.
        Assert.Equal((0f, 0f), BigTypeMotion.Push(2, 1, 2, 1));
        Assert.Equal((0f, 0f), BigTypeMotion.Push(2, 1, 4, 1));
        Assert.Equal((0f, 0f), BigTypeMotion.Push(2, 1, 2, 3));
        var moved = Enumerable.Range(0, 5).SelectMany(c => Enumerable.Range(0, 4).Select(r => BigTypeMotion.Push(2, 1, c, r)))
            .Count(push => push != (0f, 0f));
        Assert.Equal(8, moved);
    }

    [Fact]
    public void Neighbours_State_MakeRoomOnTheLandingCurve_OnlyInTheirOwnGridAndPage_AndComeBack()
    {
        var motion = new BigTypeLauncherMotion();
        motion.Input(0f);
        motion.Place("h:0:6", new LauncherCell(LauncherGroup.Home, 0, 1, 1));
        motion.Place("h:0:7", new LauncherCell(LauncherGroup.Home, 0, 2, 1));
        motion.Place("h:1:6", new LauncherCell(LauncherGroup.Home, 1, 1, 1));
        motion.Place("f:4", new LauncherCell(LauncherGroup.Folder, 0, 1, 1));
        motion.Focus("h:0:7", 1000f, instant: false);

        Assert.Equal(Vector2.Zero, motion.Push("h:0:6", 1000f));
        Assert.True(motion.IsMoving(1100f, hops: false));
        Assert.Equal(new Vector2(-8f, 0f), motion.Push("h:0:6", 1000f + BigTypeMotion.LiftMilliseconds));
        Assert.False(motion.IsMoving(1000f + BigTypeMotion.LiftMilliseconds, hops: false));
        // Another page's tile, a folder's tile and the focused one do not move.
        Assert.Equal(Vector2.Zero, motion.Push("h:1:6", 2000f));
        Assert.Equal(Vector2.Zero, motion.Push("f:4", 2000f));
        Assert.Equal(Vector2.Zero, motion.Push("h:0:7", 2000f));

        // Focus leaves for the dock: they come back.
        motion.Focus("d:1", 3000f, instant: false);
        Assert.Equal(Vector2.Zero, motion.Push("h:0:6", 3000f + BigTypeMotion.LiftMilliseconds));
    }

    [Fact]
    public void Lift_LandsOnTheLandingCurve_AndTheFocusedTileBobsSixPixels()
    {
        Assert.Equal(1f, BigTypeMotion.Settle(1f, 1.1f, 0f));
        Assert.True(BigTypeMotion.Settle(1f, 1.1f, 200f) > 1.1f); // the curve overshoots on its way
        Assert.Equal(1.1f, BigTypeMotion.Settle(1f, 1.1f, BigTypeMotion.LiftMilliseconds));
        Assert.Equal(0f, BigTypeMotion.Bob(0f));
        Assert.Equal(-6f, BigTypeMotion.Bob(1500f), 3);
        Assert.Equal(0f, BigTypeMotion.Bob(3000f), 3);
    }

    // ---- Rolling digits ----

    [Fact]
    public void Roll_OnlyTheCharactersThatChange()
    {
        Assert.Equal(
            [BigTypeRollCell.Same, BigTypeRollCell.Changed, BigTypeRollCell.Same, BigTypeRollCell.Changed, BigTypeRollCell.Changed],
            BigTypeMotion.RollPlan("12:59", "13:00"));
        Assert.Equal(
            [BigTypeRollCell.Changed, BigTypeRollCell.Same, BigTypeRollCell.Same, BigTypeRollCell.Same, BigTypeRollCell.Same],
            BigTypeMotion.RollPlan("1 / 2", "2 / 2"));
        Assert.Equal([BigTypeRollCell.Changed, BigTypeRollCell.Arriving], BigTypeMotion.RollPlan("9", "10"));
        Assert.Equal([BigTypeRollCell.Changed, BigTypeRollCell.Leaving], BigTypeMotion.RollPlan("10", "9"));
        Assert.Equal(
            "8 h 40 min · 6 / 20".Length,
            BigTypeMotion.RollPlan("8 h 40 min · 6 / 20", "8 h 40 min · 7 / 20").Count(cell => cell == BigTypeRollCell.Same) + 1);
        Assert.False(BigTypeMotion.Rolls("12:59", "12:59"));
    }

    [Fact]
    public void Roll_OldLeavesUpward_NewRisesInOnTheLandingCurve_StaggeredPerCell()
    {
        // Cell 0: the old glyph is gone (1.1 em up) after 220 ms; the new one starts 1.1 em below and lands 400 ms after 50.
        Assert.Equal((0f, 1.1f, 1f), BigTypeMotion.Roll(BigTypeRollCell.Changed, 0, 0f));
        Assert.Equal(-1.1f, BigTypeMotion.Roll(BigTypeRollCell.Changed, 0, 220f).Old, 4);
        Assert.Equal(1.1f, BigTypeMotion.Roll(BigTypeRollCell.Changed, 0, 50f).New, 4);
        Assert.Equal(0f, BigTypeMotion.Roll(BigTypeRollCell.Changed, 0, 450f).New, 4);
        // Cell 3 starts 36 ms later.
        Assert.Equal(0f, BigTypeMotion.Roll(BigTypeRollCell.Changed, 3, 36f).Old, 4);
        Assert.True(BigTypeMotion.Roll(BigTypeRollCell.Changed, 3, 37f).Old < 0f);
        Assert.Equal(1.1f, BigTypeMotion.Roll(BigTypeRollCell.Arriving, 3, 86f).New, 4);
        // A cell that is the same never moves; one the new text lost fades in 140 ms.
        Assert.Equal((0f, 0f, 1f), BigTypeMotion.Roll(BigTypeRollCell.Same, 1, 100f));
        Assert.Equal(0f, BigTypeMotion.Roll(BigTypeRollCell.Leaving, 1, 140f).OldOpacity);
        Assert.Equal(36f + 50f + 400f, BigTypeMotion.RollMilliseconds(4));
    }

    [Fact]
    public void RollDraw_LeavesTheUnchangedPartsAlone_AndClipsEachChangedCellToItself()
    {
        // "1 / 2" → "2 / 2": five 20 px cells from x = 100, 40 tall, em 30.
        static StageText Cells(string text) => new(text.Select((c, i) => new StageTextCell(c, 100 + (i * 20), 500, 20, 40, 30)).ToList());
        var pieces = BigTypeRollDraw.Pieces(Cells("1 / 2"), Cells("2 / 2"), 96, 496, 108, 48, 100f)!;
        Assert.NotNull(pieces);
        // The new picture whole except the first cell's column: left of it (the picture's margin) and right of it.
        var left = Assert.Single(pieces, p => p.Source == BigTypeRollSource.New && p.OffsetY == 0f && p.ClipX == 96);
        Assert.Equal(4f, left.ClipWidth);
        var right = Assert.Single(pieces, p => p.Source == BigTypeRollSource.New && p.OffsetY == 0f && p.ClipX == 120);
        Assert.Equal(84f, right.ClipWidth);
        // The changed cell: the old "1" on its way up and the new "2" on its way in, each clipped to the cell.
        var old = Assert.Single(pieces, p => p.Source == BigTypeRollSource.Old);
        Assert.Equal((100f, 500f, 20f, 40f), (old.ClipX, old.ClipY, old.ClipWidth, old.ClipHeight));
        Assert.True(old.OffsetY < 0f);
        var arriving = Assert.Single(pieces, p => p.Source == BigTypeRollSource.New && p.OffsetY != 0f);
        Assert.Equal((100f, 20f), (arriving.ClipX, arriving.ClipWidth));
        Assert.True(arriving.OffsetY > 0f);
        // Over: nothing left to roll, the new picture is drawn as it is.
        Assert.Null(BigTypeRollDraw.Pieces(Cells("1 / 2"), Cells("2 / 2"), 96, 496, 108, 48, 500f));
        Assert.Null(BigTypeRollDraw.Pieces(Cells("2 / 2"), Cells("2 / 2"), 96, 496, 108, 48, 0f));
    }

    [Fact]
    public void AdjacentRollingCells_DoNotExposeAColumnOfTheNewPicture()
    {
        // Measured edges of "fo" in "a folder". The separately rounded end of f and start
        // of o are one float apart. A real 12 px space before the pair stays visible.
        var first = new StageTextCell('f', 370.65f, 0, 20.412f, 63, 42);
        var second = new StageTextCell('o', 391.062f, 0, 24.108f, 63, 42);
        Assert.Equal(MathF.BitIncrement(first.X + first.Width), second.X);
        var left = first.X - 12;
        var pieces = BigTypeRollDraw.Pieces(new StageText([]), new StageText([first, second]),
            left, 0, second.X + second.Width - left, 63, 0);
        var margin = Assert.Single(pieces!);
        Assert.Equal((BigTypeRollSource.New, left, 12f), (margin.Source, margin.ClipX, margin.ClipWidth));
    }

    // ---- The idle hop ----

    [Fact]
    public void Hop_After4800ms_ThenEvery6500ms_UntilTheMinute_StaggeredAlongTheDiagonal()
    {
        Assert.Null(BigTypeMotion.HopStart(4799f, holdStill: false));
        Assert.Equal(4800f, BigTypeMotion.HopStart(5000f, holdStill: false));
        Assert.Equal(11300f, BigTypeMotion.HopStart(12000f, holdStill: false));
        // The last one starts before the resting motion stops (60 s); none after.
        Assert.Equal(56800f, BigTypeMotion.HopStart(70000f, holdStill: false));
        Assert.Equal(4800f, BigTypeMotion.UntilNextHop(0f, holdStill: false));
        Assert.Null(BigTypeMotion.UntilNextHop(57000f, holdStill: false));

        // The diagonal: (column mod 8) + row, 75 ms a step; each tile rises 20 px at the middle of its 560 ms.
        Assert.Equal(0, BigTypeMotion.HopIndex(0, 0));
        Assert.Equal(3, BigTypeMotion.HopIndex(2, 1));
        Assert.Equal(2, BigTypeMotion.HopIndex(9, 1));
        var highest = Enumerable.Range(0, 561).Min(t => BigTypeMotion.Hop(0, 0, t));
        Assert.Equal(-20f, highest, 1);
        Assert.Equal(0f, BigTypeMotion.Hop(2, 1, 225f));
        Assert.Equal(BigTypeMotion.Hop(0, 0, 200f), BigTypeMotion.Hop(2, 1, 225f + 200f), 4);
        Assert.Equal(0f, BigTypeMotion.Hop(0, 0, 560f));

        // Through the Launcher's state: home's tiles on the page in view hop; another page's and a folder's do not.
        var motion = new BigTypeLauncherMotion();
        motion.Input(0f);
        motion.Place("h:0:0", new LauncherCell(LauncherGroup.Home, 0, 0, 0));
        motion.Place("h:1:0", new LauncherCell(LauncherGroup.Home, 1, 0, 0));
        motion.Place("f:0", new LauncherCell(LauncherGroup.Folder, 0, 0, 0));
        Assert.Equal(BigTypeMotion.Hop(0, 0, 280f), motion.Offset("h:0:0", 4800f + 280f).Y, 3);
        Assert.True(motion.Offset("h:0:0", 4800f + 280f).Y < 0f);
        Assert.Equal(0f, motion.Offset("h:1:0", 4800f + 280f).Y);
        Assert.Equal(0f, motion.Offset("f:0", 4800f + 280f).Y);
        Assert.True(motion.IsMoving(4800f + 280f));
        Assert.Equal(0f, motion.Offset("h:0:0", 4800f + 280f, hops: false).Y);
    }

    // ---- At rest, and holding still ----

    [Fact]
    public void AtRest_TheBreathAndTheBobStepAt30Hz_ForAMinute_ThenNothingIsDrawn()
    {
        var motion = new BigTypeLauncherMotion();
        motion.Input(0f);
        Assert.Equal(1000f / 30f, motion.NextFrameMilliseconds(30_000f));
        Assert.Equal(1f, motion.Alive(60_000f));
        Assert.InRange(motion.Alive(61_000f), 0.01f, 0.99f);
        Assert.Null(motion.NextFrameMilliseconds(62_001f));
        Assert.Equal(1f, BigTypeMotion.Breath(1100f, alive: 0f));
        Assert.Equal(0f, BigTypeMotion.Bob(1500f, alive: 0f));
    }

    [Fact]
    public void HoldingStill_NothingMoves()
    {
        var motion = new BigTypeLauncherMotion { HoldStill = true };
        motion.Input(0f);
        motion.Place("h:0:6", new LauncherCell(LauncherGroup.Home, 0, 1, 1));
        motion.Place("h:0:7", new LauncherCell(LauncherGroup.Home, 0, 2, 1));

        // The ring jumps: no path, no pop, no breath.
        motion.MoveRing(0f, instant: true, pop: false);
        motion.Ring(TileA, 0f, out _, out _);
        motion.MoveRing(100f, instant: false, pop: false);
        Assert.Equal(TileB, motion.Ring(TileB, 100f, out var opacity, out var moving));
        Assert.False(moving);
        motion.MoveRing(200f, instant: false, pop: true);
        Assert.Equal(TileA, motion.Ring(TileA, 200f, out opacity, out moving));
        Assert.Equal(1f, opacity);
        Assert.False(moving);
        Assert.Equal(TileA, motion.Ring(TileA, 1300f, out _, out _));

        // The neighbours are where they go at once; the focused tile does not bob; nothing hops.
        motion.Focus("h:0:7", 300f, instant: false);
        Assert.Equal(new Vector2(-8f, 0f), motion.Push("h:0:6", 300f));
        Assert.False(motion.IsMoving(300f));
        Assert.Equal(Vector2.Zero, motion.Offset("h:0:7", 300f + 1500f));
        Assert.Equal(new Vector2(-8f, 0f), motion.Offset("h:0:6", 4800f + 280f));
        Assert.Null(BigTypeMotion.HopStart(5080f, holdStill: true));

        // Nothing breathes, so nothing asks for a frame; digits swap.
        Assert.Equal(0f, motion.Alive(1000f));
        Assert.Null(motion.NextFrameMilliseconds(1000f));
        Assert.False(BigTypeMotion.Rolls("12:59", "13:00", holdStill: true));
    }

    private static void AssertRing(BigTypeRing expected, BigTypeRing actual, bool radius = true)
    {
        Assert.Equal(expected.X, actual.X, 2);
        Assert.Equal(expected.Y, actual.Y, 2);
        Assert.Equal(expected.Width, actual.Width, 2);
        Assert.Equal(expected.Height, actual.Height, 2);
        if (radius)
            Assert.Equal(expected.Radius, actual.Radius, 2);
    }
}
