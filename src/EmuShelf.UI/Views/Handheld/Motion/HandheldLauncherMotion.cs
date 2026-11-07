using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The bottom screen's motion (HANDHELD-MODE.md Motion spec): the travelling selector on a retargetable
/// spring with its breathing glow and the edge bump, the page and folder-column tracks, the folder's games
/// spilling out and gathering back, and the launch cover lifting off. Springs tick on the render clock
/// (<see cref="TopLevel.RequestAnimationFrame"/>) only while something moves and write transforms; one-shot
/// effects are compositor animations. The view model never knows about any of it.
/// </summary>
internal sealed class HandheldLauncherMotion
{
    private const double PageWidth = 1240;
    private const double ColumnPitch = 264;
    private const double DotPitch = 30;   // an 18 px dot plus 12 px spacing
    private const double IndicatorWidth = 52;
    private const double GlowBlur = 28;

    private readonly Control _view;
    private readonly Visual _canvas;
    private readonly Canvas _selectorLayer;
    private readonly Panel _selector;
    private readonly Border _ring;
    private readonly HandheldShadow _glow;
    private readonly Control _homeLayer;
    private readonly ItemsControl _pageTrack;
    private readonly Control _pageIndicator;
    private readonly Control _folderHeader;
    private readonly ItemsControl _folderTiles;
    private readonly Canvas _ghostLayer;
    private readonly Control _launchCover;
    private readonly Control _titleLayer;

    private readonly TranslateTransform _selectorShift = new();
    private readonly TranslateTransform _glowShift = new();
    private readonly TranslateTransform _pageShift = new();
    private readonly TranslateTransform _indicatorShift = new();
    private readonly TranslateTransform _folderShift = new();

    private readonly HandheldSpring _x = new(HandheldMotion.Selector);
    private readonly HandheldSpring _y = new(HandheldMotion.Selector);
    private readonly HandheldSpring _width = new(HandheldMotion.Selector);
    private readonly HandheldSpring _height = new(HandheldMotion.Selector);
    private readonly HandheldSpring _radius = new(HandheldMotion.Selector);
    private readonly HandheldSpring _page = new(HandheldMotion.Track);
    private readonly HandheldSpring _column = new(HandheldMotion.Track);

    private readonly List<Control> _anchors = [];
    private HandheldShellViewModel? _shell;
    private HandheldFolderViewModel? _folder;
    private HandheldFolderViewModel? _spillFolder;
    private Point _folderOrigin;
    private bool _selectorShown;
    private bool _pagePlaced;
    private Size _glowSize;
    private bool _ticking;
    private TimeSpan? _lastFrame;
    private TimeSpan _now;
    private TimeSpan? _bumpStart;
    private HandheldDirection _bumpDirection;

    public HandheldLauncherMotion(Control view)
    {
        _view = view;
        _canvas = Find<Control>("Canvas");
        _selectorLayer = Find<Canvas>("SelectorLayer");
        _selector = Find<Panel>("Selector");
        _ring = Find<Border>("SelectorRing");
        _glow = Find<HandheldShadow>("SelectorGlow");
        _homeLayer = Find<Control>("HomeLayer");
        _pageTrack = Find<ItemsControl>("PageTrack");
        _pageIndicator = Find<Control>("PageIndicator");
        _folderHeader = Find<Control>("FolderHeader");
        _folderTiles = Find<ItemsControl>("FolderTiles");
        _ghostLayer = Find<Canvas>("GhostLayer");
        _launchCover = Find<Control>("LaunchCover");
        _titleLayer = Find<Control>("TitleLayer");

        _selector.RenderTransform = _selectorShift;
        _glow.RenderTransform = _glowShift;
        _pageTrack.RenderTransform = _pageShift;
        _pageIndicator.RenderTransform = _indicatorShift;
        _folderTiles.RenderTransform = _folderShift;
        _selector.Opacity = 0;
        _glow.Opacity = 0;

        view.AttachedToVisualTree += (_, _) =>
        {
            // A fresh composition visual (first show, or a re-host) needs its breathing started again.
            HandheldComposition.Breathe(_glow, 0.55, HandheldMotion.BreathHalfCycleMilliseconds);
            _selectorShown = false;
            _lastFrame = null;
            RequestTick();
        };
        view.DetachedFromVisualTree += (_, _) => _ticking = false;
        view.LayoutUpdated += (_, _) => RequestTick();
        _folderTiles.LayoutUpdated += (_, _) => TrySpill();
    }

    private T Find<T>(string name) where T : Control =>
        _view.FindControl<T>(name) ?? throw new InvalidOperationException($"The Launcher has no {name}.");

    public void Attach(HandheldShellViewModel? shell)
    {
        if (ReferenceEquals(shell, _shell))
            return;
        if (_shell is { } old)
        {
            old.PropertyChanged -= OnShellChanged;
            old.Bumped -= OnBumped;
            old.FolderClosing -= OnFolderClosing;
            old.LaunchStarting -= OnLaunchStarting;
        }
        _shell = shell;
        WatchFolder(shell?.OpenFolder);
        _pagePlaced = false;
        // A view that arrives while a folder is already open starts with home put away.
        _homeLayer.Opacity = shell?.OpenFolder is null ? 1 : 0;
        if (shell is null)
            return;
        shell.PropertyChanged += OnShellChanged;
        shell.Bumped += OnBumped;
        shell.FolderClosing += OnFolderClosing;
        shell.LaunchStarting += OnLaunchStarting;
        RequestTick();
    }

    public void OnAnchorChanged(Control anchor, bool isFocused)
    {
        _anchors.Remove(anchor);
        if (isFocused)
            _anchors.Add(anchor);
        RequestTick();
    }

    // ---- The shell ----

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HandheldShellViewModel.CurrentPage):
                _page.Retarget(-(_shell?.CurrentPage?.Index ?? 0) * PageWidth);
                break;
            case nameof(HandheldShellViewModel.OpenFolder):
                var folder = _shell?.OpenFolder;
                if (folder is not null && !ReferenceEquals(folder, _folder))
                    OnFolderOpening(folder);
                WatchFolder(folder);
                break;
        }
        RequestTick();
    }

    private void OnBumped(HandheldDirection direction)
    {
        _bumpDirection = direction;
        _bumpStart = null; // stamped on the next frame
        _bumpPending = true;
        RequestTick();
    }

    private bool _bumpPending;

    // ---- Folder: the column track, the spill and the gather ----

    private void WatchFolder(HandheldFolderViewModel? folder)
    {
        if (ReferenceEquals(folder, _folder))
            return;
        if (_folder is { } old)
        {
            old.PropertyChanged -= OnFolderChanged;
            old.VisibleTiles.CollectionChanged -= OnTilesChanged;
        }
        _folder = folder;
        _column.Snap(0);
        if (folder is null)
            return;
        folder.PropertyChanged += OnFolderChanged;
        folder.VisibleTiles.CollectionChanged += OnTilesChanged;
        _lastFirstColumn = folder.FirstVisibleColumn;
        ApplyColumn();
    }

    private int _lastFirstColumn;

    private void OnFolderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_folder is not { } folder)
            return;
        if (e.PropertyName == nameof(HandheldFolderViewModel.FirstVisibleColumn))
        {
            // What is on screen stays where it is while the window moves under it, then glides on the track
            // spring. A jump of more than a column (a re-sort) just lands.
            var delta = folder.FirstVisibleColumn - _lastFirstColumn;
            _lastFirstColumn = folder.FirstVisibleColumn;
            if (Math.Abs(delta) == 1 && !folder.IsResorting)
                _column.Displace(delta * ColumnPitch);
            else
                _column.Snap(0);
            ApplyColumn();
            RequestTick();
        }
        else if (e.PropertyName == nameof(HandheldFolderViewModel.WindowFirstColumn))
        {
            ApplyColumn();
        }
    }

    private void ApplyColumn()
    {
        var behind = _folder is { } folder ? folder.FirstVisibleColumn - folder.WindowFirstColumn : 0;
        _folderShift.X = (-behind * ColumnPitch) + _column.Value;
    }

    private void OnFolderOpening(HandheldFolderViewModel folder)
    {
        // 1. The tile grows a little as its flap swings open. 2. Home recedes and fades while the games
        // spill out of where the folder sat (TrySpill, once they are laid out). 3. The header fades in.
        _spillFolder = folder;
        _folderOrigin = HomeSlotCentre(folder.HomeSlot) ?? new Point(PageWidth / 2, 400);
        if (HomeSlotContainer(folder.HomeSlot) is { } slot)
        {
            HandheldComposition.Scale(slot, HandheldMotion.FolderTileGrowMilliseconds / 0.35 * 1.0, new SineEaseOut(), 0,
                (0f, 1), (0.35f, HandheldMotion.FolderTileGrowScale), (1f, 1));
            SetSlotOpen(slot, true);
        }
        _homeLayer.Opacity = 0;
        HandheldComposition.Opacity(_homeLayer, 1, 0, HandheldMotion.HomeRecedeMilliseconds, new CubicEaseInOut());
        HandheldComposition.Scale(_homeLayer, 1, HandheldMotion.HomeRecedeScale, HandheldMotion.HomeRecedeMilliseconds, new CubicEaseInOut());
        HandheldComposition.Opacity(_folderHeader, 0, 1, HandheldMotion.HeaderInMilliseconds, new CubicEaseOut(), HandheldMotion.HeaderInDelayMilliseconds);
    }

    private void OnTilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => TrySpill();

    /// <summary>The first time an opening folder's tiles are laid out, they spill out of the folder, nearest first.</summary>
    private void TrySpill()
    {
        if (_spillFolder is not { } folder || !ReferenceEquals(folder, _folder) || folder.VisibleTiles.Count == 0)
            return;
        var tiles = new List<(Control Container, Point Centre)>();
        for (var i = 0; i < folder.VisibleTiles.Count; i++)
        {
            if (_folderTiles.ContainerFromIndex(i) is not { } container || container.Bounds.Width <= 0)
                return; // not laid out yet; the next layout pass tries again
            if (CentreOnCanvas(container) is { } centre)
                tiles.Add((container, centre));
        }
        _spillFolder = null;
        var rank = 0;
        foreach (var (container, centre) in tiles.OrderBy(tile => Distance(tile.Centre, _folderOrigin)))
        {
            var delay = Math.Min(rank++ * HandheldMotion.SpillStaggerMilliseconds, HandheldMotion.SpillStaggerCapMilliseconds);
            var from = _folderOrigin - centre;
            var easing = HandheldMotion.PopCurve();
            HandheldComposition.OffsetFrom(container, new Vector(from.X, from.Y), HandheldMotion.SpillMilliseconds, easing, delay);
            HandheldComposition.Scale(container, HandheldMotion.SpillFromScale, 1, HandheldMotion.SpillMilliseconds, easing, delay);
            HandheldComposition.Opacity(container, HandheldMotion.SpillMilliseconds, new LinearEasing(), delay, (0f, 0), (0.3f, 1), (1f, 1));
        }
    }

    private void OnFolderClosing(HandheldFolderViewModel folder)
    {
        // The tiles are about to go: picture each one where it is, then gather the pictures back into the
        // folder, farthest first, while home returns and the flap closes.
        _spillFolder = null;
        var origin = HomeSlotCentre(folder.HomeSlot) ?? _folderOrigin;
        var ghosts = new List<(Image Ghost, Point Centre)>();
        for (var i = 0; i < folder.VisibleTiles.Count; i++)
        {
            // The column parked behind the window is out of sight; picturing it would fly it in from off-canvas.
            if (folder.VisibleTiles[i].Column < folder.FirstVisibleColumn)
                continue;
            if (_folderTiles.ContainerFromIndex(i) is { } container && Ghost(container) is { } ghost)
                ghosts.Add(ghost);
        }
        var header = Ghost(_folderHeader);

        _homeLayer.Opacity = 1;
        HandheldComposition.Opacity(_homeLayer, 0, 1, HandheldMotion.HomeReturnMilliseconds, new CubicEaseOut(), HandheldMotion.HomeReturnDelayMilliseconds);
        HandheldComposition.Scale(_homeLayer, HandheldMotion.HomeRecedeScale, 1, HandheldMotion.HomeReturnMilliseconds, new CubicEaseOut(), HandheldMotion.HomeReturnDelayMilliseconds);
        if (HomeSlotContainer(folder.HomeSlot) is { } slot)
            SetSlotOpen(slot, false);

        // The pictures need a layout pass before their compositor offsets mean anything.
        Dispatcher.UIThread.Post(() =>
        {
            var rank = 0;
            var longest = 0d;
            foreach (var (ghost, centre) in ghosts.OrderByDescending(g => Distance(g.Centre, origin)))
            {
                var delay = Math.Min(rank++ * HandheldMotion.GatherStaggerMilliseconds, HandheldMotion.GatherStaggerCapMilliseconds);
                var to = origin - centre;
                var easing = new CubicEaseInOut();
                HandheldComposition.OffsetFrames(ghost, HandheldMotion.GatherMilliseconds, easing, delay, (0f, default), (1f, new Vector(to.X, to.Y)));
                HandheldComposition.Scale(ghost, 1, HandheldMotion.SpillFromScale, HandheldMotion.GatherMilliseconds, easing, delay);
                HandheldComposition.Opacity(ghost, HandheldMotion.GatherMilliseconds, new LinearEasing(), delay, (0f, 1), (0.7f, 1), (1f, 0));
                longest = Math.Max(longest, delay + HandheldMotion.GatherMilliseconds);
            }
            if (header is { } headerGhost)
                HandheldComposition.Opacity(headerGhost.Ghost, 1, 0, 180);
            DispatcherTimer.RunOnce(() =>
            {
                foreach (var (ghost, _) in ghosts)
                    RemoveGhost(ghost);
                if (header is { } h)
                    RemoveGhost(h.Ghost);
            }, TimeSpan.FromMilliseconds(Math.Max(longest, 180) + 60));
        }, DispatcherPriority.Loaded);
    }

    private (Image Ghost, Point Centre)? Ghost(Control control)
    {
        var size = control.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || control.TranslatePoint(default, _ghostLayer) is not { } topLeft)
            return null;
        RenderTargetBitmap bitmap;
        try
        {
            bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)));
            bitmap.Render(control);
        }
        catch (Exception)
        {
            return null; // no picture, no gather: the folder simply closes
        }
        var ghost = new Image { Source = bitmap, Width = size.Width, Height = size.Height, IsHitTestVisible = false };
        Canvas.SetLeft(ghost, topLeft.X);
        Canvas.SetTop(ghost, topLeft.Y);
        _ghostLayer.Children.Add(ghost);
        return (ghost, new Point(topLeft.X + (size.Width / 2), topLeft.Y + (size.Height / 2)));
    }

    private void RemoveGhost(Image ghost)
    {
        var bitmap = ghost.Source as IDisposable;
        ghost.Source = null;
        _ghostLayer.Children.Remove(ghost);
        bitmap?.Dispose();
    }

    private Control? HomeSlotContainer(int slot)
    {
        if (_shell?.CurrentPage is not { } page)
            return null;
        if (_pageTrack.ContainerFromIndex(page.Index) is not { } pageContainer)
            return null;
        var slots = pageContainer.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();
        return slots?.ContainerFromIndex(slot);
    }

    private static void SetSlotOpen(Control container, bool open)
    {
        foreach (var art in container.GetVisualDescendants().OfType<HandheldFolderArt>())
            art.Classes.Set("open", open);
    }

    private Point? HomeSlotCentre(int slot) => HomeSlotContainer(slot) is { } container ? CentreOnCanvas(container) : null;

    private Point? CentreOnCanvas(Visual visual) =>
        visual.TranslatePoint(new Point(visual.Bounds.Width / 2, visual.Bounds.Height / 2), _ghostLayer);

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    // ---- Launch: the cover presses in, pops, and lifts off toward the top screen ----

    private void OnLaunchStarting(HandheldTitlePageViewModel page)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var total = HandheldMotion.PressMilliseconds + HandheldMotion.LaunchLiftMilliseconds;
            var press = (float)(HandheldMotion.PressMilliseconds * 0.4 / total);
            var pop = (float)(HandheldMotion.PressMilliseconds / total);
            var easing = new SineEaseInOut();
            HandheldComposition.Scale(_launchCover, total, easing, 0,
                (0f, 1), (press, HandheldMotion.PressScale), (pop, HandheldMotion.PressPopScale), (1f, 0.7));
            HandheldComposition.OffsetFrames(_launchCover, total, new CubicEaseIn(), 0,
                (0f, default), (pop, default), (1f, new Vector(0, -1100)));
            HandheldComposition.Opacity(_launchCover, total, new LinearEasing(), 0, (0f, 1), (pop, 1), (1f, 0));
        }, DispatcherPriority.Loaded);
    }

    // ---- The frame clock ----

    private void RequestTick()
    {
        if (_ticking || TopLevel.GetTopLevel(_view) is not { } top)
            return;
        _ticking = true;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        _ticking = false;
        var dt = _lastFrame is { } last ? (now - last).TotalSeconds : 1d / 60d;
        _lastFrame = now;
        _now = now;
        var moving = false;

        if (_shell?.CurrentPage is { } page)
        {
            if (!_pagePlaced)
            {
                _page.Snap(-page.Index * PageWidth);
                _pagePlaced = true;
            }
            moving |= _page.Step(dt);
            _pageShift.X = _page.Value;
            _indicatorShift.X = (-_page.Value / PageWidth * DotPitch) - ((IndicatorWidth - 18) / 2);
        }
        moving |= _column.Step(dt);
        ApplyColumn();

        moving |= StepSelector(dt);
        if (moving)
            RequestTick();
        else
            _lastFrame = null;
    }

    private bool StepSelector(double dt)
    {
        if (ResolveTarget() is not { } target)
        {
            _selector.Opacity = 0;
            _glow.Opacity = 0;
            _selectorShown = false;
            return false;
        }
        var (rect, radius) = target;
        if (!_selectorShown)
        {
            // The first appearance lands in place; after that the selector always travels.
            _x.Snap(rect.X);
            _y.Snap(rect.Y);
            _width.Snap(rect.Width);
            _height.Snap(rect.Height);
            _radius.Snap(radius);
            _selectorShown = true;
        }
        else
        {
            _x.Retarget(rect.X);
            _y.Retarget(rect.Y);
            _width.Retarget(rect.Width);
            _height.Retarget(rect.Height);
            _radius.Retarget(radius);
        }
        var moving = _x.Step(dt) | _y.Step(dt) | _width.Step(dt) | _height.Step(dt) | _radius.Step(dt);

        // The edge bump: a nudge toward the wall and back, on top of wherever the spring has the selector.
        var bump = default(Vector);
        if (_bumpPending)
        {
            _bumpPending = false;
            _bumpStart = _now;
        }
        if (_bumpStart is { } start)
        {
            var progress = (_now - start).TotalMilliseconds / HandheldMotion.BumpMilliseconds;
            if (progress >= 1)
            {
                _bumpStart = null;
            }
            else
            {
                var amount = BumpCurve(progress) * HandheldMotion.BumpFraction;
                bump = _bumpDirection switch
                {
                    HandheldDirection.Left => new Vector(-amount * _width.Value, 0),
                    HandheldDirection.Right => new Vector(amount * _width.Value, 0),
                    HandheldDirection.Up => new Vector(0, -amount * _height.Value),
                    _ => new Vector(0, amount * _height.Value),
                };
                moving = true;
            }
        }

        _selector.Opacity = 1;
        _glow.Opacity = 1;
        var width = Math.Round(_width.Value, 1);
        var height = Math.Round(_height.Value, 1);
        if (Math.Abs(_selector.Width - width) > 0.05 || double.IsNaN(_selector.Width))
            _selector.Width = width;
        if (Math.Abs(_selector.Height - height) > 0.05 || double.IsNaN(_selector.Height))
            _selector.Height = height;
        var r = Math.Max(0, _radius.Value);
        if (Math.Abs(_ring.CornerRadius.TopLeft - r) > 0.25)
        {
            _ring.CornerRadius = new CornerRadius(r);
            ((Border)_ring.Child!).CornerRadius = new CornerRadius(Math.Max(0, r - 4));
            ((Border)((Border)_ring.Child!).Child!).CornerRadius = new CornerRadius(Math.Max(0, r - 18));
        }
        _selectorShift.X = _x.Value + bump.X;
        _selectorShift.Y = _y.Value + bump.Y;

        // The glow is a pre-rendered bitmap sized to where the selector is going, never resized per frame.
        var glowSize = new Size(Math.Round(rect.Width), Math.Round(rect.Height));
        if (glowSize != _glowSize)
        {
            _glowSize = glowSize;
            _glow.Width = glowSize.Width + (2 * GlowBlur);
            _glow.Height = glowSize.Height + (2 * GlowBlur);
            _glow.Radius = radius;
        }
        _glowShift.X = _selectorShift.X + (_width.Value / 2) - (_glow.Width / 2);
        _glowShift.Y = _selectorShift.Y + (_height.Value / 2) - (_glow.Height / 2);
        return moving;
    }

    // Out to the wall fast (the first 35%), then eased back.
    private static double BumpCurve(double progress) => progress < 0.35
        ? Math.Sin(progress / 0.35 * Math.PI / 2)
        : Math.Pow(Math.Cos((progress - 0.35) / 0.65 * Math.PI / 2), 2);

    /// <summary>
    /// Where the selector should be: around the most recently focused anchor that is on screen, at its ring's
    /// rectangle as it will be once the anchor's lift has finished (so the lift's own animation never drags it).
    /// </summary>
    private (Rect Rect, double Radius)? ResolveTarget()
    {
        for (var i = _anchors.Count - 1; i >= 0; i--)
        {
            var anchor = _anchors[i];
            if (!anchor.IsAttachedToVisualTree())
            {
                _anchors.RemoveAt(i);
                continue;
            }
            if (!anchor.IsEffectivelyVisible || !HandheldSelectorAnchor.GetIsFocused(anchor) || OpacityHides(anchor))
                continue;
            // The Title page covers the grid: while it is up, only its own buttons and cards take the frame.
            if (_titleLayer.IsVisible && !_titleLayer.IsVisualAncestorOf(anchor))
                continue;
            if (anchor.GetVisualParent() is not Visual parent || ToLayer(parent) is not { } matrix)
                continue;
            var ring = anchor.GetVisualChildren().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("ring"));
            var local = ring?.Bounds ?? new Rect(anchor.Bounds.Size).Inflate(16);
            var lift = anchor.Classes.Contains("focusable") ? HandheldMotion.LiftScale : 1;
            var centre = new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2);
            var lifted = new Rect(
                centre.X + ((local.X - centre.X) * lift),
                centre.Y + ((local.Y - centre.Y) * lift),
                local.Width * lift,
                local.Height * lift).Translate(anchor.Bounds.Position);
            var topLeft = lifted.TopLeft.Transform(matrix);
            var bottomRight = lifted.BottomRight.Transform(matrix);
            var radius = (ring?.CornerRadius.TopLeft ?? 32) * lift * matrix.M11;
            return (new Rect(topLeft, bottomRight), radius);
        }
        return null;
    }

    /// <summary>
    /// Maps a visual's coordinates into the selector layer through the window, because the anchors and the
    /// layer sit in different Viewboxes and a direct TransformToVisual between them drops the layer's scale.
    /// </summary>
    private Matrix? ToLayer(Visual visual)
    {
        if (TopLevel.GetTopLevel(_view) is not { } root ||
            visual.TransformToVisual(root) is not { } toRoot ||
            _selectorLayer.TransformToVisual(root) is not { } layerToRoot ||
            !layerToRoot.TryInvert(out var rootToLayer))
        {
            return null;
        }
        return toRoot * rootToLayer;
    }

    // A layer faded out by the motion (home behind an open folder) hides its anchors too.
    private bool OpacityHides(Visual anchor)
    {
        foreach (var ancestor in anchor.GetVisualAncestors())
        {
            if (ReferenceEquals(ancestor, _canvas))
                return false;
            if (ancestor is Visual visual && visual.Opacity == 0)
                return true;
        }
        return false;
    }
}
