using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The Big Type grid's big moves on the Avalonia Launcher (M47 B8), from the same maths as the native Launcher's (BigTypeFolderMotion,
// BigTypeLaunchMotion): a folder opening (home's tiles fall away from it, the games deal in, the name's letters land, the ring pops in
// on the first game), a folder closing (the games drop out, home's tiles pop back in around it), a launch (the tile flies up through the
// hinge, the grid goes as the takeover starts) and the shelf coming back after a game (the tiles pop in around the focused one). Each
// frame sets every tile's container's transform and opacity; the ring waits until the move says it may show. Held still, each is simply
// there.
internal sealed partial class HandheldLauncherMotion
{
    private enum GridMoveKind
    {
        Open,
        Close,
        Launch,
        Return,
    }

    private sealed class GridMove(GridMoveKind kind, float start, int centreColumn, int centreRow)
    {
        public GridMoveKind Kind { get; } = kind;
        public float Start { get; } = start;
        public int CentreColumn { get; } = centreColumn;
        public int CentreRow { get; } = centreRow;

        /// <summary>The tile the press was on (it falls away growing, or flies), by its id.</summary>
        public string? Pressed { get; init; }

        /// <summary>The closing folder's tiles and header, pictured where they were (they are gone from the tree).</summary>
        public List<(Image Ghost, int Column, int Row)> Ghosts { get; } = [];

        public Image? HeaderGhost { get; set; }
        public bool HeadingArrived { get; set; }
        public bool RingShown { get; set; }
    }

    private GridMove? _gridMove;

    /// <summary>A move that holds the grid: the neighbours' push is folded into its poses, and the ring waits.</summary>
    private bool GridMoving => _gridMove is not null;

    private (int Column, int Row) HomeCellOf(int homeIndex)
    {
        if (_screen is not { } screen)
            return (0, 0);
        var at = screen.Grid.PositionOf(homeIndex);
        var columns = screen.Grid.Columns;
        return (at.Slot % columns, at.Slot / columns);
    }

    private void BeginGridMove(GridMove move)
    {
        EndGridMove();
        _gridMove = move;
        RequestTick();
    }

    // ---- Opening and closing a folder ----

    private void OpenFolderBigType(HandheldFolderViewModel folder)
    {
        var (column, row) = HomeCellOf(folder.HomeIndex);
        var at = _screen?.Grid.PositionOf(folder.HomeIndex);
        BeginGridMove(new GridMove(GridMoveKind.Open, BtNow, column, row) { Pressed = at is { } p ? $"h:{p.Page}:{p.Slot}" : null });
        _homeLayer.Opacity = 1;
        _folderPage.Opacity = Still ? 1 : 0;
    }

    private void CloseFolderBigType(HandheldFolderViewModel folder)
    {
        var (column, row) = HomeCellOf(folder.HomeIndex);
        var move = new GridMove(GridMoveKind.Close, BtNow, column, row);
        if (!Still)
        {
            // The folder's page is about to go: its games and its header are pictured where they are, and those pictures drop.
            var firstColumn = folder.FirstColumnFor(IsWide);
            for (var i = 0; i < folder.VisibleTiles.Count; i++)
            {
                if (_folderTiles.ContainerFromIndex(i) is not { } container || Ghost(container) is not { } picture)
                    continue;
                var tile = folder.VisibleTiles[i];
                move.Ghosts.Add((picture.Ghost, tile.Column - firstColumn, tile.Index % HandheldFolderNavigation.Rows));
            }
            if (_view.FindControl<Control>("BtFolderHeader") is { } header && Ghost(header) is { } headerPicture)
                move.HeaderGhost = headerPicture.Ghost;
        }
        BeginGridMove(move);
        _homeLayer.Opacity = Still ? 1 : 0;
    }

    // ---- The launch and the return ----

    private void LaunchBigType()
    {
        if (_shell is not { } shell)
            return;
        var anchor = FocusedAnchor();
        var tile = anchor is null ? null : Tile(anchor);
        var (column, row) = tile is { } t ? (t.Cell.Column, t.Cell.Row) : (0, 0);
        BeginGridMove(new GridMove(GridMoveKind.Launch, BtNow, column, row) { Pressed = tile?.Id });
    }

    private void ReturnBigType()
    {
        var anchor = FocusedAnchor();
        var tile = anchor is null ? null : Tile(anchor);
        var firstColumn = _folder?.FirstColumnFor(IsWide) ?? 0;
        var (column, row) = tile is { } t ? (t.Cell.Grid == LauncherGroup.Folder ? t.Cell.Column - firstColumn : t.Cell.Column, t.Cell.Row) : (0, 0);
        BeginGridMove(new GridMove(GridMoveKind.Return, BtNow, column, row));
    }

    // ---- Each frame ----

    /// <summary>Poses every tile for the move under way; false once it has ended (everything at rest again).</summary>
    private bool StepGridMove()
    {
        if (_gridMove is not { } move)
            return false;
        var since = BtNow - move.Start;
        var still = Still;
        var folder = _folder;
        var page = _screen?.CurrentPage?.Index ?? 0;
        var firstColumn = folder?.FirstColumnFor(IsWide) ?? 0;
        var now = BtNow;
        bool done;
        switch (move.Kind)
        {
            case GridMoveKind.Open:
            {
                var gone = BigTypeFolderMotion.HomeGone(since, still);
                _homeLayer.Opacity = gone ? 0 : 1;
                _folderPage.Opacity = gone ? 1 : 0;
                foreach (var (id, container, cell) in GridContainers())
                {
                    if (cell.Grid == LauncherGroup.Home)
                    {
                        if (gone || cell.Page != page)
                        {
                            Pose(container, 0, 0, 1, 1);
                            continue;
                        }
                        var opened = id == move.Pressed;
                        var leave = BigTypeFolderMotion.FallAway(cell.Column - move.CentreColumn, cell.Row - move.CentreRow, opened, since, still);
                        var push = _bt.Push(id, now);
                        // The opened tile is the focused one, lifted inside its container: it grows to the mock's 1.7 as drawn.
                        var to = opened ? BigTypeFolderMotion.OutOpenedScale / (float)HandheldTokens.FocusScale : leave.ToScale;
                        var (x, y, _) = leave.From(push.X, push.Y, 1f);
                        Pose(container, x, y, 1f + ((to - 1f) * leave.Progress), leave.Opacity);
                    }
                    else
                    {
                        var pose = gone ? BigTypeFolderMotion.Deal(cell.Column - firstColumn, cell.Row, since - BigTypeFolderMotion.OutHoldMilliseconds, still)
                            : BigTypeTilePose.Gone;
                        Pose(container, pose.X, pose.Y, pose.Scale, pose.Opacity);
                    }
                }
                if (gone && !move.HeadingArrived)
                {
                    move.HeadingArrived = true;
                    _view.FindControl<HandheldBigTypeHeading>("BtFolderName")?.Arrive();
                }
                done = since >= BigTypeFolderMotion.OpenTotal(still);
                break;
            }
            case GridMoveKind.Close:
            {
                var gone = BigTypeFolderMotion.FolderGone(since, still);
                _homeLayer.Opacity = gone ? 1 : 0;
                foreach (var (ghost, column, row) in move.Ghosts)
                {
                    var leave = BigTypeFolderMotion.Drop(column, row, since, still);
                    var (x, y, scale) = leave.From(0, 0, 1);
                    Pose(ghost, x, y, scale, gone ? 0 : leave.Opacity);
                }
                if (move.HeaderGhost is { } header)
                    header.Opacity = gone ? 0 : 1;
                foreach (var (id, container, cell) in GridContainers())
                {
                    if (cell.Grid != LauncherGroup.Home || cell.Page != page)
                        continue;
                    var pose = gone ? BigTypeFolderMotion.PopIn(cell.Column - move.CentreColumn, cell.Row - move.CentreRow,
                        since - BigTypeFolderMotion.CloseHoldMilliseconds, still) : BigTypeTilePose.Gone;
                    var push = _bt.Push(id, now);
                    Pose(container, push.X, push.Y, pose.Scale, pose.Opacity);
                }
                done = since >= BigTypeFolderMotion.CloseTotal(still);
                break;
            }
            case GridMoveKind.Launch:
            {
                var opacity = BigTypeLaunchMotion.GridOpacity(since, still);
                // The whole layer goes, a folder's header with its games (the mock's lay.classList.add('gone')).
                if (_view.FindControl<Control>("BtFolderHeader") is { } launchHeader)
                    launchHeader.Opacity = Math.Round(opacity, 3);                foreach (var (id, container, cell) in GridContainers())
                {
                    if (folder is null ? cell.Grid != LauncherGroup.Home || cell.Page != page : cell.Grid != LauncherGroup.Folder)
                        continue;
                    var push = _bt.Push(id, now);
                    if (id == move.Pressed)
                    {
                        var top = container.TranslatePoint(default, _ghostLayer)?.Y ?? 0;
                        var (rise, scale) = BigTypeLaunchMotion.Flight(since, (float)top, still);
                        Pose(container, push.X, push.Y + rise, scale, BigTypeLaunchMotion.Flown(since, still) ? 0 : opacity);
                    }
                    else
                    {
                        Pose(container, push.X, push.Y, 1, opacity);
                    }
                }
                // It holds (the grid stays gone) until the shelf comes back.
                done = false;
                break;
            }
            default:
            {
                foreach (var (id, container, cell) in GridContainers())
                {
                    if (folder is null ? cell.Grid != LauncherGroup.Home || cell.Page != page : cell.Grid != LauncherGroup.Folder)
                        continue;
                    var column = cell.Grid == LauncherGroup.Folder ? cell.Column - firstColumn : cell.Column;
                    var pose = BigTypeFolderMotion.PopIn(column - move.CentreColumn, cell.Row - move.CentreRow, since, still);
                    var push = _bt.Push(id, now);
                    Pose(container, push.X, push.Y, pose.Scale, pose.Opacity);
                }
                done = since >= (still ? 0 : BigTypeFolderMotion.PopInTotal);
                break;
            }
        }
        if (done)
        {
            EndGridMove();
            return false;
        }
        return true;
    }

    /// <summary>Whether the ring may show yet during a move, and how it arrives when it may (popping in on a folder's first game).</summary>
    private bool RingWaits(float now)
    {
        if (_gridMove is not { } move)
            return false;
        var since = now - move.Start;
        var still = Still;
        switch (move.Kind)
        {
            case GridMoveKind.Open when since < BigTypeFolderMotion.OpenRingAt(still):
            case GridMoveKind.Close when since < BigTypeFolderMotion.CloseRingAt(still):
            case GridMoveKind.Return when since < (still ? 0 : BigTypeLaunchMotion.ReturnRingAfter):
            case GridMoveKind.Launch:
                return true;
        }
        if (!move.RingShown)
        {
            move.RingShown = true;
            if (move.Kind == GridMoveKind.Open)
                _bt.PopIn(now, BigTypeFolderMotion.RingPopFromScale);
            else
                _bt.MoveRing(now, instant: true, pop: false);
        }
        return false;
    }

    /// <summary>A move ends (or another takes its place): every tile and the page go back to rest, the pictures go.</summary>
    private void EndGridMove()
    {
        if (_gridMove is not { } move)
            return;
        _gridMove = null;
        foreach (var (ghost, _, _) in move.Ghosts)
            RemoveGhost(ghost);
        if (move.HeaderGhost is { } header)
            RemoveGhost(header);
        foreach (var (_, container, _) in GridContainers())
        {
            container.RenderTransform = null;
            container.Opacity = 1;
        }
        _homeLayer.Opacity = _shell?.OpenFolder is null ? 1 : 0;
        _folderPage.Opacity = 1;
        // Only the launch fades the header (a list over the folder hides it its own way, M47 B7).
        if (move.Kind == GridMoveKind.Launch && _view.FindControl<Control>("BtFolderHeader") is { } folderHeader)
            folderHeader.Opacity = 1;
        RequestTick();
    }

    /// <summary>A container's pose: moved by (x, y), scaled about its middle, at an opacity (rounded, so a pose at rest is exactly rest).</summary>
    private static void Pose(Control control, double x, double y, double scale, double opacity)
    {
        x = Math.Round(x, 2);
        y = Math.Round(y, 2);
        scale = Math.Round(scale, 4);
        if (x == 0 && y == 0 && scale == 1)
            control.RenderTransform = null;
        else
            control.RenderTransform = new MatrixTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y));
        control.Opacity = Math.Round(Math.Clamp(opacity, 0, 1), 3);
    }
}
