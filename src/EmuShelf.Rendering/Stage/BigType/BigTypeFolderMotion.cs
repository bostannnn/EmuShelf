namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// A grid tile's pose against where it rests: moved by (<paramref name="X"/>, <paramref name="Y"/>) canvas px, scaled about its own middle by
/// <paramref name="Scale"/> (on top of its lift), and drawn at <paramref name="Opacity"/>.
/// </summary>
public readonly record struct BigTypeTilePose(float X, float Y, float Scale, float Opacity)
{
    public static BigTypeTilePose Rest { get; } = new(0f, 0f, 1f, 1f);

    public static BigTypeTilePose Gone { get; } = new(0f, 0f, 1f, 0f);
}

/// <summary>
/// A tile leaving as the mock's Web Animations leave: from wherever it was (its lift, its neighbour's push) toward a target on the Leave
/// curve. <see cref="Progress"/> is the eased progress (0 before its delay, 1 once gone); the caller blends its own resting transform to
/// the target by it. The opacity goes with it, 1 to 0.
/// </summary>
public readonly record struct BigTypeTileLeave(float Progress, float ToX, float ToY, float ToScale)
{
    public float Opacity => Math.Clamp(1f - Progress, 0f, 1f);

    /// <summary>The pose for a tile resting at (<paramref name="fromX"/>, <paramref name="fromY"/>) and <paramref name="fromScale"/>.</summary>
    public (float X, float Y, float Scale) From(float fromX, float fromY, float fromScale) =>
        (fromX + ((ToX - fromX) * Progress), fromY + ((ToY - fromY) * Progress), fromScale + ((ToScale - fromScale) * Progress));
}

/// <summary>
/// Opening and closing a folder under the Big Type look (M47 B8: the mock's <c>openFolder</c>, <c>fallAway</c>, <c>closeFolder</c> and
/// <c>popIn</c>), as pure functions of time from tokens.js <c>motion</c>. Opening: home's tiles fall away from the opened one, the folder
/// shows after <see cref="OutHoldMilliseconds"/>, its games deal in, the ring pops in on the first game. Closing: the games drop out, home
/// shows after <see cref="CloseHoldMilliseconds"/>, its tiles pop back in around the folder's tile, the ring comes back to it. Shared by the
/// native Launcher and the Avalonia one. Held still, nothing travels and nothing waits: the folder (or home) is simply there.
/// </summary>
public static class BigTypeFolderMotion
{
    /// <summary>tokens.js <c>FolderOut</c>: home's tiles fall away from the opened one.</summary>
    public const float OutMilliseconds = 250f;
    public const float OutStagger = 20f;
    public const float OutPush = 56f;
    public const float OutToScale = 0.55f;
    public const float OutOpenedScale = 1.7f;
    public const float OutOpenedDelay = 70f;
    public const int OutStaggerSteps = 6;
    public const float OutHoldMilliseconds = 370f;

    /// <summary>tokens.js <c>FolderDeal</c>: the folder's games deal in.</summary>
    public const float DealMilliseconds = 480f;
    public const float DealStagger = 45f;
    public const float DealRise = 160f;
    public const float DealFromScale = 0.55f;
    public const int DealStaggerSteps = 8;
    public const float DealRingAfter = 230f;

    /// <summary>tokens.js <c>FolderClose</c>: the games drop out.</summary>
    public const float CloseMilliseconds = 200f;
    public const float CloseStagger = 16f;
    public const float CloseDrop = 120f;
    public const float CloseToScale = 0.6f;
    public const int CloseStaggerFrom = 5;
    public const float CloseHoldMilliseconds = 300f;
    public const float CloseRingAfter = 130f;

    /// <summary>tokens.js <c>FolderPopIn</c>: home's tiles pop back in.</summary>
    public const float PopInMilliseconds = 440f;
    public const float PopInStagger = 30f;
    public const float PopInFromScale = 0.45f;
    public const int PopInStaggerSteps = 6;

    /// <summary>tokens.js <c>RingPop.folderFromScale</c>: the ring popping in on a folder's first game.</summary>
    public const float RingPopFromScale = 1.3f;

    /// <summary>The distance between two cells, as the mock's <c>Math.hypot</c>.</summary>
    public static float Distance(int columns, int rows) => MathF.Sqrt((columns * columns) + (rows * rows));

    /// <summary>
    /// A home tile <paramref name="since"/> ms after a folder began to open, standing (<paramref name="columns"/>, <paramref name="rows"/>)
    /// cells from the opened one (<paramref name="opened"/>: it is the opened one): it is pushed away and shrinks, or grows; all fade.
    /// </summary>
    public static BigTypeTileLeave FallAway(int columns, int rows, bool opened, float since, bool holdStill = false)
    {
        var to = opened ? (0f, 0f, OutOpenedScale) : (columns * OutPush, rows * OutPush, OutToScale);
        if (holdStill)
            return new BigTypeTileLeave(1f, to.Item1, to.Item2, to.Item3);
        var delay = opened ? OutOpenedDelay : Math.Min(Distance(columns, rows), OutStaggerSteps) * OutStagger;
        return new BigTypeTileLeave(BigTypeMotion.Leave(Math.Clamp((since - delay) / OutMilliseconds, 0f, 1f)), to.Item1, to.Item2, to.Item3);
    }

    /// <summary>Whether home has gone (the folder shows) <paramref name="since"/> ms after the press.</summary>
    public static bool HomeGone(float since, bool holdStill = false) => holdStill || since >= OutHoldMilliseconds;

    /// <summary>
    /// A folder's game at (<paramref name="column"/>, <paramref name="row"/>) of its window, <paramref name="sinceDeal"/> ms after the deal
    /// began (the folder showing): how far below its place it is, its scale and its opacity, on the Land curve.
    /// </summary>
    public static BigTypeTilePose Deal(int column, int row, float sinceDeal, bool holdStill = false)
    {
        if (holdStill)
            return BigTypeTilePose.Rest;
        var local = sinceDeal - (Math.Min(Math.Max(0, column) + Math.Max(0, row), DealStaggerSteps) * DealStagger);
        if (local >= DealMilliseconds)
            return BigTypeTilePose.Rest;
        if (local <= 0f)
            return new BigTypeTilePose(0f, DealRise, DealFromScale, 0f);
        var e = BigTypeMotion.Land(local / DealMilliseconds);
        return new BigTypeTilePose(0f, DealRise * (1f - e), DealFromScale + ((1f - DealFromScale) * e), Math.Clamp(e, 0f, 1f));
    }

    /// <summary>How long a deal takes once the folder shows.</summary>
    public static float DealTotal => (DealStaggerSteps * DealStagger) + DealMilliseconds;

    /// <summary>When the ring pops in after a folder's press (or at once, held still).</summary>
    public static float OpenRingAt(bool holdStill = false) => holdStill ? 0f : OutHoldMilliseconds + DealRingAfter;

    /// <summary>
    /// A folder's game at (<paramref name="column"/>, <paramref name="row"/>) of its window, <paramref name="since"/> ms after the folder
    /// began to close: it drops and shrinks as it fades, the ones nearest the top left last.
    /// </summary>
    public static BigTypeTileLeave Drop(int column, int row, float since, bool holdStill = false)
    {
        if (holdStill)
            return new BigTypeTileLeave(1f, 0f, CloseDrop, CloseToScale);
        var delay = Math.Max(0, CloseStaggerFrom - Math.Max(0, column) - Math.Max(0, row)) * CloseStagger;
        return new BigTypeTileLeave(BigTypeMotion.Leave(Math.Clamp((since - delay) / CloseMilliseconds, 0f, 1f)), 0f, CloseDrop, CloseToScale);
    }

    /// <summary>Whether the folder has gone (home shows) <paramref name="since"/> ms after the press.</summary>
    public static bool FolderGone(float since, bool holdStill = false) => holdStill || since >= CloseHoldMilliseconds;

    /// <summary>
    /// A home tile standing (<paramref name="columns"/>, <paramref name="rows"/>) cells from the tile the pop centres on,
    /// <paramref name="sincePop"/> ms after home came back: it grows from <see cref="PopInFromScale"/> and fades in on the Land curve.
    /// </summary>
    public static BigTypeTilePose PopIn(int columns, int rows, float sincePop, bool holdStill = false)
    {
        if (holdStill)
            return BigTypeTilePose.Rest;
        var local = sincePop - (Math.Min(Distance(columns, rows), PopInStaggerSteps) * PopInStagger);
        if (local >= PopInMilliseconds)
            return BigTypeTilePose.Rest;
        if (local <= 0f)
            return new BigTypeTilePose(0f, 0f, PopInFromScale, 0f);
        var e = BigTypeMotion.Land(local / PopInMilliseconds);
        return new BigTypeTilePose(0f, 0f, PopInFromScale + ((1f - PopInFromScale) * e), Math.Clamp(e, 0f, 1f));
    }

    /// <summary>How long a pop-in takes once home shows.</summary>
    public static float PopInTotal => (PopInStaggerSteps * PopInStagger) + PopInMilliseconds;

    /// <summary>When the ring is back after a folder's close (or at once, held still).</summary>
    public static float CloseRingAt(bool holdStill = false) => holdStill ? 0f : CloseHoldMilliseconds + CloseRingAfter;

    /// <summary>How long opening takes, from the press until the last game has landed.</summary>
    public static float OpenTotal(bool holdStill = false) => holdStill ? 0f : OutHoldMilliseconds + DealTotal;

    /// <summary>How long closing takes, from the press until the last home tile has landed.</summary>
    public static float CloseTotal(bool holdStill = false) => holdStill ? 0f : CloseHoldMilliseconds + PopInTotal;

    /// <summary>
    /// The ring popping in on a folder's first game <paramref name="since"/> ms after it shows (tokens.js <c>RingPop</c>): its scale, from
    /// <see cref="RingPopFromScale"/>, and its opacity, on the Land curve.
    /// </summary>
    public static (float Scale, float Opacity) RingPop(float since, bool holdStill = false)
    {
        if (holdStill || since >= BigTypeMotion.PopMilliseconds)
            return (1f, 1f);
        if (since <= 0f)
            return (RingPopFromScale, 0f);
        var e = BigTypeMotion.Land(since / BigTypeMotion.PopMilliseconds);
        return (RingPopFromScale + ((1f - RingPopFromScale) * e), Math.Clamp(e, 0f, 1f));
    }
}
