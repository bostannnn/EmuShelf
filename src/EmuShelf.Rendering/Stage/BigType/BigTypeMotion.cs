namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>A rectangle the Big Type ring frames, on the canvas, with its corner radius.</summary>
public readonly record struct BigTypeRing(float X, float Y, float Width, float Height, float Radius)
{
    public float CentreX => X + (Width / 2f);
    public float CentreY => Y + (Height / 2f);

    public static BigTypeRing Lerp(BigTypeRing a, BigTypeRing b, float t) => new(
        a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t), a.Width + ((b.Width - a.Width) * t),
        a.Height + ((b.Height - a.Height) * t), a.Radius + ((b.Radius - a.Radius) * t));

    /// <summary>The same ring grown (or shrunk) about its own centre, its radius with it.</summary>
    public BigTypeRing Scaled(float scale) => scale == 1f ? this : new(
        CentreX - (Width * scale / 2f), CentreY - (Height * scale / 2f), Width * scale, Height * scale, Radius * scale);
}

/// <summary>What a cell of a rolling text does when its text changes (the mock's <c>roll</c>).</summary>
public enum BigTypeRollCell
{
    /// <summary>The same character as before: it does not move.</summary>
    Same,

    /// <summary>A different character: the old one leaves upward and the new one rises in.</summary>
    Changed,

    /// <summary>A cell the old text did not have: the new character rises in.</summary>
    Arriving,

    /// <summary>A cell the new text does not have: the old character fades where it is.</summary>
    Leaving,
}

/// <summary>
/// The Big Type look's motion (M47 B5 and B6): the ring's stretch, its breath, the lift and the neighbours making room, the bob,
/// rolling digits and the idle hop; the name's letters, the flood and the wipe. The numbers are tokens.js <c>motion</c> and
/// <c>shape</c> at Thor resolution; the shapes of the curves are the mock's script (docs/prototypes/handheld-big-type).
/// Everything here is a pure function of time, shared by the native compositors and the Avalonia Launcher.
/// </summary>
public static class BigTypeMotion
{
    // ---- The ring (move 4, move 11) ----

    /// <summary>tokens.js <c>Ring.ms</c>.</summary>
    public const float RingMilliseconds = 300f;

    /// <summary>tokens.js <c>Ring.coversBothAt</c>: where the ring covers both the old and the new target.</summary>
    public const float RingCoversBothAt = 0.38f;

    /// <summary>The mock's script: where the squash is deepest before the ring settles.</summary>
    public const float RingSquashAt = 0.74f;

    /// <summary>The squash at Thor scale (the mock's 5 and 3 px at half scale): shorter along the travel, longer across it.</summary>
    public const float RingSquashAlong = 10f;
    public const float RingSquashAcross = 6f;

    /// <summary>Two targets this close in size count as the same size (the mock compares exact pixels).</summary>
    public const float SameSizeTolerance = 0.5f;

    /// <summary>tokens.js <c>RingBreath</c>: 1 → 1.03 → 1 over 2.2 s.</summary>
    public const float BreathPeriodMilliseconds = 2200f;
    public const float BreathScale = 1.03f;

    /// <summary>The mock's pop when the ring arrives on another page: from 1.25× and clear, 320 ms on the landing curve.</summary>
    public const float PopMilliseconds = 320f;
    public const float PopFromScale = 1.25f;

    // ---- The lift (move 4) ----

    /// <summary>tokens.js <c>FocusLift.ms</c>, on the landing curve; neighbours move on the same transition.</summary>
    public const float LiftMilliseconds = 380f;

    /// <summary>tokens.js <c>shape.NeighbourPush</c>: how far each of the eight tiles around the focused one moves away.</summary>
    public const float NeighbourPush = 8f;

    /// <summary>The mock's bob at Thor scale (3 px there): the focused tile rises 6 px and back, 1.5 s each way.</summary>
    public const float BobRise = 6f;
    public const float BobPeriodMilliseconds = 3000f;

    // ---- Rolling digits (move 5) ----

    /// <summary>tokens.js <c>DigitOut</c>: the old character leaves upward 1.1 em in 220 ms (CSS ease-in).</summary>
    public const float DigitOutMilliseconds = 220f;

    /// <summary>tokens.js <c>DigitIn</c>: the new one rises 1.1 em in 400 ms on the landing curve, after 50 ms.</summary>
    public const float DigitInMilliseconds = 400f;
    public const float DigitInDelay = 50f;
    public const float DigitTravelEm = 1.1f;

    /// <summary>tokens.js <c>stagger</c>: 12 ms per cell, at most 240 ms (the mock's script).</summary>
    public const float DigitStagger = 12f;
    public const float DigitStaggerMax = 240f;

    /// <summary>The mock's script: a cell the new text no longer has fades in 140 ms.</summary>
    public const float DigitFadeMilliseconds = 140f;

    // ---- The idle hop (move 8) ----

    /// <summary>tokens.js <c>IdleHop</c>.</summary>
    public const float HopAfterMilliseconds = 4800f;
    public const float HopEveryMilliseconds = 6500f;
    public const float HopMilliseconds = 560f;
    public const float HopStagger = 75f;
    public const float HopRise = 20f;

    /// <summary>The mock's script: the diagonal index is the column modulo 8 plus the row.</summary>
    public const int HopColumnWrap = 8;

    // ---- At rest ----

    /// <summary>
    /// What stays alive at rest (the breath and the bob) steps at 30 Hz, as the backdrop does; anything else moving draws at
    /// the panel's rate.
    /// </summary>
    public const float AliveStepMilliseconds = 1000f / 30f;

    // ---- Curves ----

    /// <summary>tokens.js <c>Land</c>, cubic-bezier(.2, 1.5, .35, 1): anything arriving.</summary>
    public static float Land(float t) => Curve(0.2f, 1.5f, 0.35f, 1f, t);

    /// <summary>The mock's ring keyframes, cubic-bezier(.3, 0, .4, 1).</summary>
    public static float RingEase(float t) => Curve(0.3f, 0f, 0.4f, 1f, t);

    /// <summary>CSS <c>ease</c>, cubic-bezier(.25, .1, .25, 1): the ring's corner radius on its way.</summary>
    public static float CssEase(float t) => Curve(0.25f, 0.1f, 0.25f, 1f, t);

    /// <summary>CSS <c>ease-in</c>, cubic-bezier(.42, 0, 1, 1): a digit leaving.</summary>
    public static float EaseIn(float t) => Curve(0.42f, 0f, 1f, 1f, t);

    /// <summary>CSS <c>ease-in-out</c>, cubic-bezier(.42, 0, .58, 1): the breath and the bob.</summary>
    public static float EaseInOut(float t) => Curve(0.42f, 0f, 0.58f, 1f, t);

    /// <summary>The mock's hop, cubic-bezier(.3, 0, .3, 1).</summary>
    public static float HopEase(float t) => Curve(0.3f, 0f, 0.3f, 1f, t);

    // A CSS cubic-bezier that starts at exactly 0 and ends at exactly 1 (the solver is a hair off at its ends).
    private static float Curve(float x1, float y1, float x2, float y2, float t) =>
        t <= 0f ? 0f : t >= 1f ? 1f : StageCurves.CubicBezier(x1, y1, x2, y2, t);

    // ---- The ring's path ----

    /// <summary>Whether two targets are the same size, so the ring lands with a squash.</summary>
    public static bool SameSize(BigTypeRing a, BigTypeRing b) =>
        Math.Abs(a.Width - b.Width) <= SameSizeTolerance && Math.Abs(a.Height - b.Height) <= SameSizeTolerance;

    /// <summary>The smallest rectangle covering both targets: where the ring is at <see cref="RingCoversBothAt"/>.</summary>
    public static BigTypeRing Union(BigTypeRing a, BigTypeRing b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        var right = Math.Max(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new BigTypeRing(left, top, right - left, bottom - top, b.Radius);
    }

    /// <summary>
    /// Where the ring is at <see cref="RingSquashAt"/>: on a target of the same size it is squashed across its direction of
    /// travel (shorter along it, longer across it); otherwise it is already on the new target.
    /// </summary>
    public static BigTypeRing Squash(BigTypeRing from, BigTypeRing to)
    {
        if (!SameSize(from, to))
            return to;
        var horizontal = Math.Abs(to.CentreX - from.CentreX) >= Math.Abs(to.CentreY - from.CentreY);
        return horizontal
            ? new BigTypeRing(to.X + RingSquashAlong, to.Y - RingSquashAcross, to.Width - (2 * RingSquashAlong), to.Height + (2 * RingSquashAcross), to.Radius)
            : new BigTypeRing(to.X - RingSquashAcross, to.Y + RingSquashAlong, to.Width + (2 * RingSquashAcross), to.Height - (2 * RingSquashAlong), to.Radius);
    }

    /// <summary>
    /// The ring <paramref name="progress"/> (0–1) of the way from <paramref name="from"/> to <paramref name="to"/>: it grows to
    /// cover both by 38%, lets go of the old one and lands by 74% (squashed when the two are the same size), then settles. Its
    /// corner radius changes over the whole move (the mock's border-radius transition).
    /// </summary>
    public static BigTypeRing RingAt(BigTypeRing from, BigTypeRing to, float progress)
    {
        var t = Math.Clamp(progress, 0f, 1f);
        if (t >= 1f)
            return to;
        var union = Union(from, to);
        var squash = Squash(from, to);
        BigTypeRing at;
        if (t <= RingCoversBothAt)
            at = BigTypeRing.Lerp(from, union, RingEase(t / RingCoversBothAt));
        else if (t <= RingSquashAt)
            at = BigTypeRing.Lerp(union, squash, RingEase((t - RingCoversBothAt) / (RingSquashAt - RingCoversBothAt)));
        else
            at = BigTypeRing.Lerp(squash, to, (t - RingSquashAt) / (1f - RingSquashAt));
        return at with { Radius = from.Radius + ((to.Radius - from.Radius) * CssEase(t)) };
    }

    /// <summary>The breath's scale <paramref name="sinceLanded"/> ms after the ring came to rest, at <paramref name="alive"/> strength.</summary>
    public static float Breath(float sinceLanded, float alive = 1f) =>
        1f + ((BreathScale - 1f) * alive * Swing(sinceLanded, BreathPeriodMilliseconds));

    /// <summary>The ring's pop on arriving at another page: its scale and opacity <paramref name="since"/> ms in.</summary>
    public static (float Scale, float Opacity) Pop(float since) => Pop(since, PopFromScale);

    /// <summary>The ring's pop from <paramref name="from"/> times its size (M47 B8: a folder's first game pops it from 1.3).</summary>
    public static (float Scale, float Opacity) Pop(float since, float from)
    {
        if (since >= PopMilliseconds)
            return (1f, 1f);
        var e = Land(Math.Max(0f, since) / PopMilliseconds);
        return (from + ((1f - from) * e), Math.Clamp(e, 0f, 1f));
    }

    // ---- The lift, the neighbours and the bob ----

    /// <summary>Where a lift (or a push) is <paramref name="since"/> ms after it started from <paramref name="from"/> toward <paramref name="to"/>.</summary>
    public static float Settle(float from, float to, float since) =>
        since >= LiftMilliseconds ? to : from + ((to - from) * Land(Math.Max(0f, since) / LiftMilliseconds));

    /// <summary>
    /// How far a tile at (<paramref name="column"/>, <paramref name="row"/>) moves away from the focused one at
    /// (<paramref name="focusColumn"/>, <paramref name="focusRow"/>): <see cref="NeighbourPush"/> along each axis it is a
    /// neighbour on (so a diagonal neighbour moves along both), nothing for the focused tile or one further away.
    /// </summary>
    public static (float X, float Y) Push(int focusColumn, int focusRow, int column, int row)
    {
        var dc = column - focusColumn;
        var dr = row - focusRow;
        if ((dc == 0 && dr == 0) || Math.Abs(dc) > 1 || Math.Abs(dr) > 1)
            return (0f, 0f);
        return (dc * NeighbourPush, dr * NeighbourPush);
    }

    /// <summary>The focused tile's bob (negative is up) <paramref name="sinceFocused"/> ms after it took the focus.</summary>
    public static float Bob(float sinceFocused, float alive = 1f) => -BobRise * alive * Swing(sinceFocused, BobPeriodMilliseconds);

    /// <summary>
    /// How much of the resting motion (the breath, the bob) is alive <paramref name="sinceInput"/> ms after the last press:
    /// all of it for a minute, then it eases to nothing over two seconds, as the Stage's backdrop rests (StageRest). Nothing
    /// while the device asks for stillness.
    /// </summary>
    public static float Alive(float sinceInput, bool holdStill)
    {
        if (holdStill || float.IsNaN(sinceInput))
            return 0f;
        if (sinceInput <= StageRest.AfterMilliseconds)
            return 1f;
        var t = (sinceInput - StageRest.AfterMilliseconds) / StageRest.SlowMilliseconds;
        return t >= 1f ? 0f : StageRest.Eased(1f - t);
    }

    // CSS `ease-in-out infinite alternate`: 0 → 1 over half the period and back.
    private static float Swing(float since, float period)
    {
        if (since <= 0f)
            return 0f;
        var half = period / 2f;
        var cycle = since % period;
        return cycle < half ? EaseInOut(cycle / half) : EaseInOut(1f - ((cycle - half) / half));
    }

    // ---- The idle hop ----

    /// <summary>
    /// When the latest hop began, in ms after the last press, at <paramref name="sinceInput"/>: the first after 4.8 s, then one
    /// every 6.5 s while the resting motion is alive (none starts after the minute); null before the first or while holding still.
    /// </summary>
    public static float? HopStart(float sinceInput, bool holdStill)
    {
        if (holdStill || float.IsNaN(sinceInput) || sinceInput < HopAfterMilliseconds)
            return null;
        var last = (int)MathF.Floor((StageRest.AfterMilliseconds - HopAfterMilliseconds) / HopEveryMilliseconds);
        var k = Math.Min((int)MathF.Floor((sinceInput - HopAfterMilliseconds) / HopEveryMilliseconds), last);
        return HopAfterMilliseconds + (k * HopEveryMilliseconds);
    }

    /// <summary>The ms from <paramref name="sinceInput"/> to the next hop's start; null when no further hop will start.</summary>
    public static float? UntilNextHop(float sinceInput, bool holdStill)
    {
        if (holdStill || float.IsNaN(sinceInput))
            return null;
        if (sinceInput < HopAfterMilliseconds)
            return HopAfterMilliseconds - sinceInput;
        var next = HopStart(sinceInput, false)!.Value + HopEveryMilliseconds;
        return next > StageRest.AfterMilliseconds ? null : next - sinceInput;
    }

    /// <summary>A tile's place along the hop's diagonal: the mock's (column modulo 8) + row.</summary>
    public static int HopIndex(int column, int row) => (Math.Max(0, column) % HopColumnWrap) + Math.Max(0, row);

    /// <summary>A tile's hop (negative is up) <paramref name="sinceHop"/> ms after the wave began.</summary>
    public static float Hop(int column, int row, float sinceHop)
    {
        var t = (sinceHop - (HopIndex(column, row) * HopStagger)) / HopMilliseconds;
        if (t <= 0f || t >= 1f)
            return 0f;
        var p = HopEase(t);
        return -HopRise * (p < 0.5f ? 2f * p : 2f - (2f * p));
    }

    /// <summary>How long a wave runs over tiles up to diagonal index <paramref name="lastIndex"/>.</summary>
    public static float HopWaveMilliseconds(int lastIndex) => (Math.Max(0, lastIndex) * HopStagger) + HopMilliseconds;

    // ---- Rolling digits ----

    /// <summary>
    /// Which cells of a text roll when it changes from <paramref name="old"/> to <paramref name="next"/>, character by
    /// character from the left as the mock's <c>roll</c> does: a character that is the same stays; a different one rolls; a
    /// cell only the new text has rises in; one only the old text has fades.
    /// </summary>
    public static BigTypeRollCell[] RollPlan(string? old, string? next)
    {
        old ??= string.Empty;
        next ??= string.Empty;
        var cells = new BigTypeRollCell[Math.Max(old.Length, next.Length)];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = i >= next.Length ? BigTypeRollCell.Leaving
                : i >= old.Length ? BigTypeRollCell.Arriving
                : old[i] == next[i] ? BigTypeRollCell.Same
                : BigTypeRollCell.Changed;
        }
        return cells;
    }

    /// <summary>Whether a text change rolls at all (some cell is not the same); held still, it simply swaps.</summary>
    public static bool Rolls(string? old, string? next, bool holdStill = false) =>
        !holdStill && !string.Equals(old ?? string.Empty, next ?? string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Cell <paramref name="index"/> of a roll <paramref name="since"/> ms in: the old character's and the new one's offsets in
    /// ems (negative is up; 0 is in the cell) and the old one's opacity. A cell that is the same never moves.
    /// </summary>
    public static (float Old, float New, float OldOpacity) Roll(BigTypeRollCell cell, int index, float since)
    {
        if (cell == BigTypeRollCell.Same)
            return (0f, 0f, 1f);
        var stagger = Math.Min(index * DigitStagger, DigitStaggerMax);
        if (cell == BigTypeRollCell.Leaving)
            return (0f, 0f, 1f - Math.Clamp(since / DigitFadeMilliseconds, 0f, 1f));
        var leave = Math.Clamp((since - stagger) / DigitOutMilliseconds, 0f, 1f);
        var land = Math.Clamp((since - DigitInDelay - stagger) / DigitInMilliseconds, 0f, 1f);
        return (-DigitTravelEm * EaseIn(leave), DigitTravelEm * (1f - Land(land)), 1f);
    }

    /// <summary>How long a roll over <paramref name="cells"/> cells runs.</summary>
    public static float RollMilliseconds(int cells) =>
        Math.Max(DigitFadeMilliseconds, Math.Min(Math.Max(0, cells - 1) * DigitStagger, DigitStaggerMax) + DigitInDelay + DigitInMilliseconds);

    // ---- The name's letters (move 2, M47 B6) ----

    /// <summary>tokens.js <c>LetterIn</c>: each letter lands in 540 ms on the landing curve, after 90 ms.</summary>
    public const float LetterInMilliseconds = 540f;
    public const float LetterInDelay = 90f;

    /// <summary>tokens.js <c>LetterIn</c>: up to 26 ms a letter, 300 ms across the name at most.</summary>
    public const float LetterInStaggerMax = 26f;
    public const float LetterInStaggerTotal = 300f;

    /// <summary>tokens.js <c>LetterIn</c> (the mock's ENTER): where a letter starts, in ems of the name's size, and its tilt in degrees.</summary>
    public const float LetterInFromEm = 0.55f;
    public const float LetterInFromEmVertical = 0.5f;
    public const float LetterInOpenEm = 0.95f;
    public const float LetterInOpenTilt = 7f;
    public const float LetterInBackEm = -0.7f;
    public const float LetterInBackTilt = -5f;
    public const float LetterInRiseEm = 0.7f;
    public const float LetterInRiseTilt = 5f;

    /// <summary>tokens.js <c>LetterOut</c>: each old letter leaves in 170 ms on the leaving curve, up to 12 ms apart (110 in all).</summary>
    public const float LetterOutMilliseconds = 170f;
    public const float LetterOutStaggerMax = 12f;
    public const float LetterOutStaggerTotal = 110f;

    /// <summary>tokens.js <c>LetterOut</c> (the mock's EXIT): where a leaving letter goes, in ems.</summary>
    public const float LetterOutToEm = 0.45f;
    public const float LetterOutOpenEm = -0.6f;
    public const float LetterOutBackEm = 0.6f;
    public const float LetterOutRiseEm = -0.4f;

    /// <summary>tokens.js <c>LetterOut.stillMs</c>: held still, the old name goes in 60 ms, all at once (the mock's reduced motion).</summary>
    public const float LetterOutStillMilliseconds = 60f;

    /// <summary>tokens.js <c>Leave</c>, cubic-bezier(.5, 0, .9, .5): anything going.</summary>
    public static float Leave(float t) => Curve(0.5f, 0f, 0.9f, 0.5f, t);

    /// <summary>The arriving letters' stagger for a name of <paramref name="count"/> letters: up to 26 ms each, 300 ms in all.</summary>
    public static float LetterInStagger(int count) => Math.Min(LetterInStaggerMax, LetterInStaggerTotal / Math.Max(1, count));

    /// <summary>The leaving letters' stagger: up to 12 ms each, 110 ms in all.</summary>
    public static float LetterOutStagger(int count) => Math.Min(LetterOutStaggerMax, LetterOutStaggerTotal / Math.Max(1, count));

    /// <summary>Where an arriving letter starts (ems; y down) and its tilt (degrees, clockwise), the mock's <c>ENTER</c>.</summary>
    public static (float X, float Y, float Degrees) LetterFrom(BigTypeEntrance entrance) => entrance switch
    {
        BigTypeEntrance.Right => (LetterInFromEm, 0f, 0f),
        BigTypeEntrance.Left => (-LetterInFromEm, 0f, 0f),
        BigTypeEntrance.Up => (0f, -LetterInFromEmVertical, 0f),
        BigTypeEntrance.Down => (0f, LetterInFromEmVertical, 0f),
        BigTypeEntrance.Open => (0f, LetterInOpenEm, LetterInOpenTilt),
        BigTypeEntrance.Back => (0f, LetterInBackEm, LetterInBackTilt),
        _ => (0f, LetterInRiseEm, LetterInRiseTilt),
    };

    /// <summary>Where a leaving letter goes (ems), the mock's <c>EXIT</c>: the other way from the arrival.</summary>
    public static (float X, float Y) LetterTo(BigTypeEntrance entrance) => entrance switch
    {
        BigTypeEntrance.Right => (-LetterOutToEm, 0f),
        BigTypeEntrance.Left => (LetterOutToEm, 0f),
        BigTypeEntrance.Up => (0f, LetterOutToEm),
        BigTypeEntrance.Down => (0f, -LetterOutToEm),
        BigTypeEntrance.Open => (0f, LetterOutOpenEm),
        BigTypeEntrance.Back => (0f, LetterOutBackEm),
        _ => (0f, LetterOutRiseEm),
    };

    /// <summary>
    /// Letter <paramref name="order"/> (reading order) of a name of <paramref name="count"/> letters, <paramref name="since"/> ms after
    /// the name came: its offset in ems, tilt and opacity (the mock's <c>setWord</c>: from <see cref="LetterFrom"/> and clear, onto its
    /// place on the landing curve, 90 ms plus its stagger late; moving left the last letter leads). Before its turn it is not drawn.
    /// Held still the name is simply there.
    /// </summary>
    public static BigTypeLetterPose Arrive(BigTypeEntrance entrance, int order, int count, float since, bool holdStill = false)
    {
        if (holdStill)
            return BigTypeLetterPose.Rest;
        var index = entrance == BigTypeEntrance.Left ? count - 1 - order : order;
        var t = (since - LetterInDelay - (index * LetterInStagger(count))) / LetterInMilliseconds;
        var (x, y, degrees) = LetterFrom(entrance);
        if (t <= 0f)
            return new BigTypeLetterPose(x, y, degrees, 0f);
        if (t >= 1f)
            return BigTypeLetterPose.Rest;
        var e = Land(t);
        var k = 1f - e;
        return new BigTypeLetterPose(x * k, y * k, degrees * k, Math.Clamp(e, 0f, 1f));
    }

    /// <summary>How long a name of <paramref name="count"/> letters takes to land (0 held still).</summary>
    public static float ArrivalMilliseconds(int count, bool holdStill = false) => holdStill ? 0f :
        LetterInDelay + (Math.Max(0, count - 1) * LetterInStagger(count)) + LetterInMilliseconds;

    /// <summary>
    /// Leaving letter <paramref name="order"/> <paramref name="since"/> ms after the next name came: its offset in ems and opacity on
    /// the leaving curve toward <see cref="LetterTo"/>, staggered in reading order; null once it has gone. Held still, every letter goes
    /// together in <see cref="LetterOutStillMilliseconds"/>.
    /// </summary>
    public static BigTypeLetterPose? Depart(BigTypeEntrance entrance, int order, int count, float since, bool holdStill = false)
    {
        var duration = holdStill ? LetterOutStillMilliseconds : LetterOutMilliseconds;
        var delay = holdStill ? 0f : order * LetterOutStagger(count);
        var t = (since - delay) / duration;
        if (t >= 1f)
            return null;
        var e = Leave(t);
        var (x, y) = LetterTo(entrance);
        return new BigTypeLetterPose(x * e, y * e, 0f, 1f - e);
    }

    /// <summary>How long a name of <paramref name="count"/> letters takes to leave.</summary>
    public static float DepartureMilliseconds(int count, bool holdStill = false) => holdStill ? LetterOutStillMilliseconds :
        (Math.Max(0, count - 1) * LetterOutStagger(count)) + LetterOutMilliseconds;

    // ---- A run of names (M47 B11, the app's own) ----

    /// <summary>
    /// tokens.js <c>LetterRun.withinMs</c>: a press that comes this soon after the one before is part of a run (quick taps; a held
    /// direction keeps the app's once-per-move rule, the Stage waiting until the focus rests). The name a run's first press brings lands
    /// as the mock's does; each after it follows in a short form, so quick taps never queue a full landing per tile: the name there goes at once (<see cref="RunOutMilliseconds"/>, all letters together) and the
    /// new one's letters come in together from <see cref="RunFromShare"/> of the mock's distance over <see cref="RunInMilliseconds"/> on
    /// the landing curve; rolling texts swap in place. The name the run stops on is already in place; nothing lands again.
    /// </summary>
    public const float RunWithinMilliseconds = 220f;

    /// <summary>tokens.js <c>LetterRun.ms</c>: a name in a run arrives in this long, all its letters together.</summary>
    public const float RunInMilliseconds = 200f;

    /// <summary>tokens.js <c>LetterRun.fromShare</c>: of the mock's distance (and tilt) a name in a run comes from.</summary>
    public const float RunFromShare = 0.4f;

    /// <summary>tokens.js <c>LetterRun.outMs</c>: the name a run's next one replaces goes in this long, all its letters together.</summary>
    public const float RunOutMilliseconds = 90f;

    /// <summary>Whether a press <paramref name="sinceLast"/> ms after the one before is part of a run (NaN: there was none before).</summary>
    public static bool InRun(float sinceLast) => sinceLast >= 0f && sinceLast < RunWithinMilliseconds;

    /// <summary>A letter of a name in a run, <paramref name="since"/> ms after it came: every letter together, from a short way off.</summary>
    public static BigTypeLetterPose ArriveInRun(BigTypeEntrance entrance, float since, bool holdStill = false)
    {
        if (holdStill)
            return BigTypeLetterPose.Rest;
        var t = since / RunInMilliseconds;
        if (t >= 1f)
            return BigTypeLetterPose.Rest;
        var (x, y, degrees) = LetterFrom(entrance);
        var e = Land(Math.Max(0f, t));
        var k = RunFromShare * (1f - e);
        return new BigTypeLetterPose(x * k, y * k, degrees * k, Math.Clamp(e, 0f, 1f));
    }

    /// <summary>A letter of a name a run's next one replaces, <paramref name="since"/> ms after: all together, the short way; null once gone.</summary>
    public static BigTypeLetterPose? DepartInRun(BigTypeEntrance entrance, float since, bool holdStill = false)
    {
        var duration = holdStill ? LetterOutStillMilliseconds : RunOutMilliseconds;
        var t = since / duration;
        if (t >= 1f)
            return null;
        var e = Leave(Math.Max(0f, t));
        var (x, y) = LetterTo(entrance);
        return new BigTypeLetterPose(x * e * RunFromShare, y * e * RunFromShare, 0f, 1f - e);
    }

    /// <summary>How long a name in a run takes to arrive (0 held still) and to go.</summary>
    public static float RunArrivalMilliseconds(bool holdStill = false) => holdStill ? 0f : RunInMilliseconds;

    public static float RunDepartureMilliseconds(bool holdStill = false) => holdStill ? LetterOutStillMilliseconds : RunOutMilliseconds;

    // ---- The letters' idle hop (move 8) ----

    /// <summary>tokens.js <c>IdleHop</c>: the name's letters hop 260 ms after the tiles' wave begins, 45 ms apart, 0.07 em high.</summary>
    public const float LetterHopDelay = 260f;
    public const float LetterHopStagger = 45f;
    public const float LetterHopRiseEm = 0.07f;

    /// <summary>Letter <paramref name="order"/>'s hop in ems (negative is up) <paramref name="sinceHop"/> ms after the wave began.</summary>
    public static float LetterHop(int order, float sinceHop)
    {
        var t = (sinceHop - LetterHopDelay - (order * LetterHopStagger)) / HopMilliseconds;
        if (t <= 0f || t >= 1f)
            return 0f;
        var p = HopEase(t);
        return -LetterHopRiseEm * (p < 0.5f ? 2f * p : 2f - (2f * p));
    }

    /// <summary>How long the letters' part of a wave runs for a name of <paramref name="count"/> letters.</summary>
    public static float LetterHopWaveMilliseconds(int count) => LetterHopDelay + (Math.Max(0, count - 1) * LetterHopStagger) + HopMilliseconds;

    // ---- The flood and the wipe (move 3) ----

    /// <summary>tokens.js <c>Flood</c>: the Launcher's new ground grows as a circle from the focused tile to 1640 px in 640 ms.</summary>
    public const float FloodMilliseconds = 640f;
    public const float FloodRadius = 1640f;

    /// <summary>tokens.js <c>Wipe</c>: the Stage's new backdrop grows as a circle to 2300 px in 760 ms, 110 ms after the flood, from
    /// <c>below</c> px under the Stage's bottom edge at the focused tile's x.</summary>
    public const float WipeMilliseconds = 760f;
    public const float WipeDelay = 110f;
    public const float WipeRadius = 2300f;
    public const float WipeBelow = 40f;

    /// <summary>tokens.js <c>Flood.easing</c> and <c>Wipe.easing</c>, cubic-bezier(.2, .6, .2, 1).</summary>
    public static float FloodEase(float t) => Curve(0.2f, 0.6f, 0.2f, 1f, t);

    /// <summary>The flood's radius <paramref name="since"/> ms after it began; held still it covers at once.</summary>
    public static float FloodRadiusAt(float since, bool holdStill = false) =>
        holdStill || since >= FloodMilliseconds ? FloodRadius : FloodRadius * FloodEase(Math.Max(0f, since) / FloodMilliseconds);

    /// <summary>Whether a flood that began <paramref name="since"/> ms ago still grows.</summary>
    public static bool Flooding(float since, bool holdStill = false) => !holdStill && since < FloodMilliseconds;

    /// <summary>The wipe's radius <paramref name="sinceGrowing"/> ms after its circle began to grow (its delay is the caller's).</summary>
    public static float WipeRadiusAt(float sinceGrowing) =>
        sinceGrowing >= WipeMilliseconds ? WipeRadius : WipeRadius * FloodEase(Math.Max(0f, sinceGrowing) / WipeMilliseconds);

    /// <summary>
    /// Where the Stage's wipe grows from, on a Stage canvas <paramref name="stageWidth"/> × <paramref name="stageHeight"/>: the Launcher's
    /// focused x (<paramref name="launcherX"/> on its canvas of <paramref name="launcherWidth"/>, centred under the Stage as on the Thor,
    /// the mock's <c>cx + 170</c>), <see cref="WipeBelow"/> under the bottom edge.
    /// </summary>
    public static (float X, float Y) WipeOrigin(float launcherX, float launcherWidth, float stageWidth, float stageHeight) =>
        (launcherX + ((stageWidth - launcherWidth) / 2f), stageHeight + WipeBelow);
}

/// <summary>Where the arriving letters come from: the direction of the move, a folder opening or closing, or none (they rise).</summary>
public enum BigTypeEntrance
{
    /// <summary>No move (a first show, a page, a drift): the letters rise in, tilted.</summary>
    Rise,
    Left,
    Right,
    Up,
    Down,

    /// <summary>Into a folder: the letters rise from the hinge.</summary>
    Open,

    /// <summary>Out of a folder: the letters drop in from above.</summary>
    Back,
}

/// <summary>A letter of the name at a moment: its offset from its place in ems (y down), its tilt in degrees and its opacity.</summary>
public readonly record struct BigTypeLetterPose(float X, float Y, float Degrees, float Opacity)
{
    public static BigTypeLetterPose Rest { get; } = new(0f, 0f, 0f, 1f);
}
