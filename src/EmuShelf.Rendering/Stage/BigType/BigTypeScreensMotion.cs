namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>Where a heading's letters come from (the page's <c>letters()</c> directions).</summary>
public enum BigTypeScreenSide
{
    /// <summary>From below, tilted (a first show).</summary>
    Up,

    /// <summary>From above, tilted the other way.</summary>
    Down,

    /// <summary>From the left (the order reversed, so the nearest letter lands first).</summary>
    Left,

    /// <summary>From the right.</summary>
    Right,
}

/// <summary>
/// The four screens the owner picked on 16 November 2026 from <c>proposals-screens.html</c> (M47 B15): All apps as the Shelf, every
/// achievement as the Wall, the photo viewer as the Reel and the idle artwork as the Porthole. Their motion as pure functions of time, from
/// tokens.js (<c>motion.ScreenLettersIn</c> to <c>motion.PortWipe</c>), shared by the Avalonia companion and Stage and the native Stage.
/// Every time is in ms since the thing began. Held still (the device's say or the app's Reduce motion), everything is in place at once.
/// </summary>
public static class BigTypeScreensMotion
{
    // ---- tokens.js motion.ScreenLettersIn / ScreenLettersOut ----

    public const float LettersInMilliseconds = 520f;
    public const float LettersInDelay = 60f;
    public const float LettersInStagger = 24f;
    public const float LettersInStaggerTotal = 300f;
    public const float LettersFromEmUp = 0.9f;
    public const float LettersTiltUp = 6f;
    public const float LettersFromEmDown = -0.7f;
    public const float LettersTiltDown = -5f;
    public const float LettersFromEmSide = 0.55f;
    public const float LettersOutMilliseconds = 170f;
    public const float LettersOutStagger = 8f;
    public const float LettersOutStaggerMax = 90f;
    public const float LettersOutEmSide = 0.45f;
    public const float LettersOutEmVertical = 0.5f;

    // ---- tokens.js motion.ScreenHintsIn, ScreenLayerFade ----

    public const float HintsInMilliseconds = 380f;
    public const float HintsInRise = 24f;
    public const float HintsInStagger = 50f;
    public const float LayerFadeMilliseconds = 280f;

    // ---- tokens.js motion.ShelfIn, ShelfOut, ShelfFocus, ShelfScroll, ShelfBob ----

    public const float ShelfInMilliseconds = 440f;
    public const float ShelfInFromScale = 0.45f;
    public const float ShelfInStagger = 34f;
    public const float ShelfOutMilliseconds = 240f;
    public const float ShelfOutToScale = 0.55f;
    public const float ShelfOutPush = 52f;
    public const float ShelfOutStagger = 18f;
    public const int ShelfMaxSteps = 6;
    public const float ShelfFocusMilliseconds = 380f;
    public const float ShelfScrollMilliseconds = 450f;
    public const float ShelfBobMilliseconds = 1500f;
    public const float ShelfBobRise = 6f;

    // ---- tokens.js motion.WallIn, WallOut, WallMove, WallFilterOut, Pulse ----

    public const float WallInMilliseconds = 440f;
    public const float WallInFromScale = 0.3f;
    public const float WallInStagger = 38f;
    public const float WallOutMilliseconds = 180f;
    public const float WallOutDrop = 80f;
    public const float WallOutStagger = 12f;
    public const float WallMoveMilliseconds = 420f;
    public const float WallFilterOutMilliseconds = 380f;
    public const float WallFilterOutToScale = 0.3f;
    public const float WallFilterOutFadeMilliseconds = 250f;
    public const float PulseMilliseconds = 320f;
    public const float PulseFrom = 0.6f;

    // ---- tokens.js motion.ReelSlide, PhotoOpen, PhotoClose, ReelChrome; screens.ReelPitch, ReelSideScale, ReelSideOpacity ----

    public const float ReelSlideMilliseconds = 500f;
    public const float PhotoOpenMilliseconds = 480f;
    public const float PhotoCloseMilliseconds = 260f;
    public const float ReelChromeMilliseconds = 300f;
    public const float ReelPitch = 956f;
    public const float ReelSideScale = 0.86f;
    public const float ReelSideOpacity = 0.45f;

    // ---- tokens.js motion.PortIn, PortOut, PortFloat, PortWipe ----

    public const float PortInMilliseconds = 700f;
    public const float PortInDrop = 1040f;
    public const float PortInFromScale = 0.5f;
    public const float PortFadeMilliseconds = 300f;
    public const float PortOutMilliseconds = 320f;
    public const float PortOutToScale = 0.5f;
    public const float PortFloatMilliseconds = 5000f;
    public const float PortFloatFrom = 10f;
    public const float PortFloatTo = -14f;
    public const float PortWipeMilliseconds = 760f;
    public const float PortWipeShare = 1.2f;

    // ---- Curves ----

    /// <summary>The page's SPRING, cubic-bezier(.2, 1.5, .35, 1): letters, icons, badges, hints arriving (tokens.js <c>Land</c>).</summary>
    public static float Spring(float t) => BigTypeMotion.Land(t);

    /// <summary>The page's EASEIN, cubic-bezier(.5, 0, .9, .5): anything leaving (tokens.js <c>Leave</c>).</summary>
    public static float Leave(float t) => BigTypeMotion.Leave(t);

    /// <summary>The page's SOFT, cubic-bezier(.2, 1.15, .3, 1): rows scrolling, the reel sliding, a picture growing out.</summary>
    public static float Soft(float t) => Curve(0.2f, 1.15f, 0.3f, 1f, t);

    /// <summary>cubic-bezier(.2, 1.2, .3, 1): a badge moving to its place under a new filter.</summary>
    public static float Move(float t) => Curve(0.2f, 1.2f, 0.3f, 1f, t);

    /// <summary>cubic-bezier(.2, 1.4, .3, 1): the porthole rising.</summary>
    public static float Rise(float t) => Curve(0.2f, 1.4f, 0.3f, 1f, t);

    /// <summary>The page's WIPE, cubic-bezier(.2, .6, .2, 1): the next artwork's circle.</summary>
    public static float Wipe(float t) => Curve(0.2f, 0.6f, 0.2f, 1f, t);

    private static float Curve(float x1, float y1, float x2, float y2, float t) =>
        t <= 0f ? 0f : t >= 1f ? 1f : StageCurves.CubicBezier(x1, y1, x2, y2, t);

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);

    // ---- A heading whose letters land (the page's letters()) ----

    /// <summary>When letter <paramref name="index"/> of <paramref name="count"/> starts (the order reversed from the left).</summary>
    public static float LetterDelay(int index, int count, BigTypeScreenSide side)
    {
        count = Math.Max(1, count);
        var per = Math.Min(LettersInStagger, LettersInStaggerTotal / count);
        var order = side == BigTypeScreenSide.Left ? count - 1 - index : index;
        return LettersInDelay + (Math.Max(0, order) * per);
    }

    /// <summary>How long a heading of <paramref name="count"/> letters takes to land.</summary>
    public static float LettersInTotal(int count, bool holdStill = false) =>
        holdStill ? 0f : LetterDelay(Math.Max(0, count - 1), count, BigTypeScreenSide.Right) + LettersInMilliseconds;

    /// <summary>Where each letter starts (em across, em down, degrees).</summary>
    public static (float X, float Y, float Degrees) LetterFrom(BigTypeScreenSide side) => side switch
    {
        BigTypeScreenSide.Down => (0f, LettersFromEmDown, LettersTiltDown),
        BigTypeScreenSide.Right => (LettersFromEmSide, 0f, 0f),
        BigTypeScreenSide.Left => (-LettersFromEmSide, 0f, 0f),
        _ => (0f, LettersFromEmUp, LettersTiltUp),
    };

    /// <summary>
    /// Letter <paramref name="index"/> of <paramref name="count"/>, <paramref name="since"/> ms after its heading changed. With
    /// <paramref name="quick"/> (a press within a run of the one before, M47 B11's <c>LetterRun</c>) every letter comes together, from
    /// <see cref="BigTypeMotion.RunFromShare"/> of the way, over <see cref="BigTypeMotion.RunInMilliseconds"/>.
    /// </summary>
    public static BigTypeLetterPose LetterIn(int index, int count, BigTypeScreenSide side, float since, bool quick = false, bool holdStill = false)
    {
        if (holdStill)
            return BigTypeLetterPose.Rest;
        var (x, y, degrees) = LetterFrom(side);
        float t, share;
        if (quick)
        {
            t = since / BigTypeMotion.RunInMilliseconds;
            share = BigTypeMotion.RunFromShare;
        }
        else
        {
            t = (since - LetterDelay(index, count, side)) / LettersInMilliseconds;
            share = 1f;
        }
        if (t >= 1f)
            return BigTypeLetterPose.Rest;
        if (t <= 0f)
            return new BigTypeLetterPose(x * share, y * share, degrees * share, 0f);
        var e = Spring(t);
        var k = share * (1f - e);
        return new BigTypeLetterPose(x * k, y * k, degrees * k, Clamp01(e));
    }

    /// <summary>
    /// Letter <paramref name="index"/> of the heading being replaced, <paramref name="since"/> ms after: it leaves the other way from the
    /// new one's side; null once gone. Quick (a run): all together over <see cref="BigTypeMotion.RunOutMilliseconds"/>.
    /// </summary>
    public static BigTypeLetterPose? LetterOut(int index, BigTypeScreenSide side, float since, bool quick = false, bool holdStill = false)
    {
        if (holdStill)
            return null;
        var (x, y) = side switch
        {
            BigTypeScreenSide.Left => (LettersOutEmSide, 0f),
            BigTypeScreenSide.Right => (-LettersOutEmSide, 0f),
            BigTypeScreenSide.Down => (0f, LettersOutEmVertical),
            _ => (0f, -LettersOutEmVertical),
        };
        var t = quick ? since / BigTypeMotion.RunOutMilliseconds
            : (since - Math.Min(index * LettersOutStagger, LettersOutStaggerMax)) / LettersOutMilliseconds;
        if (t >= 1f)
            return null;
        var e = Leave(Math.Max(0f, t));
        return new BigTypeLetterPose(x * e, y * e, 0f, 1f - e);
    }

    /// <summary>How long a replaced heading takes to go.</summary>
    public static float LettersOutTotal(bool quick = false, bool holdStill = false) =>
        holdStill ? 0f : quick ? BigTypeMotion.RunOutMilliseconds : LettersOutStaggerMax + LettersOutMilliseconds;

    // ---- The hints on the bottom screen ----

    /// <summary>Hint <paramref name="index"/> of a new row, <paramref name="since"/> ms after it changed: how far below its place, its opacity.</summary>
    public static (float Rise, float Opacity) HintIn(int index, float since, bool holdStill = false)
    {
        var t = (since - (Math.Max(0, index) * HintsInStagger)) / HintsInMilliseconds;
        if (holdStill || t >= 1f)
            return (0f, 1f);
        if (t <= 0f)
            return (HintsInRise, 0f);
        var e = Spring(t);
        return (HintsInRise * (1f - e), Clamp01(e));
    }

    /// <summary>A layer over the companion coming (<paramref name="showing"/>) or going: its opacity.</summary>
    public static float LayerOpacity(bool showing, float since, bool holdStill = false)
    {
        var t = holdStill ? 1f : Clamp01(since / LayerFadeMilliseconds);
        var e = BigTypeMotion.CssEase(t);
        return showing ? e : 1f - e;
    }

    // ---- 2 Shelf ----

    /// <summary>Steps from the focused cell: the page's <c>min(hypot(dc, dr), 6)</c>.</summary>
    public static float Steps(int dColumn, int dRow) => Math.Min(MathF.Sqrt((dColumn * dColumn) + (dRow * dRow)), ShelfMaxSteps);

    /// <summary>An icon <paramref name="since"/> ms after All apps opened, <paramref name="dColumn"/>, <paramref name="dRow"/> cells from the focused one.</summary>
    public static (float Scale, float Opacity) ShelfIconIn(int dColumn, int dRow, float since, bool holdStill = false)
    {
        var t = (since - (Steps(dColumn, dRow) * ShelfInStagger)) / ShelfInMilliseconds;
        if (holdStill || t >= 1f)
            return (1f, 1f);
        if (t <= 0f)
            return (ShelfInFromScale, 0f);
        var e = Spring(t);
        return (ShelfInFromScale + ((1f - ShelfInFromScale) * e), Clamp01(e));
    }

    /// <summary>How long the icons take to arrive.</summary>
    public static float ShelfInTotal(bool holdStill = false) => holdStill ? 0f : (ShelfMaxSteps * ShelfInStagger) + ShelfInMilliseconds;

    /// <summary>An icon <paramref name="since"/> ms after All apps closed: pushed away from the focused one, shrinking, fading.</summary>
    public static (float X, float Y, float Scale, float Opacity) ShelfIconOut(int dColumn, int dRow, float since, bool holdStill = false)
    {
        var t = (since - (Steps(dColumn, dRow) * ShelfOutStagger)) / ShelfOutMilliseconds;
        if (holdStill || t >= 1f)
            return (dColumn * ShelfOutPush, dRow * ShelfOutPush, ShelfOutToScale, 0f);
        if (t <= 0f)
            return (0f, 0f, 1f, 1f);
        var e = Leave(t);
        return (dColumn * ShelfOutPush * e, dRow * ShelfOutPush * e, 1f + ((ShelfOutToScale - 1f) * e), 1f - e);
    }

    /// <summary>How long the icons take to go.</summary>
    public static float ShelfOutTotal(bool holdStill = false) => holdStill ? 0f : (ShelfMaxSteps * ShelfOutStagger) + ShelfOutMilliseconds;

    /// <summary>
    /// A value easing from <paramref name="from"/> to <paramref name="to"/> on the focus's transition (the CSS one: a change mid-way starts
    /// again from where it is drawn, as a browser does).
    /// </summary>
    public static float Focus(float from, float to, float since, bool holdStill = false)
    {
        if (holdStill || since >= ShelfFocusMilliseconds)
            return to;
        return from + ((to - from) * Spring(Math.Max(0f, since) / ShelfFocusMilliseconds));
    }

    /// <summary>The focused icon's bob <paramref name="since"/> ms after it was focused: px down (negative: up), ease-in-out each way.</summary>
    public static float ShelfBob(float since, bool holdStill = false)
    {
        if (holdStill || since <= 0f)
            return 0f;
        var phase = since / ShelfBobMilliseconds;
        var leg = phase % 2f;
        var t = leg <= 1f ? leg : 2f - leg;
        return -ShelfBobRise * BigTypeMotion.EaseInOut(t);
    }

    // ---- 4 Wall ----

    /// <summary>A badge <paramref name="since"/> ms after every badge opened, at <paramref name="column"/>, <paramref name="row"/> of the wall.</summary>
    public static (float Scale, float Opacity) WallBadgeIn(int column, int row, float since, bool holdStill = false)
    {
        var t = (since - ((Math.Max(0, column) + Math.Max(0, row)) * WallInStagger)) / WallInMilliseconds;
        if (holdStill || t >= 1f)
            return (1f, 1f);
        if (t <= 0f)
            return (WallInFromScale, 0f);
        var e = Spring(t);
        return (WallInFromScale + ((1f - WallInFromScale) * e), Clamp01(e));
    }

    /// <summary>How long a wall of <paramref name="columns"/> by <paramref name="rows"/> takes to arrive.</summary>
    public static float WallInTotal(int columns, int rows, bool holdStill = false) =>
        holdStill ? 0f : ((Math.Max(0, columns - 1) + Math.Max(0, rows - 1)) * WallInStagger) + WallInMilliseconds;

    /// <summary>A badge <paramref name="since"/> ms after the wall closed: how far it has dropped, its opacity.</summary>
    public static (float Drop, float Opacity) WallBadgeOut(int column, float since, bool holdStill = false)
    {
        var t = (since - (Math.Max(0, column) * WallOutStagger)) / WallOutMilliseconds;
        if (holdStill || t >= 1f)
            return (WallOutDrop, 0f);
        if (t <= 0f)
            return (0f, 1f);
        var e = Leave(t);
        return (WallOutDrop * e, 1f - e);
    }

    /// <summary>How long a wall of <paramref name="columns"/> takes to go.</summary>
    public static float WallOutTotal(int columns, bool holdStill = false) =>
        holdStill ? 0f : (Math.Max(0, columns - 1) * WallOutStagger) + WallOutMilliseconds;

    /// <summary>A badge moving to its place under a new filter: the share of the way it has come.</summary>
    public static float WallMoved(float since, bool holdStill = false) =>
        holdStill || since >= WallMoveMilliseconds ? 1f : Move(Math.Max(0f, since) / WallMoveMilliseconds);

    /// <summary>A badge a new filter leaves out, <paramref name="since"/> ms after: its scale and opacity.</summary>
    public static (float Scale, float Opacity) WallBadgeFiltered(float since, bool holdStill = false)
    {
        if (holdStill)
            return (WallFilterOutToScale, 0f);
        var scale = since >= WallFilterOutMilliseconds ? WallFilterOutToScale
            : 1f + ((WallFilterOutToScale - 1f) * Spring(Math.Max(0f, since) / WallFilterOutMilliseconds));
        var opacity = 1f - BigTypeMotion.CssEase(Clamp01(since / WallFilterOutFadeMilliseconds));
        return (scale, opacity);
    }

    /// <summary>A new unlock's pulse <paramref name="since"/> ms after the refresh brought it: its scale.</summary>
    public static float Pulse(float since, bool holdStill = false)
    {
        if (holdStill || since >= PulseMilliseconds || since < 0f)
            return 1f;
        return PulseFrom + ((1f - PulseFrom) * Spring(since / PulseMilliseconds));
    }

    // ---- 9 Reel ----

    /// <summary>
    /// A picture <paramref name="offset"/> pictures from the one shown (fractional while the row slides): its x from the shown one's
    /// place, its scale and opacity. The shown one is whole; one either side is at <see cref="ReelSideScale"/> and
    /// <see cref="ReelSideOpacity"/>; further ones are gone (the page's <c>place()</c>, taken between its stops).
    /// </summary>
    public static (float X, float Scale, float Opacity) ReelPose(float offset)
    {
        var d = Math.Abs(offset);
        var scale = 1f - ((1f - ReelSideScale) * Math.Min(d, 1f));
        var opacity = d <= 1f ? 1f - ((1f - ReelSideOpacity) * d) : Math.Max(0f, ReelSideOpacity * (2f - d));
        return (offset * ReelPitch, scale, opacity);
    }

    /// <summary>
    /// A picture growing out of its small picture on the Photos tab (<paramref name="opening"/>) or going back into it,
    /// <paramref name="since"/> ms after: the share of the way from the small picture to its place in the reel (1: in place).
    /// </summary>
    public static float PhotoFlip(bool opening, float since, bool holdStill = false)
    {
        if (opening)
            return holdStill || since >= PhotoOpenMilliseconds ? 1f : Soft(Math.Max(0f, since) / PhotoOpenMilliseconds);
        return holdStill || since >= PhotoCloseMilliseconds ? 0f : 1f - Leave(Math.Max(0f, since) / PhotoCloseMilliseconds);
    }

    /// <summary>The figure and the lines fading out on a tap (<paramref name="shown"/> false) or back: their opacity, from <paramref name="from"/>.</summary>
    public static float Chrome(float from, bool shown, float since, bool holdStill = false)
    {
        var to = shown ? 1f : 0f;
        if (holdStill || since >= ReelChromeMilliseconds)
            return to;
        return from + ((to - from) * BigTypeMotion.CssEase(Math.Max(0f, since) / ReelChromeMilliseconds));
    }

    // ---- 12 Porthole ----

    /// <summary>The round window <paramref name="since"/> ms after the drift began: how far below its place, its scale, its opacity.</summary>
    public static (float Drop, float Scale, float Opacity) PortIn(float since, bool holdStill = false)
    {
        if (holdStill)
            return (0f, 1f, 1f);
        var e = since >= PortInMilliseconds ? 1f : Rise(Math.Max(0f, since) / PortInMilliseconds);
        var opacity = BigTypeMotion.CssEase(Clamp01(since / PortFadeMilliseconds));
        return (PortInDrop * (1f - e), PortInFromScale + ((1f - PortInFromScale) * e), opacity);
    }

    /// <summary>The window <paramref name="since"/> ms after a press woke the drift: dropping back through the hinge.</summary>
    public static (float Drop, float Scale, float Opacity) PortOut(float since, bool holdStill = false)
    {
        if (holdStill)
            return (PortInDrop, PortOutToScale, 0f);
        var e = since >= PortOutMilliseconds ? 1f : Leave(Math.Max(0f, since) / PortOutMilliseconds);
        var opacity = 1f - BigTypeMotion.CssEase(Clamp01(since / PortFadeMilliseconds));
        return (PortInDrop * e, 1f + ((PortOutToScale - 1f) * e), opacity);
    }

    /// <summary>How long the window takes to go.</summary>
    public static float PortOutTotal(bool holdStill = false) => holdStill ? 0f : Math.Max(PortOutMilliseconds, PortFadeMilliseconds);

    /// <summary>The window's float <paramref name="since"/> ms after it arrived: px down (from 10 to −14 and back, ease-in-out each way).</summary>
    public static float PortFloat(float since, bool holdStill = false)
    {
        if (holdStill)
            return 0f;
        var leg = Math.Max(0f, since) / PortFloatMilliseconds % 2f;
        var t = leg <= 1f ? leg : 2f - leg;
        return PortFloatFrom + ((PortFloatTo - PortFloatFrom) * BigTypeMotion.EaseInOut(t));
    }

    /// <summary>
    /// The next game's artwork <paramref name="since"/> ms after it came: the radius of its circle from the window's bottom middle, for a
    /// window <paramref name="size"/> px across (the page's <c>circle(0% → 120% at 50% 100%)</c>).
    /// </summary>
    public static float PortWipeRadius(float since, float size, bool holdStill = false)
    {
        var whole = PortWipeShare * size;
        if (holdStill || since >= PortWipeMilliseconds)
            return whole;
        return whole * Wipe(Math.Max(0f, since) / PortWipeMilliseconds);
    }
}

/// <summary>
/// One value that eases to its target and, when a new target comes before it has arrived, glides on from where it is drawn and how
/// fast it is going (M47 B11's rule, as <see cref="BigTypeRingGlide"/> does for the ring): a single change runs on its curve over its
/// time; a change mid-way trails the moving target on a critically damped spring and settles by that time after the last change. Nothing
/// restarts from rest, nothing stacks, nothing jumps. Used for the Shelf's and the Wall's rows scrolling and the Reel sliding.
/// </summary>
public sealed class BigTypeGlide(float value, float milliseconds, Func<float, float> ease)
{
    private const float Rate = 2f / BigTypeRingGlide.LagMilliseconds;
    private float _from = value;
    private float _velocity;
    private float _start = float.NegativeInfinity;
    private bool _spring;

    /// <summary>Where it is going.</summary>
    public float Target { get; private set; } = value;

    /// <summary>A new target from <paramref name="now"/>; held still it is there at once.</summary>
    public void To(float target, float now, bool holdStill = false)
    {
        if (target == Target && !holdStill)
            return;
        if (holdStill)
        {
            Target = _from = target;
            _velocity = 0f;
            _start = float.NegativeInfinity;
            return;
        }
        var moving = now - _start < milliseconds;
        var drawn = At(now);
        _velocity = moving ? (At(now + 0.5f) - At(now - 0.5f)) : 0f;
        _from = drawn;
        _spring = moving;
        _start = now;
        Target = target;
    }

    /// <summary>Puts it at <paramref name="target"/> at once.</summary>
    public void Jump(float target)
    {
        Target = _from = target;
        _velocity = 0f;
        _start = float.NegativeInfinity;
    }

    /// <summary>Whether it has arrived by <paramref name="now"/>.</summary>
    public bool Settled(float now) => now - _start >= milliseconds;

    /// <summary>Its value <paramref name="now"/>.</summary>
    public float At(float now)
    {
        var t = now - _start;
        if (t >= milliseconds)
            return Target;
        t = Math.Max(0f, t);
        if (!_spring)
            return _from + ((Target - _from) * ease(t / milliseconds));
        var e0 = _from - Target;
        return Target + ((e0 + ((_velocity + (Rate * e0)) * t)) * MathF.Exp(-Rate * t));
    }
}
