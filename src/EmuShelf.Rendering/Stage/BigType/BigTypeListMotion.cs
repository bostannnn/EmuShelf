namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The Big Type lists' motion (M47 B7), as pure functions of time from tokens.js <c>motion</c> and the mock's script: rows arriving
/// (<c>RowsIn</c>, the mock's <c>slideIn</c>), a Settings section's rows rising in when the section changes (<c>SectionIn</c>), a
/// heading's letters landing (<c>HeadingIn</c>, the mock's <c>lettersIn</c>), and the grid stepping back under a sheet
/// (<c>StepBack</c>). Each is the mock's Web Animation: one keyframe pair eased as a whole on the Land curve, held at its first
/// keyframe until its delay has passed (<c>fill: backwards</c>). Holding still, everything is where it rests.
/// </summary>
public static class BigTypeListMotion
{
    /// <summary>tokens.js <c>RowsIn</c>: each row slides <c>slide</c> px from the right (the rail <c>railSlide</c>, from the left) and
    /// fades in over 420 ms on the Land curve, the first <c>delay</c> ms after the list opens and each next one <c>stagger</c> ms later.</summary>
    public const float RowsInMilliseconds = 420f;
    public const float RowsInStagger = 32f;
    public const float RowsInSlide = 140f;
    public const float RowsInDelay = 40f;
    public const float RowsInRailSlide = -100f;

    /// <summary>tokens.js <c>SectionIn</c>: a section's rows rise <c>rise</c> px (from above when the move was up) over 400 ms, 30 ms apart.</summary>
    public const float SectionInMilliseconds = 400f;
    public const float SectionInStagger = 30f;
    public const float SectionInRise = 52f;

    /// <summary>tokens.js <c>CoversIn</c>: cover search's candidates rise <c>rise</c> px from <c>fromScale</c> over 460 ms, 45 ms later
    /// per step of column + row.</summary>
    public const float CoversInMilliseconds = 460f;
    public const float CoversInStagger = 45f;
    public const float CoversInRise = 120f;
    public const float CoversInFromScale = 0.6f;

    /// <summary>tokens.js <c>HeadingIn</c> (the mock's <c>lettersIn</c>): each letter rises <c>fromEm</c> (a sheet's title
    /// <c>sheetFromEm</c>) tilted <c>tilt</c>° over 520 ms on the Land curve, <c>delay</c> ms after the heading shows and
    /// <c>stagger</c> ms a letter, at most <c>staggerMax</c>.</summary>
    public const float HeadingInMilliseconds = 520f;
    public const float HeadingInDelay = 60f;
    public const float HeadingInStagger = 24f;
    public const float HeadingInStaggerMax = 360f;
    public const float HeadingInFromEm = 0.9f;
    public const float HeadingInSheetFromEm = 0.8f;
    public const float HeadingInTilt = 6f;

    /// <summary>tokens.js <c>StepBack</c>: the grid under a sheet scales on cubic-bezier(.2, 1.2, .3, 1) over 400 ms and fades
    /// (CSS <c>ease</c>) over 280 ms.</summary>
    public const float StepBackMilliseconds = 400f;
    public const float StepBackFadeMilliseconds = 280f;

    /// <summary>tokens.js <c>RowFocus</c>: the focused row moves over on the Land curve in 350 ms and brightens in 200.</summary>
    public const float RowFocusMilliseconds = 350f;
    public const float RowFocusFadeMilliseconds = 200f;

    /// <summary>tokens.js <c>Switch</c>: the knob springs across on the Land curve in 350 ms; the track fills in 250.</summary>
    public const float SwitchMilliseconds = 350f;
    public const float SwitchFadeMilliseconds = 250f;

    /// <summary>tokens.js <c>Scroll</c>: a list longer than its window moves by whole rows over 500 ms on cubic-bezier(.2, 1.15, .3, 1).</summary>
    public const float ScrollMilliseconds = 500f;

    /// <summary>tokens.js <c>SheetOut</c>: a sheet's (or the menu's) rows leave right when it closes.</summary>
    public static readonly BigTypeListExit SheetOut = new(160f, 12f, int.MaxValue, 120f, Vertical: false, HoldMilliseconds: 240f);

    /// <summary>tokens.js <c>SheetSwap</c>: a sheet's rows leave left when another sheet takes its place.</summary>
    public static readonly BigTypeListExit SheetSwap = new(150f, 10f, int.MaxValue, -120f, Vertical: false, HoldMilliseconds: 210f);

    /// <summary>tokens.js <c>SettingsOut</c>: the rail's and the rows' rows drop when Settings closes.</summary>
    public static readonly BigTypeListExit SettingsOut = new(180f, 10f, int.MaxValue, 80f, Vertical: true, HoldMilliseconds: 290f);

    /// <summary>tokens.js <c>PanelOut</c>: a panel's rows drop when it closes, at most eight staggers apart.</summary>
    public static readonly BigTypeListExit PanelOut = new(170f, 10f, 8, 80f, Vertical: true, HoldMilliseconds: 280f);

    /// <summary>
    /// Row <paramref name="index"/> of a list leaving <paramref name="since"/> ms ago by <paramref name="exit"/>: how far it has gone
    /// (along x, or y when the exit drops) and how opaque it still is. One keyframe pair eased as a whole on the Leave curve, held at
    /// its end (fill: forwards).
    /// </summary>
    public static (float Offset, float Opacity) RowOut(BigTypeListExit exit, int index, float since, bool holdStill = false)
    {
        var local = since - (Math.Min(Math.Max(0, index), exit.StaggerCap) * exit.Stagger);
        if (holdStill || local >= exit.Milliseconds)
            return (exit.To, 0f);
        if (local <= 0f)
            return (0f, 1f);
        var eased = BigTypeMotion.Leave(local / exit.Milliseconds);
        return (exit.To * eased, Math.Clamp(1f - eased, 0f, 1f));
    }

    /// <summary>The grid stepping back: tokens.js <c>StepBack.easing</c>.</summary>
    public static float StepBackEase(float t) => t <= 0f ? 0f : t >= 1f ? 1f : StageCurves.CubicBezier(0.2f, 1.2f, 0.3f, 1f, t);

    /// <summary>
    /// Row <paramref name="index"/> of a list that opened <paramref name="since"/> ms ago and slides in from <paramref name="slide"/>
    /// px: how far it still is from its place (along x) and how opaque it is.
    /// </summary>
    public static (float Offset, float Opacity) RowIn(int index, float since, float slide = RowsInSlide, bool holdStill = false) =>
        Arrive(since - RowsInDelay - (Math.Max(0, index) * RowsInStagger), RowsInMilliseconds, slide, holdStill);

    /// <summary>Row <paramref name="index"/> of a Settings section shown <paramref name="since"/> ms ago, rising from <paramref name="rise"/> px (along y).</summary>
    public static (float Offset, float Opacity) SectionRowIn(int index, float since, float rise, bool holdStill = false) =>
        Arrive(since - (Math.Max(0, index) * SectionInStagger), SectionInMilliseconds, rise, holdStill);

    /// <summary>
    /// The candidate at (<paramref name="column"/>, <paramref name="row"/>) of a grid shown <paramref name="since"/> ms ago: how far
    /// below its place it is, its scale and its opacity.
    /// </summary>
    public static (float Rise, float Scale, float Opacity) CoverIn(int column, int row, float since, bool holdStill = false)
    {
        var local = since - ((Math.Max(0, column) + Math.Max(0, row)) * CoversInStagger);
        if (holdStill || local >= CoversInMilliseconds)
            return (0f, 1f, 1f);
        if (local <= 0f)
            return (CoversInRise, CoversInFromScale, 0f);
        var eased = BigTypeMotion.Land(local / CoversInMilliseconds);
        return (CoversInRise * (1f - eased), CoversInFromScale + ((1f - CoversInFromScale) * eased), Math.Clamp(eased, 0f, 1f));
    }

    /// <summary>How long a grid whose last candidate is <paramref name="steps"/> steps (column + row) from the first takes to arrive.</summary>
    public static float CoversInTotal(int steps) => (Math.Max(0, steps) * CoversInStagger) + CoversInMilliseconds;

    /// <summary>How long a list of <paramref name="rows"/> rows takes to arrive.</summary>
    public static float RowsInTotal(int rows) => RowsInDelay + (Math.Max(0, rows - 1) * RowsInStagger) + RowsInMilliseconds;

    /// <summary>How long a section of <paramref name="rows"/> rows takes to rise in.</summary>
    public static float SectionInTotal(int rows) => (Math.Max(0, rows - 1) * SectionInStagger) + SectionInMilliseconds;

    /// <summary>When letter <paramref name="index"/> of a heading starts to land.</summary>
    public static float HeadingLetterDelay(int index) => HeadingInDelay + Math.Min(Math.Max(0, index) * HeadingInStagger, HeadingInStaggerMax);

    /// <summary>
    /// Letter <paramref name="index"/> of a heading shown <paramref name="since"/> ms ago that rises from <paramref name="fromEm"/>:
    /// how far below its place it is (in em), its tilt (degrees) and its opacity.
    /// </summary>
    public static (float Em, float Degrees, float Opacity) HeadingLetter(int index, float since, float fromEm = HeadingInFromEm, bool holdStill = false)
    {
        var local = since - HeadingLetterDelay(index);
        if (holdStill || local >= HeadingInMilliseconds)
            return (0f, 0f, 1f);
        if (local <= 0f)
            return (fromEm, HeadingInTilt, 0f);
        var eased = BigTypeMotion.Land(local / HeadingInMilliseconds);
        return (fromEm * (1f - eased), HeadingInTilt * (1f - eased), Math.Clamp(eased, 0f, 1f));
    }

    /// <summary>How long a heading of <paramref name="letters"/> letters takes to land.</summary>
    public static float HeadingInTotal(int letters) => HeadingLetterDelay(Math.Max(0, letters - 1)) + HeadingInMilliseconds;

    /// <summary>
    /// The grid's scale and opacity <paramref name="since"/> ms after it began to go from (<paramref name="fromScale"/>,
    /// <paramref name="fromOpacity"/>) to (<paramref name="toScale"/>, <paramref name="toOpacity"/>): a sheet opening steps it back,
    /// one closing brings it forward.
    /// </summary>
    public static (float Scale, float Opacity) StepBack(float since, float fromScale, float fromOpacity, float toScale, float toOpacity, bool holdStill = false)
    {
        if (holdStill || since >= StepBackMilliseconds)
            return (toScale, toOpacity);
        var s = StepBackEase(Math.Max(0f, since) / StepBackMilliseconds);
        var o = BigTypeMotion.CssEase(Math.Clamp(since / StepBackFadeMilliseconds, 0f, 1f));
        return (fromScale + ((toScale - fromScale) * s), fromOpacity + ((toOpacity - fromOpacity) * o));
    }

    // One keyframe pair eased as a whole on the Land curve, held at its start before its delay (fill: backwards).
    private static (float Offset, float Opacity) Arrive(float local, float duration, float from, bool holdStill)
    {
        if (holdStill || local >= duration)
            return (0f, 1f);
        if (local <= 0f)
            return (from, 0f);
        var eased = BigTypeMotion.Land(local / duration);
        return (from * (1f - eased), Math.Clamp(eased, 0f, 1f));
    }
}

/// <summary>How a list's rows leave (M47 B7): over <paramref name="Milliseconds"/>, <paramref name="Stagger"/> ms apart for at most
/// <paramref name="StaggerCap"/> rows, to <paramref name="To"/> px along x (or y when <paramref name="Vertical"/>); the list goes after
/// <paramref name="HoldMilliseconds"/>.</summary>
public readonly record struct BigTypeListExit(float Milliseconds, float Stagger, int StaggerCap, float To, bool Vertical, float HoldMilliseconds);
