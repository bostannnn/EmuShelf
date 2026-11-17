namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The launch and the in-game companion under the Big Type look (M47 B8: the mock's <c>launch</c>, <c>playTab</c>, <c>playWake</c> and
/// <c>endPlay</c>), as pure functions of time from tokens.js. Every time is from the press that launched (the shell's
/// <c>LaunchStarting</c>) unless it says otherwise. Shared by the native screens and the Avalonia ones. Held still, nothing travels: the
/// tile is gone, the colour and the title are there, the companion's parts are in place.
/// </summary>
public static class BigTypeLaunchMotion
{
    /// <summary>tokens.js <c>Launch</c>.</summary>
    public const float FlightMilliseconds = 340f;
    public const float FlightScale = 1.25f;
    public const float FlightBeyond = 340f;
    public const float BumpAt = 270f;
    public const float BumpMilliseconds = 420f;
    public const float BumpScale = 1.16f;
    public const float BumpRise = 20f;
    public const float TakeoverAt = 430f;
    public const float TakeoverMilliseconds = 640f;
    public const float TakeoverX = 1484f;
    public const float TakeoverY = 516f;
    public const float TakeoverRadius = 2200f;
    public const float TitleFromEm = 0.8f;
    public const float GridFadeMilliseconds = 280f;
    public const float CompanionAt = 710f;

    /// <summary>tokens.js <c>InGameIn</c>, <c>InGameTab</c>, <c>InGameOut</c>, <c>UnlockLine</c>, <c>InGameRest</c>, <c>SessionAchievementBar</c>.</summary>
    public const float PartInMilliseconds = 460f;
    public const float PartInStagger = 55f;
    public const float PartInRise = 80f;
    public const float TabMilliseconds = 420f;
    public const float TabSlide = 120f;
    public const float PartOutMilliseconds = 200f;
    public const float PartOutStagger = 18f;
    public const float PartOutDrop = 60f;
    public const float LaunchFadeMilliseconds = 320f;
    public const float ReturnHoldMilliseconds = 340f;
    public const float ReturnRingAfter = 130f;
    public const float UnlockLineHoldMilliseconds = 3200f;
    public const float RestAfterMilliseconds = 9000f;
    public const float RestFadeMilliseconds = 1200f;
    public const float BarMilliseconds = 800f;

    /// <summary>tokens.js <c>Message</c>: how long a message holds the Stage's line (an error longer), and an error's shake.</summary>
    public const float HoldMilliseconds = 2800f;
    public const float ErrorHoldMilliseconds = 4200f;
    public const float ShakeMilliseconds = 380f;
    public const float Shake = 22f;
    public const float ShakeBack = 18f;
    public const float ShakeSettle = 12f;

    /// <summary>tokens.js <c>type.LaunchTitle</c>: the title's size by its length (the mock's <c>gm.t.length</c>).</summary>
    public static float TitleSize(string title) => title.Length > 22 ? 128f : title.Length > 12 ? 160f : 224f;

    private static float FlightEase(float t) => StageCurve(0.6f, 0f, 0.9f, 0.5f, t);

    private static float TakeoverEase(float t) => StageCurve(0.3f, 0.6f, 0.2f, 1f, t);

    private static float EaseOut(float t) => StageCurve(0f, 0f, 0.58f, 1f, t);

    private static float BarEase(float t) => StageCurve(0.2f, 1.3f, 0.3f, 1f, t);

    private static float StageCurve(float x1, float y1, float x2, float y2, float t) =>
        t <= 0f ? 0f : t >= 1f ? 1f : StageCurves.CubicBezier(x1, y1, x2, y2, t);

    /// <summary>
    /// The launched tile <paramref name="since"/> ms after the press, its top <paramref name="tileTop"/> px down its screen: how far it has
    /// risen (negative: up) and its scale. It never fades; it leaves the screen (the mock's <c>fill: forwards</c>).
    /// </summary>
    public static (float OffsetY, float Scale) Flight(float since, float tileTop, bool holdStill = false)
    {
        var e = holdStill ? 1f : FlightEase(Math.Clamp(since / FlightMilliseconds, 0f, 1f));
        return (-(tileTop + FlightBeyond) * e, 1f + ((FlightScale - 1f) * e));
    }

    /// <summary>Whether the launched tile has left its screen.</summary>
    public static bool Flown(float since, bool holdStill = false) => holdStill || since >= FlightMilliseconds;

    /// <summary>
    /// The case on the Stage <paramref name="since"/> ms after the press: its scale about its middle and how far it has risen (px, up),
    /// the mock's <c>scale(1.16) translateY(-10px)</c> keyframe halfway, on ease-out as a whole.
    /// </summary>
    public static (float Scale, float Rise) CaseBump(float since, bool holdStill = false)
    {
        var local = since - BumpAt;
        if (holdStill || local <= 0f || local >= BumpMilliseconds)
            return (1f, 0f);
        var p = EaseOut(local / BumpMilliseconds);
        var q = p < 0.5f ? p / 0.5f : (1f - p) / 0.5f;
        var scale = 1f + ((BumpScale - 1f) * q);
        return (scale, BumpRise * q * scale);
    }

    /// <summary>The colour's circle on the Stage <paramref name="since"/> ms after the press (0 until it starts).</summary>
    public static float TakeoverRadiusAt(float since, bool holdStill = false)
    {
        if (holdStill)
            return TakeoverRadius;
        var local = since - TakeoverAt;
        return local <= 0f ? 0f : TakeoverRadius * TakeoverEase(Math.Min(1f, local / TakeoverMilliseconds));
    }

    /// <summary>Whether the takeover has covered the Stage.</summary>
    public static bool TakenOver(float since, bool holdStill = false) => holdStill || since >= TakeoverAt + TakeoverMilliseconds;

    /// <summary>
    /// Letter <paramref name="index"/> of the launch title <paramref name="since"/> ms after the press: below its place (em), its tilt and
    /// opacity (the mock's <c>lettersIn(lw, .8)</c>, as the takeover starts).
    /// </summary>
    public static (float Em, float Degrees, float Opacity) TitleLetter(int index, float since, bool holdStill = false) =>
        BigTypeListMotion.HeadingLetter(index, since - TakeoverAt, TitleFromEm, holdStill);

    /// <summary>When the launch's motion on the Stage is over: the circle has covered it and every title letter has landed.</summary>
    public static float StageTotal(int letters, bool holdStill = false) =>
        holdStill ? 0f : Math.Max(TakeoverAt + TakeoverMilliseconds, TakeoverAt + BigTypeListMotion.HeadingInTotal(letters));

    /// <summary>The Launcher's grid as the takeover starts: it fades (CSS <c>ease</c>) to nothing.</summary>
    public static float GridOpacity(float since, bool holdStill = false)
    {
        if (holdStill)
            return since >= 0f ? 0f : 1f;
        var local = since - TakeoverAt;
        return local <= 0f ? 1f : 1f - BigTypeMotion.CssEase(Math.Min(1f, local / GridFadeMilliseconds));
    }

    /// <summary>
    /// Part <paramref name="index"/> of the companion (kicker, title, tabs, the tab's body, the bar) <paramref name="since"/> ms after it was
    /// built: how far below its place it is and its opacity.
    /// </summary>
    public static (float Offset, float Opacity) PartIn(int index, float since, bool holdStill = false)
    {
        var local = since - (Math.Max(0, index) * PartInStagger);
        if (holdStill || local >= PartInMilliseconds)
            return (0f, 1f);
        if (local <= 0f)
            return (PartInRise, 0f);
        var e = BigTypeMotion.Land(local / PartInMilliseconds);
        return (PartInRise * (1f - e), Math.Clamp(e, 0f, 1f));
    }

    /// <summary>How long the companion's <paramref name="parts"/> parts take to arrive.</summary>
    public static float PartsInTotal(int parts) => (Math.Max(0, parts - 1) * PartInStagger) + PartInMilliseconds;

    /// <summary>Part <paramref name="index"/> of the companion <paramref name="since"/> ms after the game ended: how far it has dropped, its opacity.</summary>
    public static (float Offset, float Opacity) PartOut(int index, float since, bool holdStill = false)
    {
        var local = since - (Math.Max(0, index) * PartOutStagger);
        if (holdStill || local >= PartOutMilliseconds)
            return (PartOutDrop, 0f);
        if (local <= 0f)
            return (0f, 1f);
        var e = BigTypeMotion.Leave(local / PartOutMilliseconds);
        return (PartOutDrop * e, Math.Clamp(1f - e, 0f, 1f));
    }

    /// <summary>The Stage's colour, title and status <paramref name="since"/> ms after the game ended: fading out, linearly.</summary>
    public static float LaunchFade(float since, bool holdStill = false) =>
        holdStill ? 0f : 1f - Math.Clamp(since / LaunchFadeMilliseconds, 0f, 1f);

    /// <summary>
    /// A tab's body <paramref name="since"/> ms after it was chosen, coming from the side moved to (<paramref name="direction"/> 1: from
    /// the right): how far from its place it is (along x) and its opacity.
    /// </summary>
    public static (float Offset, float Opacity) TabIn(int direction, float since, bool holdStill = false)
    {
        if (holdStill || since >= TabMilliseconds)
            return (0f, 1f);
        if (since <= 0f)
            return (direction * TabSlide, 0f);
        var e = BigTypeMotion.Land(since / TabMilliseconds);
        return (direction * TabSlide * (1f - e), Math.Clamp(e, 0f, 1f));
    }

    /// <summary>The achievements bar's share <paramref name="since"/> ms after it was set from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static float Bar(float from, float to, float since, bool holdStill = false) =>
        holdStill || since >= BarMilliseconds ? to : from + ((to - from) * BarEase(Math.Max(0f, since) / BarMilliseconds));

    /// <summary>
    /// The companion's opacity <paramref name="since"/> ms after it began to rest (<paramref name="resting"/>) or woke: toward
    /// <paramref name="restOpacity"/> over the rest's fade (CSS <c>ease</c>), back to 1 the same way.
    /// </summary>
    public static float RestOpacity(float from, bool resting, float since, float restOpacity, bool holdStill = false)
    {
        var to = resting ? restOpacity : 1f;
        if (holdStill || since >= RestFadeMilliseconds)
            return to;
        return from + ((to - from) * BigTypeMotion.CssEase(Math.Max(0f, since) / RestFadeMilliseconds));
    }

    /// <summary>An error message's shake <paramref name="since"/> ms after it showed: its offset along x (the mock's <c>toast</c>).</summary>
    public static float ShakeAt(float since, bool holdStill = false)
    {
        if (holdStill || since <= 0f || since >= ShakeMilliseconds)
            return 0f;
        var p = since / ShakeMilliseconds * 4f;
        ReadOnlySpan<float> keys = [0f, -Shake, ShakeBack, -ShakeSettle, 0f];
        var i = Math.Min(3, (int)p);
        return keys[i] + ((keys[i + 1] - keys[i]) * (p - i));
    }
}
