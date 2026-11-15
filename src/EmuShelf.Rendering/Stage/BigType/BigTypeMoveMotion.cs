namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// Pick up and put down in the Big Type look (M47 B9), as pure functions of time from tokens.js and the mock's script (<c>startMove</c>,
/// <c>moveKey</c>, <c>endMove</c>, <c>.tile.held</c>, <c>@keyframes wig</c>, the tile's <c>left</c>/<c>top</c> transition): the held tile
/// grows to <see cref="HeldScale"/> and its inside rocks from side to side; each trade of places slides both tiles to their new slots;
/// put down, its inside squashes and settles. Holding still, everything is where it rests.
/// </summary>
public static class BigTypeMoveMotion
{
    /// <summary>tokens.js <c>shape.HeldScale</c>: the held tile's size.</summary>
    public const float HeldScale = 1.20f;

    /// <summary>tokens.js <c>Held</c>: the held tile's inside rocks from −3.5° to 3.5° and back, 420 ms each way (CSS ease-in-out).</summary>
    public const float HeldWiggleMilliseconds = 420f;
    public const float HeldDegrees = 3.5f;

    /// <summary>tokens.js <c>Swap</c>: a tile trading places slides to its new slot over 360 ms on cubic-bezier(.2, 1.3, .35, 1).</summary>
    public const float SwapMilliseconds = 360f;

    /// <summary>tokens.js <c>PutDown</c>: put down, the tile's inside goes 1.12, 0.92, 1 over 380 ms (CSS ease-out over the whole).</summary>
    public const float PutDownMilliseconds = 380f;
    public const float PutDownFrom = 1.12f;
    public const float PutDownLow = 0.92f;

    /// <summary>The swap's curve, tokens.js <c>Swap.easing</c>.</summary>
    public static float SwapEase(float t) => t <= 0f ? 0f : t >= 1f ? 1f : StageCurves.CubicBezier(0.2f, 1.3f, 0.35f, 1f, t);

    /// <summary>
    /// The held tile's inside, <paramref name="sinceHeld"/> ms after it was picked up: its turn in degrees (the mock's <c>wig</c>, which
    /// starts at −3.5°, runs to 3.5° and back for as long as it is held). Held still, it does not rock.
    /// </summary>
    public static float Wiggle(float sinceHeld, bool holdStill = false)
    {
        if (holdStill)
            return 0f;
        var phase = Math.Max(0f, sinceHeld) / HeldWiggleMilliseconds;
        var swing = (int)Math.Floor(phase);
        var e = BigTypeMotion.EaseInOut(phase - swing);
        return swing % 2 == 0 ? -HeldDegrees + (2f * HeldDegrees * e) : HeldDegrees - (2f * HeldDegrees * e);
    }

    /// <summary>A tile trading places <paramref name="since"/> ms ago, from <paramref name="dx"/>, <paramref name="dy"/> away: where it is from its new slot.</summary>
    public static (float X, float Y) Swap(float dx, float dy, float since, bool holdStill = false)
    {
        if (holdStill || since >= SwapMilliseconds)
            return (0f, 0f);
        var k = 1f - SwapEase(Math.Max(0f, since) / SwapMilliseconds);
        return (dx * k, dy * k);
    }

    /// <summary>Whether a trade of places <paramref name="since"/> ms ago is still sliding.</summary>
    public static bool Swapping(float since, bool holdStill = false) => !holdStill && since < SwapMilliseconds;

    /// <summary>The tile's inside <paramref name="since"/> ms after it was put down: its scale (1.12, then 0.92 halfway, then 1).</summary>
    public static float PutDown(float since, bool holdStill = false)
    {
        if (holdStill || since >= PutDownMilliseconds || since < 0f)
            return 1f;
        var p = BigTypeTypingMotion.EaseOut(since / PutDownMilliseconds);
        return p < 0.5f
            ? PutDownFrom + ((PutDownLow - PutDownFrom) * (p / 0.5f))
            : PutDownLow + ((1f - PutDownLow) * ((p - 0.5f) / 0.5f));
    }
}
