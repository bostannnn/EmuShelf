using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// Pick up and put down on the Avalonia Launcher (M47 B9, the mock's startMove, moveKey, endMove and .tile.held), from BigTypeMoveMotion:
// the held tile grows (its style) above its neighbours, its inside rocks on the compositor (so it never reaches a picture), the ring is
// hidden; each trade of places slides both tiles from where they were to their new slots, across pages too; put down, its inside
// squashes and settles and the ring lands on it again. A folder named on the keyboard is born on its slot once the keyboard has gone.
// While any of it moves the native Launcher waits to take the screen back (the Avalonia Launcher draws move mode and the keyboard).
internal sealed partial class HandheldLauncherMotion
{
    // Each home slot (by its index across pages) sliding to its new place: from how far, since when (stamped on the next frame).
    private readonly Dictionary<int, (Vector2 From, long? Start)> _trades = [];
    private (int Index, long? Start)? _putDown;
    private (int Index, long? Start)? _born;
    private long? _heldStart;
    private int _rocking = -1;
    private Control? _rockingFace;
    private bool _holding;

    // A tile is held and nothing is over the grid (Y's Put in sheet has the ring on its rows).
    private bool HoldingTile => _shell is { IsMoving: true, Sheet: null } && HandheldTokens.IsBigType;

    private void AttachPickup(HandheldShellViewModel? old, HandheldShellViewModel? shell)
    {
        if (old is not null)
        {
            old.TilesTraded -= OnTilesTraded;
            old.TilePutDown -= OnTilePutDown;
            old.FolderBorn -= OnFolderBorn;
        }
        if (shell is not null)
        {
            shell.TilesTraded += OnTilesTraded;
            shell.TilePutDown += OnTilePutDown;
            shell.FolderBorn += OnFolderBorn;
        }
    }

    private void OnTilesTraded(IReadOnlyList<(int From, int To)> traded)
    {
        if (!HandheldTokens.IsBigType || !_onScreen || Still)
            return;
        foreach (var (from, to) in traded)
        {
            // Where it is drawn now, if it is still sliding from an earlier trade, is where this slide starts.
            var still = _trades.TryGetValue(from, out var earlier) ? SwapOffset(earlier) : Vector2.Zero;
            _trades[to] = (SlotPlace(from) - SlotPlace(to) + still, null);
        }
        RequestTick();
    }

    private void OnTilePutDown(int index)
    {
        if (!HandheldTokens.IsBigType)
            return;
        _putDown = _onScreen && !Still ? (index, null) : null;
        // The ring comes back on the tile, landing there (the mock's setFocus(i, 'back')).
        _jump = true;
        RequestTick();
    }

    private void OnFolderBorn(int index)
    {
        if (!HandheldTokens.IsBigType)
            return;
        _born = _onScreen && !Still ? (index, null) : null;
        RequestTick();
    }

    /// <summary>A home slot's place on the page track (its page's offset plus its place in the page's grid).</summary>
    private Vector2 SlotPlace(int index)
    {
        if (_screen is not { } screen)
            return Vector2.Zero;
        var at = screen.Grid.PositionOf(index);
        var container = SlotContainer(at.Page, at.Slot) ?? SlotContainer(screen.CurrentPage?.Index ?? 0, at.Slot);
        var position = container?.Bounds.Position ?? default;
        return new Vector2((float)((at.Page * PageWidth) + position.X), (float)position.Y);
    }

    private Control? SlotContainer(int page, int slot) =>
        _pageTrack.ContainerFromIndex(page) is { } pageContainer &&
        pageContainer.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault() is { } slots
            ? slots.ContainerFromIndex(slot)
            : null;

    private Vector2 SwapOffset((Vector2 From, long? Start) trade)
    {
        var since = trade.Start is { } start ? SinceTicks(start) : 0f;
        var (x, y) = BigTypeMoveMotion.Swap(trade.From.X, trade.From.Y, since, Still);
        return new Vector2(x, y);
    }

    /// <summary>Where a home slot's tile is drawn from its slot this frame while it slides there after a trade.</summary>
    private Vector2 TradeOffset(LauncherCell cell)
    {
        if (_trades.Count == 0 || cell.Grid != LauncherGroup.Home || _screen is not { } screen)
            return Vector2.Zero;
        var index = (cell.Page * screen.Grid.SlotsPerPage) + (cell.Row * screen.Grid.Columns) + cell.Column;
        return _trades.TryGetValue(index, out var trade) ? SwapOffset(trade) : Vector2.Zero;
    }

    /// <summary>Each frame: the slides, the put-down squash, the held tile's rock and z, a folder being born. True while any moves.</summary>
    private bool StepPickup()
    {
        var moving = false;
        foreach (var index in _trades.Keys.ToList())
        {
            var trade = _trades[index];
            if (trade.Start is null)
                _trades[index] = trade with { Start = Stopwatch.GetTimestamp() };
            else if (!BigTypeMoveMotion.Swapping(SinceTicks(trade.Start.Value), Still))
                _trades.Remove(index);
        }
        moving |= _trades.Count > 0;
        moving |= StepHeld();
        moving |= StepPutDown();
        moving |= StepBorn();
        return moving;
    }

    // The held tile above its neighbours, its inside rocking (started again, in phase, on the tile at each new slot).
    private bool StepHeld()
    {
        var holding = HoldingTile;
        if (holding && !_holding)
            _heldStart = Stopwatch.GetTimestamp();
        _holding = holding;
        var index = holding && _shell is { } shell ? shell.HomeIndexOfHeld : -1;
        foreach (var (_, container, cell) in GridContainers())
        {
            var held = index >= 0 && cell.Grid == LauncherGroup.Home && _screen is { } screen &&
                (cell.Page * screen.Grid.SlotsPerPage) + (cell.Row * screen.Grid.Columns) + cell.Column == index;
            var z = held ? HeldZIndex : 0;
            if (container.ZIndex != z)
                container.ZIndex = z;
            if (held && Face(container) is { } face && !ReferenceEquals(face, _rockingFace))
                Rock(face);
        }
        if (index < 0 && _rockingFace is not null)
        {
            StopRock();
        }
        _rocking = index;
        return false;
    }

    private const int HeldZIndex = 2;

    // The rock, on the compositor, sampled over one cycle and started at the phase the held tile has reached (so a trade, which gives
    // the tile a new container, does not jump it back to −3.5°); for what is left of the minute the resting motion lives.
    private void Rock(Control face)
    {
        StopRock();
        _rockingFace = face;
        if (!_onScreen || Still || HalfCycles(BigTypeMoveMotion.HeldWiggleMilliseconds * 2) is not { } halves)
            return;
        var phase = _heldStart is { } start ? SinceTicks(start) : 0f;
        const int samples = 24;
        var cycle = BigTypeMoveMotion.HeldWiggleMilliseconds * 2;
        var frames = new (float Progress, double Degrees)[samples + 1];
        for (var i = 0; i <= samples; i++)
        {
            var progress = (float)i / samples;
            frames[i] = (progress, BigTypeMoveMotion.Wiggle(phase + (progress * cycle)));
        }
        HandheldComposition.Rock(face, new Point(face.Bounds.Width / 2, face.Bounds.Height / 2), cycle, Math.Max(1, halves / 2), frames);
    }

    private void StopRock()
    {
        if (_rockingFace is { } face)
            HandheldComposition.Rock(face, default, 1, 1, (0f, 0d), (1f, 0d));
        _rockingFace = null;
    }

    // The face of a home tile: the Big Type part that rocks and squashes (its container keeps the slide and the push).
    private static Control? Face(Control container) =>
        container.GetVisualDescendants().OfType<Panel>().FirstOrDefault(panel => panel.Classes.Contains("bt-face"));

    // Put down: the inside goes 1.12, 0.92, 1 (CSS ease-out over the whole), drawn each frame so it is in place at rest.
    private bool StepPutDown()
    {
        if (_putDown is not { } down)
            return false;
        if (down.Start is null)
        {
            _putDown = down with { Start = Stopwatch.GetTimestamp() };
            down = _putDown.Value;
        }
        var since = SinceTicks(down.Start!.Value);
        var scale = BigTypeMoveMotion.PutDown(since, Still);
        if (HomeContainer(down.Index) is { } container && Face(container) is { } face)
            face.RenderTransform = scale == 1f ? null : new ScaleTransform(scale, scale);
        if (since < BigTypeMoveMotion.PutDownMilliseconds && !Still)
            return true;
        _putDown = null;
        return false;
    }

    // A folder named on the keyboard grows on its slot from nothing, turning upright, once the keyboard has gone (the mock's closeKb).
    private bool StepBorn()
    {
        if (_born is not { } born || _keysExit is not null)
            return _born is not null;
        if (born.Start is null)
        {
            _born = born with { Start = Stopwatch.GetTimestamp() };
            born = _born.Value;
        }
        var since = SinceTicks(born.Start!.Value);
        var (scale, degrees, opacity) = BigTypeTypingMotion.FolderBorn(since, Still);
        if (HomeContainer(born.Index) is { } container && Face(container) is { } face)
        {
            face.RenderTransform = scale == 1f && degrees == 0f ? null
                : new MatrixTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateRotation(degrees * Math.PI / 180));
            face.Opacity = Math.Round(opacity, 3);
        }
        if (since < BigTypeTypingMotion.FolderBornMilliseconds && !Still)
            return true;
        _born = null;
        return false;
    }

    private Control? HomeContainer(int index)
    {
        if (_screen is not { } screen)
            return null;
        var at = screen.Grid.PositionOf(index);
        return SlotContainer(at.Page, at.Slot);
    }

    /// <summary>
    /// How much longer this Launcher must keep the screen for what it is moving that the native one does not draw: a list's rows or the
    /// keyboard's keys leaving, a tile settling after it was put down or traded, a folder being born.
    /// </summary>
    public float ScreenHoldRemaining
    {
        get
        {
            var remaining = Math.Max(ListExitRemaining, KeyboardExitRemaining);
            if (_trades.Count > 0)
                remaining = Math.Max(remaining, BigTypeMoveMotion.SwapMilliseconds);
            if (_putDown is not null)
                remaining = Math.Max(remaining, BigTypeMoveMotion.PutDownMilliseconds);
            if (_born is not null)
                remaining = Math.Max(remaining, BigTypeTypingMotion.FolderBornMilliseconds);
            return remaining;
        }
    }

    private bool _screenHeld;

    /// <summary>Raised once what kept the screen (<see cref="ScreenHoldRemaining"/>) has finished.</summary>
    public event Action? ScreenHoldEnded;

    private void CheckScreenHold()
    {
        var held = ScreenHoldRemaining > 0f;
        if (_screenHeld && !held)
        {
            _screenHeld = false;
            ScreenHoldEnded?.Invoke();
            return;
        }
        _screenHeld = held;
    }
}
