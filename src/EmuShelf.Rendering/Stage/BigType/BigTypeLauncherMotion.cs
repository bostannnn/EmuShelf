using System.Numerics;

namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// A tile's place in a Launcher grid (M47 B5): which grid (home or an open folder), the home page it is on (0 in a folder), and
/// its column and row there. Neighbours are tiles of the same grid and page one step away; the idle hop runs along the diagonal.
/// </summary>
public readonly record struct LauncherCell(LauncherGroup Grid, int Page, int Column, int Row);

/// <summary>
/// The Big Type Launcher's motion as state over time (M47 B5), with no drawing: the one ring's path from where it was drawn to
/// its new target, its pop onto another page and its breath at rest; the neighbours making room around the focused tile, its bob,
/// and the idle hop. The native Launcher asks it where things are each frame; the Avalonia Launcher uses it for the ring and the
/// neighbours. While <see cref="HoldStill"/> is set (battery saver, reduced motion) nothing animates: the ring and the neighbours
/// land at once, and nothing breathes, bobs or hops.
/// </summary>
public sealed class BigTypeLauncherMotion
{
    private const float Never = float.NegativeInfinity;

    private BigTypeRing? _drawn;
    private BigTypeRing _from;
    private float _moveStart = Never;
    private float _popStart = Never;
    private float _landed = Never;

    private readonly Dictionary<string, Tile> _tiles = new(StringComparer.Ordinal);
    private string? _focused;
    private LauncherCell? _focusCell;
    private float _focusStart = Never;

    private sealed class Tile(LauncherCell cell)
    {
        public LauncherCell Cell { get; set; } = cell;
        public Vector2 From { get; set; }
        public Vector2 To { get; set; }
        public float Start { get; set; } = Never;

        public Vector2 At(float now)
        {
            var since = now - Start;
            return since >= BigTypeMotion.LiftMilliseconds
                ? To
                : new Vector2(BigTypeMotion.Settle(From.X, To.X, since), BigTypeMotion.Settle(From.Y, To.Y, since));
        }
    }

    /// <summary>The device asks for stillness: the ring and the neighbours jump, nothing breathes, bobs or hops.</summary>
    public bool HoldStill { get; set; }

    /// <summary>Whether <see cref="Ring"/> adds the breath at rest (the Avalonia Launcher breathes on its compositor instead).</summary>
    public bool Breathes { get; init; } = true;

    /// <summary>When the last press came (NaN before the first): the breath and the bob rest a minute after it, and the hop waits on it.</summary>
    public float LastInput { get; private set; } = float.NaN;

    /// <summary>The grid the hop runs over (home, or the open folder), its page on home, and a folder's first column on screen.</summary>
    public LauncherGroup HopGrid { get; set; } = LauncherGroup.Home;

    public int HopPage { get; set; }

    public int HopFirstColumn { get; set; }

    /// <summary>A press or a touch.</summary>
    public void Input(float now) => LastInput = now;

    /// <summary>How much of the resting motion is alive now (1 for a minute after a press, then easing to 0).</summary>
    public float Alive(float now) => BigTypeMotion.Alive(now - LastInput, HoldStill);

    // ---- The ring ----

    /// <summary>
    /// The focus moved: the ring sets off from where it was last drawn toward its new target (see <see cref="Ring"/>), or with
    /// <paramref name="pop"/> lands there at once and pops in (a move onto another page), or with <paramref name="instant"/> (its
    /// first appearance, a screen coming back, holding still) simply lands.
    /// </summary>
    public void MoveRing(float now, bool instant, bool pop)
    {
        if (_drawn is not { } drawn || instant || HoldStill)
        {
            _moveStart = _popStart = Never;
            _landed = now;
            _drawn = null;
            return;
        }
        if (pop)
        {
            _moveStart = Never;
            _popStart = now;
            _landed = now + BigTypeMotion.PopMilliseconds;
            return;
        }
        _from = drawn;
        _moveStart = now;
        _popFrom = BigTypeMotion.PopFromScale;
    }

    /// <summary>
    /// The ring pops in where its target is, from <paramref name="from"/> times its size, whatever it did before (M47 B8: on a folder's
    /// first game, tokens.js <c>RingPop.folderFromScale</c>). Held still it simply lands.
    /// </summary>
    public void PopIn(float now, float from)
    {
        _moveStart = Never;
        if (HoldStill)
        {
            _popStart = Never;
            _landed = now;
            _drawn = null;
            return;
        }
        _popStart = now;
        _popFrom = from;
        _landed = now + BigTypeMotion.PopMilliseconds;
    }

    private float _popFrom = BigTypeMotion.PopFromScale;

    /// <summary>
    /// Where to draw the ring now, given where its target is now (it rides a sliding page), and how opaque; <paramref name="moving"/>
    /// is set while the ring travels or pops (drawn at the panel's rate). At rest it breathes while the resting motion is alive.
    /// </summary>
    public BigTypeRing Ring(BigTypeRing target, float now, out float opacity, out bool moving)
    {
        opacity = 1f;
        moving = false;
        var at = target;
        var sinceMove = now - _moveStart;
        if (sinceMove < BigTypeMotion.RingMilliseconds)
        {
            at = BigTypeMotion.RingAt(_from, target, sinceMove / BigTypeMotion.RingMilliseconds);
            moving = true;
        }
        else if (_moveStart > _landed)
        {
            _landed = _moveStart + BigTypeMotion.RingMilliseconds;
        }
        var sincePop = now - _popStart;
        if (sincePop < BigTypeMotion.PopMilliseconds)
        {
            var (scale, fade) = BigTypeMotion.Pop(sincePop, _popFrom);
            at = at.Scaled(scale);
            opacity = fade;
            moving = true;
        }
        if (!moving && Breathes)
            at = at.Scaled(BigTypeMotion.Breath(now - _landed, Alive(now)));
        _drawn = at;
        return at;
    }

    // ---- The tiles ----

    /// <summary>A tile with a place in a grid came (or moved); null forgets it. A tile seen for the first time takes its place at once.</summary>
    public void Place(string id, LauncherCell? cell)
    {
        if (cell is not { } place)
        {
            _tiles.Remove(id);
            return;
        }
        // The focused tile placed after the focus came to it: its neighbours make room now.
        if (id == _focused && _focusCell != place)
        {
            _focusCell = place;
            foreach (var other in _tiles.Values)
            {
                other.From = other.To = PushFor(other.Cell);
                other.Start = Never;
            }
        }
        if (_tiles.TryGetValue(id, out var tile))
        {
            if (tile.Cell == place)
                return;
            tile.Cell = place;
        }
        else
        {
            tile = new Tile(place);
            _tiles[id] = tile;
        }
        tile.From = tile.To = PushFor(place);
        tile.Start = Never;
    }

    /// <summary>
    /// The focus is on <paramref name="id"/> now (null, or a piece with no place in a grid: nothing is pushed): the tiles around
    /// it make room on the landing curve and the rest come back, at once when <paramref name="instant"/> or holding still.
    /// </summary>
    public void Focus(string? id, float now, bool instant, LauncherCell? cell = null)
    {
        if (id == _focused)
            return;
        _focused = id;
        _focusCell = cell ?? (id is not null && _tiles.TryGetValue(id, out var focused) ? focused.Cell : null);
        _focusStart = now;
        foreach (var tile in _tiles.Values)
        {
            var to = PushFor(tile.Cell);
            if (to == tile.To)
                continue;
            tile.From = instant || HoldStill ? to : tile.At(now);
            tile.To = to;
            tile.Start = instant || HoldStill ? Never : now;
        }
    }

    private Vector2 PushFor(LauncherCell cell)
    {
        if (_focusCell is not { } focus || focus.Grid != cell.Grid || focus.Page != cell.Page)
            return Vector2.Zero;
        var (x, y) = BigTypeMotion.Push(focus.Column, focus.Row, cell.Column, cell.Row);
        return new Vector2(x, y);
    }

    /// <summary>
    /// How far a tile is moved from its place now: its push, the focused tile's bob, and its part of an idle hop (unless
    /// <paramref name="hops"/> is false: a page is zooming, a Title page is up).
    /// </summary>
    public Vector2 Offset(string id, float now, bool hops = true)
    {
        if (!_tiles.TryGetValue(id, out var tile))
            return Vector2.Zero;
        var offset = tile.At(now);
        if (id == _focused)
            offset.Y += BigTypeMotion.Bob(now - _focusStart, Alive(now));
        if (hops && HopSince(now) is { } since && HopsHere(tile.Cell, out var column))
            offset.Y += BigTypeMotion.Hop(column, tile.Cell.Row, since);
        return offset;
    }

    /// <summary>The push alone (the Avalonia Launcher bobs and hops on the compositor).</summary>
    public Vector2 Push(string id, float now) => _tiles.TryGetValue(id, out var tile) ? tile.At(now) : Vector2.Zero;

    private float? HopSince(float now)
    {
        var since = now - LastInput;
        return BigTypeMotion.HopStart(since, HoldStill) is { } start ? since - start : null;
    }

    private bool HopsHere(LauncherCell cell, out int column)
    {
        column = cell.Grid == LauncherGroup.Folder ? cell.Column - HopFirstColumn : cell.Column;
        return cell.Grid == HopGrid && (cell.Grid != LauncherGroup.Home || cell.Page == HopPage);
    }

    /// <summary>Whether something moves at the panel's rate: a neighbour on its way, or a hop's wave.</summary>
    public bool IsMoving(float now, bool hops = true)
    {
        foreach (var tile in _tiles.Values)
        {
            if (now - tile.Start < BigTypeMotion.LiftMilliseconds)
                return true;
        }
        if (!hops || HopSince(now) is not { } since)
            return false;
        var last = 0;
        foreach (var tile in _tiles.Values)
        {
            if (HopsHere(tile.Cell, out var column))
                last = Math.Max(last, BigTypeMotion.HopIndex(column, tile.Cell.Row));
        }
        return since < BigTypeMotion.HopWaveMilliseconds(last);
    }

    /// <summary>
    /// When nothing moves at the panel's rate, how soon the next frame is wanted: the breath and the bob step at 30 Hz while the
    /// resting motion is alive (a hop starts on one of those steps); null once it has rested, or while holding still.
    /// </summary>
    public float? NextFrameMilliseconds(float now) => Alive(now) > 0f ? BigTypeMotion.AliveStepMilliseconds : null;
}
