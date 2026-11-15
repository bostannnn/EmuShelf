namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The Big Type keyboard's motion (M47 B9), as pure functions of time from tokens.js <c>motion</c> and the mock's script (<c>openKb</c>,
/// <c>closeKb</c>, <c>kbSearch</c>, <c>pulse</c>, <c>typeWord</c>): the keys rising in and dropping out, Search's results arriving, a key
/// pulsing when pressed, the typed word's letters dropping in and falling off its end, the word resizing, the name before it leaving
/// upward, and a folder made from the keyboard being born on its slot. Each is the mock's Web Animation: one keyframe pair eased as a
/// whole on its curve, held at its first keyframe until its delay has passed. Holding still, everything is where it rests.
/// </summary>
public static class BigTypeTypingMotion
{
    /// <summary>tokens.js <c>KeysIn</c>: each key rises <c>rise</c> px and clears over 440 ms on the Land curve, 50 ms later a row and
    /// 12 ms a key along it; the ring is on the first key <c>ringAfterMs</c> after the keyboard opens.</summary>
    public const float KeysInMilliseconds = 440f;
    public const float KeysInRise = 140f;
    public const float KeysInRowStagger = 50f;
    public const float KeysInKeyStagger = 12f;
    public const float KeysInRingAfter = 260f;

    /// <summary>tokens.js <c>KeyFocus</c>: a key taking the focus turns white over 200 ms and grows on the Land curve over 300 ms.</summary>
    public const float KeyFocusMilliseconds = 300f;
    public const float KeyFocusFadeMilliseconds = 200f;

    /// <summary>tokens.js <c>KeyPress</c>: a key pressed (or tapped) pulses from 0.95 to rest over 300 ms on the Land curve.</summary>
    public const float KeyPressMilliseconds = 300f;
    public const float KeyPressFromScale = 0.95f;

    /// <summary>tokens.js <c>ResultsIn</c>: Search's results come down from <c>from</c> px at <c>fromScale</c> and clear, over 420 ms
    /// on the Land curve, 45 ms apart.</summary>
    public const float ResultsInMilliseconds = 420f;
    public const float ResultsInStagger = 45f;
    public const float ResultsInFrom = -60f;
    public const float ResultsInFromScale = 0.6f;

    /// <summary>tokens.js <c>KeysOut</c>: closing, each key and result drops <c>drop</c> px and clears over 170 ms on the Leave curve,
    /// 8 ms apart counted every 10 keys; the layer under comes back after <c>holdMs</c>.</summary>
    public const float KeysOutMilliseconds = 170f;
    public const float KeysOutDrop = 100f;
    public const float KeysOutStagger = 8f;
    public const int KeysOutStaggerEvery = 10;
    public const float KeysOutHoldMilliseconds = 290f;

    /// <summary>tokens.js <c>FolderBorn</c>: a folder named on the keyboard grows on its slot from nothing, turned −25°, over 600 ms on
    /// the Land curve.</summary>
    public const float FolderBornMilliseconds = 600f;
    public const float FolderBornFromScale = 0f;
    public const float FolderBornFromTilt = -25f;

    /// <summary>tokens.js <c>TypedLetterIn</c>: a new letter drops in from 0.55 em above, tilted −8°, over 460 ms on the Land curve,
    /// 30 ms after the one before it.</summary>
    public const float TypedLetterInMilliseconds = 460f;
    public const float TypedLetterInFromEm = 0.55f;
    public const float TypedLetterInTilt = -8f;
    public const float TypedLetterInStagger = 30f;

    /// <summary>tokens.js <c>TypedLetterOut</c>: a deleted letter falls 0.6 em off the end, turning 14°, over 260 ms on the Leave curve.</summary>
    public const float TypedLetterOutMilliseconds = 260f;
    public const float TypedLetterOutDropEm = 0.6f;
    public const float TypedLetterOutTilt = 14f;

    /// <summary>tokens.js <c>TypedResize</c>: the word set to its new size over 200 ms (CSS ease-out).</summary>
    public const float TypedResizeMilliseconds = 200f;

    /// <summary>tokens.js <c>TypedNameOut</c>: the name that was there leaves 0.6 em upward over 170 ms on the Leave curve, 10 ms a
    /// letter up to 110.</summary>
    public const float TypedNameOutMilliseconds = 170f;
    public const float TypedNameOutStagger = 10f;
    public const float TypedNameOutStaggerTotal = 110f;
    public const float TypedNameOutToEm = -0.6f;

    /// <summary>tokens.js <c>colour.PlaceholderOpacity</c>: the placeholder ("Name", "Search") in place of the typed word.</summary>
    public const float PlaceholderOpacity = 0.34f;

    /// <summary>CSS <c>ease-out</c>, cubic-bezier(0, 0, .58, 1).</summary>
    public static float EaseOut(float t) => t <= 0f ? 0f : t >= 1f ? 1f : StageCurves.CubicBezier(0f, 0f, 0.58f, 1f, t);

    // ---- The keys ----

    /// <summary>
    /// Key <paramref name="index"/> of row <paramref name="row"/>, <paramref name="since"/> ms after the keyboard opened: how far below
    /// its place it still is and how opaque it is.
    /// </summary>
    public static (float Rise, float Opacity) KeyIn(int row, int index, float since, bool holdStill = false)
    {
        var local = since - (Math.Max(0, row) * KeysInRowStagger) - (Math.Max(0, index) * KeysInKeyStagger);
        if (holdStill || local >= KeysInMilliseconds)
            return (0f, 1f);
        if (local <= 0f)
            return (KeysInRise, 0f);
        var e = BigTypeMotion.Land(local / KeysInMilliseconds);
        return (KeysInRise * (1f - e), Math.Clamp(e, 0f, 1f));
    }

    /// <summary>How long a keyboard whose last key is key <paramref name="lastIndex"/> of row <paramref name="lastRow"/> takes to rise in.</summary>
    public static float KeysInTotal(int lastRow, int lastIndex) =>
        (Math.Max(0, lastRow) * KeysInRowStagger) + (Math.Max(0, lastIndex) * KeysInKeyStagger) + KeysInMilliseconds;

    /// <summary>When the ring first shows on a keyboard opened <paramref name="since"/> ms ago (the mock's <c>sleep(260)</c>).</summary>
    public static bool RingShows(float since, bool holdStill = false) => holdStill || since >= KeysInRingAfter;

    /// <summary>
    /// Key (or result) <paramref name="index"/>, counted in drawing order, <paramref name="since"/> ms after the keyboard began to close:
    /// how far it has dropped and how opaque it still is.
    /// </summary>
    public static (float Drop, float Opacity) KeyOut(int index, float since, bool holdStill = false)
    {
        var local = since - ((Math.Max(0, index) % KeysOutStaggerEvery) * KeysOutStagger);
        if (holdStill || local >= KeysOutMilliseconds)
            return (KeysOutDrop, 0f);
        if (local <= 0f)
            return (0f, 1f);
        var e = BigTypeMotion.Leave(local / KeysOutMilliseconds);
        return (KeysOutDrop * e, Math.Clamp(1f - e, 0f, 1f));
    }

    /// <summary>How long a closing keyboard keeps the screen before the layer under it comes back (0 held still).</summary>
    public static float KeysOutHold(bool holdStill = false) => holdStill ? 0f : KeysOutHoldMilliseconds;

    /// <summary>A key pressed <paramref name="since"/> ms ago: its scale (the mock's <c>pulse</c>).</summary>
    public static float KeyPulse(float since, bool holdStill = false)
    {
        if (holdStill || since >= KeyPressMilliseconds || since < 0f)
            return 1f;
        return KeyPressFromScale + ((1f - KeyPressFromScale) * BigTypeMotion.Land(since / KeyPressMilliseconds));
    }

    /// <summary>
    /// Result <paramref name="index"/> of a set shown <paramref name="since"/> ms ago: how far above its place it still is (negative is
    /// up), its scale and its opacity.
    /// </summary>
    public static (float Y, float Scale, float Opacity) ResultIn(int index, float since, bool holdStill = false)
    {
        var local = since - (Math.Max(0, index) * ResultsInStagger);
        if (holdStill || local >= ResultsInMilliseconds)
            return (0f, 1f, 1f);
        if (local <= 0f)
            return (ResultsInFrom, ResultsInFromScale, 0f);
        var e = BigTypeMotion.Land(local / ResultsInMilliseconds);
        return (ResultsInFrom * (1f - e), ResultsInFromScale + ((1f - ResultsInFromScale) * e), Math.Clamp(e, 0f, 1f));
    }

    /// <summary>How long a set of <paramref name="count"/> results takes to arrive.</summary>
    public static float ResultsInTotal(int count) => (Math.Max(0, count - 1) * ResultsInStagger) + ResultsInMilliseconds;

    /// <summary>A folder made on the keyboard, <paramref name="since"/> ms after it was born on its slot: its scale, tilt and opacity.</summary>
    public static (float Scale, float Degrees, float Opacity) FolderBorn(float since, bool holdStill = false)
    {
        if (holdStill || since >= FolderBornMilliseconds)
            return (1f, 0f, 1f);
        if (since <= 0f)
            return (FolderBornFromScale, FolderBornFromTilt, 0f);
        var e = BigTypeMotion.Land(since / FolderBornMilliseconds);
        return (FolderBornFromScale + ((1f - FolderBornFromScale) * e), FolderBornFromTilt * (1f - e), Math.Clamp(e, 0f, 1f));
    }

    // ---- The typed word ----

    /// <summary>
    /// A letter typed <paramref name="since"/> ms ago, the <paramref name="fresh"/>-th of the letters that arrived together (0 for the
    /// first): its offset in ems, tilt and opacity. Before its turn it is not drawn.
    /// </summary>
    public static BigTypeLetterPose TypedIn(int fresh, float since, bool holdStill = false)
    {
        if (holdStill)
            return BigTypeLetterPose.Rest;
        var t = (since - (Math.Max(0, fresh) * TypedLetterInStagger)) / TypedLetterInMilliseconds;
        if (t <= 0f)
            return new BigTypeLetterPose(0f, -TypedLetterInFromEm, TypedLetterInTilt, 0f);
        if (t >= 1f)
            return BigTypeLetterPose.Rest;
        var e = BigTypeMotion.Land(t);
        var k = 1f - e;
        return new BigTypeLetterPose(0f, -TypedLetterInFromEm * k, TypedLetterInTilt * k, Math.Clamp(e, 0f, 1f));
    }

    /// <summary>How long <paramref name="fresh"/> letters arriving together take to land (0 held still or with none).</summary>
    public static float TypedInMilliseconds(int fresh, bool holdStill = false) => holdStill || fresh <= 0 ? 0f :
        ((fresh - 1) * TypedLetterInStagger) + TypedLetterInMilliseconds;

    /// <summary>A letter deleted <paramref name="since"/> ms ago, falling off the end of the word; null once it has gone (at once held still).</summary>
    public static BigTypeLetterPose? TypedOut(float since, bool holdStill = false)
    {
        if (holdStill || since >= TypedLetterOutMilliseconds)
            return null;
        var e = BigTypeMotion.Leave(Math.Max(0f, since) / TypedLetterOutMilliseconds);
        return new BigTypeLetterPose(0f, TypedLetterOutDropEm * e, TypedLetterOutTilt * e, Math.Clamp(1f - e, 0f, 1f));
    }

    /// <summary>The typed word's size <paramref name="since"/> ms after it was refitted from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float TypedSize(float from, float to, float since, bool holdStill = false)
    {
        if (holdStill || from <= 0f || since >= TypedResizeMilliseconds)
            return to;
        return from + ((to - from) * EaseOut(Math.Max(0f, since) / TypedResizeMilliseconds));
    }

    /// <summary>
    /// Letter <paramref name="order"/> of the name that was on the Stage when typing began, <paramref name="since"/> ms later: leaving
    /// upward on the Leave curve; null once it has gone. Held still, every letter goes together in <see cref="BigTypeMotion.LetterOutStillMilliseconds"/>.
    /// </summary>
    public static BigTypeLetterPose? NameOut(int order, float since, bool holdStill = false)
    {
        var duration = holdStill ? BigTypeMotion.LetterOutStillMilliseconds : TypedNameOutMilliseconds;
        var delay = holdStill ? 0f : Math.Min(Math.Max(0, order) * TypedNameOutStagger, TypedNameOutStaggerTotal);
        var t = (since - delay) / duration;
        if (t >= 1f)
            return null;
        var e = BigTypeMotion.Leave(Math.Max(0f, t));
        return new BigTypeLetterPose(0f, TypedNameOutToEm * e, 0f, 1f - e);
    }

    /// <summary>How long the name before the typed word takes to leave.</summary>
    public static float NameOutMilliseconds(int count, bool holdStill = false) => holdStill ? BigTypeMotion.LetterOutStillMilliseconds :
        Math.Min(Math.Max(0, count - 1) * TypedNameOutStagger, TypedNameOutStaggerTotal) + TypedNameOutMilliseconds;
}
