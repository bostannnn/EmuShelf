namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// Setup's pacing in the Big Type look (M47 B14, the mock's <c>wizScan</c>), as pure functions of time from tokens.js
/// <c>SetupCount</c>. While folders are read the Stage's name is the count of games found, typed as the keyboard's word is (its letters
/// drop in), and a new count is shown at most once every <see cref="CountStepMilliseconds"/>, as the mock's counts follow one another;
/// a count that arrives sooner waits for its turn and the latest one is shown then. Once every folder is read the line says "All set"
/// and home comes back <see cref="DoneHoldMilliseconds"/> later.
/// </summary>
/// <remarks>
/// The pacing is reading time, not motion: holding still (reduced motion, battery saver) it is the same, as the mock still waits its
/// 260 ms and 1100 ms under <c>prefers-reduced-motion</c>; only the count's letters land in place at once (the typed word's own rule).
/// </remarks>
public static class BigTypeSetupMotion
{
    /// <summary>tokens.js <c>SetupCount.stepMs</c>: the least time between two counts on the Stage.</summary>
    public const float CountStepMilliseconds = 260f;

    /// <summary>tokens.js <c>SetupCount.doneHoldMs</c>: how long "All set" stays before home comes back.</summary>
    public const float DoneHoldMilliseconds = 1100f;

    /// <summary>
    /// When a count that arrives at <paramref name="now"/> may be shown, the last one having been shown at <paramref name="lastShown"/>
    /// (null: none yet, so at once). Never before <paramref name="now"/>.
    /// </summary>
    public static float NextCountAt(float? lastShown, float now, bool holdStill = false)
    {
        _ = holdStill;
        if (lastShown is not { } last)
            return now;
        return System.Math.Max(now, last + CountStepMilliseconds);
    }

    /// <summary>How long to wait at <paramref name="now"/> before showing a count (0: show it now).</summary>
    public static float WaitBeforeCount(float? lastShown, float now, bool holdStill = false) =>
        NextCountAt(lastShown, now, holdStill) - now;

    /// <summary>Whether home is due back <paramref name="sinceDone"/> ms after "All set" was shown.</summary>
    public static bool HomeDue(float sinceDone, bool holdStill = false)
    {
        _ = holdStill;
        return sinceDone >= DoneHoldMilliseconds;
    }
}
