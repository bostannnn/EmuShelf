using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The Big Type look's motion on the Avalonia Launcher (M47 B5), the same as the native Launcher's from the same maths
// (BigTypeMotion, BigTypeLauncherMotion): the one ring's timed path, the neighbours making room (a transform on each tile's
// container, so a picture at rest shows it), and on the compositor only, so they never reach a picture or a design frame: the
// ring's breath, the focused tile's bob and the idle hop. Each of those ends at rest after the minute the native side breathes.
internal sealed partial class HandheldLauncherMotion
{
    private readonly BigTypeLauncherMotion _bt = new() { Breathes = false };
    private readonly Stopwatch _btClock = Stopwatch.StartNew();
    private Control? _ringAnchor;
    private bool _ringMoving;
    private bool _breathing;
    private bool _paged;
    private Control? _bobbing;
    private DispatcherTimer? _hopTimer;

    private float BtNow => (float)_btClock.Elapsed.TotalMilliseconds;

    private bool Still => _shell?.HoldsStill == true;

    private static readonly SplineEasing EaseInOut = new(0.42, 0, 0.58, 1);

    // ---- Holding still, input ----

    /// <summary>The device's say on motion, and whether this view is on screen, reach the look's motion and its rolling text.</summary>
    private void UpdateStill()
    {
        HandheldRollingText.SetRolls(_view, _onScreen && !Still);
        if (!HandheldTokens.IsBigType)
            return;
        _view.FindControl<Panel>("Root")?.Classes.Set("still", Still);
        _bt.HoldStill = Still;
        if (Still)
            RestBigType(keepPushes: true);
    }

    private void OnInput()
    {
        if (!HandheldTokens.IsBigType)
            return;
        var now = BtNow;
        var wasAlive = _bt.Alive(now) > 0f;
        _bt.Input(now);
        ArmHop();
        // A press after the resting motion has stopped wakes it again (one that comes while it is alive keeps it going).
        if (!wasAlive && _onScreen && !Still)
        {
            if (!_ringMoving)
                StartBreath();
            if (_bobbing is { } tile)
                StartBob(tile);
        }
    }

    /// <summary>Everything this view moves on its own comes to rest at once (it goes off screen, or the device asks for stillness).</summary>
    private void RestBigType(bool keepPushes = false)
    {
        if (!HandheldTokens.IsBigType)
            return;
        StopBreath();
        if (_bobbing is { } tile)
            HandheldComposition.OffsetFrames(tile, 1, new LinearEasing(), 0, (0f, default), (1f, default));
        _hopTimer?.Stop();
        if (!keepPushes)
        {
            foreach (var (_, container, _) in GridContainers())
                container.RenderTransform = null;
        }
    }

    // ---- The ring ----

    /// <summary>
    /// Big Type's ring: a timed path from where it was drawn to the newly focused element (it covers both by 38%, then lets go and
    /// lands, squashed when the two are the same size), changing its size and radius on the way; it pops in on another page.
    /// </summary>
    private bool StepRing(Control anchor, Rect rect, double radius)
    {
        if (AwaitsListAnchor(anchor))
        {
            _selector.Opacity = 0;
            return false;
        }
        // M47 B9: hidden while a tile is held (the mock's startMove) and for the keyboard's first 260 ms (its openKb); it then lands.
        if (HoldingTile || KeyboardRingWaits)
        {
            _selector.Opacity = 0;
            _ringAnchor = null;
            return KeyboardRingWaits;
        }
        var now = BtNow;
        var target = new BigTypeRing((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height, (float)radius);
        // A folder opening or closing, a launch, the shelf coming back: the ring waits, then pops in or lands (M47 B8).
        if (RingWaits(now))
        {
            _selector.Opacity = 0;
            _ringAnchor = anchor;
            _selectorShown = true;
            return true;
        }
        if (!_selectorShown)
        {
            _bt.MoveRing(now, instant: true, pop: false);
            _ringAnchor = anchor;
            _selectorShown = true;
        }
        else if (!ReferenceEquals(anchor, _ringAnchor))
        {
            _ringAnchor = anchor;
            StopBreath();
            // Into a list or back out of one it simply lands (M47 B7, the mock's 'open' and 'back').
            _bt.MoveRing(now, instant: TakeJump() || Still, pop: _paged);
        }
        _paged = false;
        var ring = _bt.Ring(target, now, out var opacity, out var moving);
        var bumping = false;
        var bump = StepBump(ring.Width, ring.Height, ref bumping);

        _selector.Opacity = ListLeaving ? 0 : Math.Round(opacity, 3);
        _glow.Opacity = 0;
        var width = Math.Round(ring.Width, 1);
        var height = Math.Round(ring.Height, 1);
        if (Math.Abs(_selector.Width - width) > 0.05 || double.IsNaN(_selector.Width))
            _selector.Width = width;
        if (Math.Abs(_selector.Height - height) > 0.05 || double.IsNaN(_selector.Height))
            _selector.Height = height;
        var r = Math.Max(0, (double)ring.Radius);
        // At rest it takes the exact radius, so it always rests the same however the frames fell (as B0 made the springs do).
        if (Math.Abs(_ring.CornerRadius.TopLeft - r) > 0.25 || (!moving && _ring.CornerRadius.TopLeft != r))
        {
            _ring.CornerRadius = new CornerRadius(r);
            ((Border)_ring.Child!).CornerRadius = new CornerRadius(Math.Max(0, r - HandheldTokens.ClassicRingKeylineWidth));
            ((Border)((Border)_ring.Child!).Child!).CornerRadius = new CornerRadius(Math.Max(0, r - (HandheldTokens.ClassicRingKeylineWidth + HandheldTokens.RingStroke)));
        }
        _selectorShift.X = ring.X + bump.X;
        _selectorShift.Y = ring.Y + bump.Y;
        if (!moving && !_breathing)
            StartBreath();
        _ringMoving = moving;
        return moving || bumping;
    }

    // The breath: 1 → 1.03 → 1 every 2.2 s about the ring's middle, on the compositor, for what is left of the minute.
    private void StartBreath()
    {
        if (!_selectorShown || double.IsNaN(_selector.Width) || double.IsNaN(_selector.Height))
            return;
        _breathing = true;
        if (!_onScreen || Still || HalfCycles(BigTypeMotion.BreathPeriodMilliseconds) is not { } count)
            return;
        var size = new Point(_selector.Width / 2, _selector.Height / 2);
        HandheldComposition.Swing(_selector, "Scale", size, new Vector3D(1, 1, 1),
            new Vector3D(BigTypeMotion.BreathScale, BigTypeMotion.BreathScale, 1), BigTypeMotion.BreathPeriodMilliseconds / 2, count, EaseInOut);
    }

    private void StopBreath()
    {
        if (_breathing)
            HandheldComposition.Scale(_selector, 1, 1, 1);
        _breathing = false;
    }

    // How many half cycles of a swing fit in what is left of the minute the resting motion lives (even, so it ends at rest).
    private int? HalfCycles(float period)
    {
        var left = StageRest.AfterMilliseconds - (BtNow - _bt.LastInput);
        if (float.IsNaN(left))
            left = StageRest.AfterMilliseconds;
        if (left <= 0)
            return null;
        var halves = (int)Math.Ceiling(left / (period / 2));
        return Math.Max(2, halves + (halves % 2));
    }

    // ---- The tiles ----

    /// <summary>The focus moved: the tiles around it make room, and it bobs.</summary>
    private void FocusBigType(Control anchor)
    {
        var now = BtNow;
        foreach (var (id, _, cell) in GridContainers())
            _bt.Place(id, cell);
        var focused = Tile(anchor);
        _bt.Focus(focused?.Id, now, instant: !_onScreen || Still, focused?.Cell);
        if (_bobbing is { } old && !ReferenceEquals(old, anchor))
            HandheldComposition.OffsetFrames(old, 1, new LinearEasing(), 0, (0f, default), (1f, default));
        _bobbing = focused is null ? null : anchor;
        if (_bobbing is { } tile)
            StartBob(tile);
    }

    // The bob: up 6 px and back, 1.5 s each way, on the compositor.
    private void StartBob(Control tile)
    {
        if (!_onScreen || Still || HalfCycles(BigTypeMotion.BobPeriodMilliseconds) is not { } count)
            return;
        HandheldComposition.Swing(tile, "Offset", default, default, new Vector3D(0, -BigTypeMotion.BobRise, 0),
            BigTypeMotion.BobPeriodMilliseconds / 2, count, EaseInOut, relativeToOffset: true);
    }

    /// <summary>Each frame: every tile's container takes its push (the neighbours make room on the landing curve).</summary>
    private bool StepPushes()
    {
        if (!HandheldTokens.IsBigType)
            return false;
        // A folder opening or closing, a launch or the shelf coming back poses every tile itself, the push folded in (M47 B8).
        if (GridMoving)
            return _bt.IsMoving(BtNow, hops: false);
        var now = BtNow;
        var trading = false;
        foreach (var (id, container, cell) in GridContainers())
        {
            _bt.Place(id, cell);
            // A tile still sliding to the slot it traded into (M47 B9) is drawn from where it came.
            var traded = TradeOffset(cell);
            trading |= traded != System.Numerics.Vector2.Zero;
            var push = _bt.Push(id, now) + traded;
            if (container.RenderTransform is TranslateTransform shift)
            {
                if (shift.X != push.X || shift.Y != push.Y)
                    (shift.X, shift.Y) = (push.X, push.Y);
            }
            else if (push != System.Numerics.Vector2.Zero)
            {
                container.RenderTransform = new TranslateTransform(push.X, push.Y);
            }
        }
        return _bt.IsMoving(now, hops: false) || trading;
    }

    // The home page's or the open folder's tiles on this Launcher: each tile's id (as the native Launcher names it), its container
    // and its place.
    private IEnumerable<(string Id, Control Container, LauncherCell Cell)> GridContainers()
    {
        if (_screen is { } screen)
        {
            foreach (var page in screen.Pages)
            {
                if (_pageTrack.ContainerFromIndex(page.Index) is not { } pageContainer ||
                    pageContainer.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault() is not { } slots)
                    continue;
                for (var i = 0; i < page.Slots.Count; i++)
                {
                    if (slots.ContainerFromIndex(i) is not { } container)
                        continue;
                    var slot = page.Slots[i];
                    yield return ($"h:{page.Index}:{slot.Index}", container,
                        new LauncherCell(LauncherGroup.Home, page.Index, slot.Index % page.Columns, slot.Index / page.Columns));
                }
            }
        }
        if (_folder is { } folder)
        {
            for (var i = 0; i < folder.VisibleTiles.Count; i++)
            {
                if (_folderTiles.ContainerFromIndex(i) is not { } container)
                    continue;
                var tile = folder.VisibleTiles[i];
                yield return ($"f:{tile.Index}", container,
                    new LauncherCell(LauncherGroup.Folder, 0, tile.Column, tile.Index % HandheldFolderNavigation.Rows));
            }
        }
    }

    // A grid tile's id and place from its anchor's view model; null for anything else (the dock, the header, a Title page's button).
    private (string Id, LauncherCell Cell)? Tile(Control anchor) => anchor.DataContext switch
    {
        HandheldHomeSlotViewModel slot when _screen?.Pages.FirstOrDefault(page => page.Slots.Contains(slot)) is { } page =>
            ($"h:{page.Index}:{slot.Index}", new LauncherCell(LauncherGroup.Home, page.Index, slot.Index % page.Columns, slot.Index / page.Columns)),
        HandheldGameTileViewModel tile when _folder?.VisibleTiles.Contains(tile) == true =>
            ($"f:{tile.Index}", new LauncherCell(LauncherGroup.Folder, 0, tile.Column, tile.Index % HandheldFolderNavigation.Rows)),
        _ => null,
    };

    // ---- The idle hop ----

    /// <summary>A press: the hop waits 4.8 s again, then comes every 6.5 s while the resting motion lives.</summary>
    private void ArmHop()
    {
        if (Still)
        {
            _hopTimer?.Stop();
            return;
        }
        if (_hopTimer is null)
        {
            _hopTimer = new DispatcherTimer(DispatcherPriority.Background);
            _hopTimer.Tick += (_, _) => OnHopTimer();
        }
        _hopTimer.Stop();
        _hopTimer.Interval = TimeSpan.FromMilliseconds(BigTypeMotion.HopAfterMilliseconds);
        _hopTimer.Start();
    }

    private void OnHopTimer()
    {
        _hopTimer?.Stop();
        var since = BtNow - _bt.LastInput;
        Hop();
        if (BigTypeMotion.UntilNextHop(since + 1, Still) is { } next)
        {
            _hopTimer!.Interval = TimeSpan.FromMilliseconds(next + 1);
            _hopTimer.Start();
        }
    }

    // A wave across the grid on screen: each tile rises 20 px and comes back, 75 ms later per step along the diagonal. Not while
    // something covers the grid (a sheet, the menu, a Title page) or a folder is zooming.
    private void Hop()
    {
        if (!_onScreen || Still || _shell is not { } shell || shell.IsLauncherOverlayOpen || shell.ShowsTitlePage || GridMoving || shell.IsPlaying ||
            _selectorHideStart is not null || _selectorHidePending)
            return;
        var folder = _folder;
        var firstColumn = folder?.FirstColumnFor(IsWide) ?? 0;
        var page = _screen?.CurrentPage?.Index ?? 0;
        const int samples = 14;
        foreach (var (_, container, cell) in GridContainers())
        {
            if (folder is null ? cell.Grid != LauncherGroup.Home || cell.Page != page : cell.Grid != LauncherGroup.Folder)
                continue;
            var column = cell.Grid == LauncherGroup.Folder ? cell.Column - firstColumn : cell.Column;
            var delay = BigTypeMotion.HopIndex(column, cell.Row) * BigTypeMotion.HopStagger;
            var frames = new (float Progress, Vector Displacement)[samples + 1];
            for (var i = 0; i <= samples; i++)
            {
                var progress = (float)i / samples;
                frames[i] = (progress, new Vector(0, BigTypeMotion.Hop(0, 0, progress * BigTypeMotion.HopMilliseconds)));
            }
            HandheldComposition.OffsetFrames(container, BigTypeMotion.HopMilliseconds, new LinearEasing(), delay, frames);
        }
    }
}
