using System;
using System.Collections.Generic;
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
/// spring with its breathing glow and the edge bump, the page and folder-column tracks, the folder's Zoom
/// through (the tile grows toward the viewer and fades while the folder page grows out of its place, and
/// back), and the launch cover lifting off. Springs tick on the render clock
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
    private readonly Control _folderPage;
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
    private Point _folderOrigin;
    private int _zoom;
    private bool _selectorHidePending;
    private TimeSpan? _selectorHideStart;
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
        _folderPage = Find<Control>("FolderPage");
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
            // A fresh composition visual (first show, or a re-host) starts breathing again.
            Breathe();
            _selectorShown = false;
            _lastFrame = null;
            RequestTick();
        };
        view.DetachedFromVisualTree += (_, _) => _ticking = false;
        view.LayoutUpdated += (_, _) => RequestTick();
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
        {
            _anchors.Add(anchor);
            Breathe();
        }
        RequestTick();
    }

    // The glow breathes for a few seconds after the focus moves, then rests at full opacity, so an idle
    // Launcher draws nothing.
    private void Breathe() =>
        HandheldComposition.Breathe(_glow, 0.55, HandheldMotion.BreathHalfCycleMilliseconds, HandheldMotion.BreathHalfCycles);

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

    // ---- Folder: the column track and the Zoom through ----

    private void WatchFolder(HandheldFolderViewModel? folder)
    {
        if (ReferenceEquals(folder, _folder))
            return;
        if (_folder is { } old)
            old.PropertyChanged -= OnFolderChanged;
        _folder = folder;
        _column.Snap(0);
        if (folder is null)
            return;
        folder.PropertyChanged += OnFolderChanged;
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
        // Zoom through: two objects, one direction. The folder tile grows toward the viewer and fades (its
        // flap swings open as it goes); home fades out under it; the whole folder page, header and grid as one
        // surface, grows out of the tile's place on a spring. No tile moves on its own. The selector stays
        // hidden until the page has landed.
        var zoom = ++_zoom;
        _folderOrigin = HomeSlotCentre(folder.HomeSlot) ?? new Point(PageWidth / 2, 400);
        HideSelector();
        var ease = HandheldMotion.ZoomEase();
        if (HomeSlotContainer(folder.HomeSlot) is { } slot)
        {
            SetSlotOpen(slot, true);
            HandheldComposition.Scale(slot, 1, HandheldMotion.ZoomTileScale, HandheldMotion.ZoomTileOutMilliseconds, ease);
            HandheldComposition.Opacity(slot, 1, 0, HandheldMotion.ZoomTileOutMilliseconds, ease);
            // Once home is out of sight the tile goes back to rest, so its Avalonia-side state (1×, opaque)
            // and its composition visual agree again whatever closes the folder.
            DispatcherTimer.RunOnce(() =>
            {
                if (zoom == _zoom)
                    RestSlot(slot);
            }, TimeSpan.FromMilliseconds(HandheldMotion.ZoomTileOutMilliseconds + 40));
        }
        _homeLayer.Opacity = 0;
        HandheldComposition.Opacity(_homeLayer, 1, 0, HandheldMotion.ZoomHomeOutMilliseconds, ease, HandheldMotion.ZoomHomeOutDelayMilliseconds);

        var centre = ToFolderPage(_folderOrigin);
        HandheldComposition.ScaleAbout(_folderPage, centre, HandheldMotion.ZoomSheetInMilliseconds, SheetEasing,
            HandheldMotion.ZoomSheetInDelayMilliseconds, (0f, HandheldMotion.ZoomSheetFromScale), (1f, 1));
        HandheldComposition.Opacity(_folderPage, HandheldMotion.ZoomSheetInMilliseconds, new LinearEasing(),
            HandheldMotion.ZoomSheetInDelayMilliseconds, (0f, 0), (HandheldMotion.ZoomSheetFadeInPortion, 1), (1f, 1));
    }

    private static readonly HandheldSpringEasing SheetEasing = new(HandheldMotion.ZoomSheet);
    private static readonly HandheldSpringEasing TileReturnEasing = new(HandheldMotion.ZoomTile);

    private void OnFolderClosing(HandheldFolderViewModel folder)
    {
        // The reverse. The page is about to go, so picture it whole and shrink the picture back into the
        // folder's place while home fades in and the tile comes back from 2.6× with its flap closing.
        ++_zoom;
        HideSelector();
        var origin = HomeSlotCentre(folder.HomeSlot) ?? _folderOrigin;
        var page = Ghost(_folderPage);
        var ease = HandheldMotion.ZoomEase();

        _homeLayer.Opacity = 1;
        HandheldComposition.Opacity(_homeLayer, 0, 1, HandheldMotion.ZoomHomeInMilliseconds, ease, HandheldMotion.ZoomHomeInDelayMilliseconds);
        if (HomeSlotContainer(folder.HomeSlot) is { } slot)
        {
            SetSlotOpen(slot, false);
            HandheldComposition.Scale(slot, HandheldMotion.ZoomTileInMilliseconds, TileReturnEasing, HandheldMotion.ZoomTileInDelayMilliseconds,
                (0f, HandheldMotion.ZoomTileScale), (1f, 1));
            HandheldComposition.Opacity(slot, 0, 1, HandheldMotion.ZoomTileInMilliseconds, new CubicEaseOut(), HandheldMotion.ZoomTileInDelayMilliseconds);
        }
        if (page is not { } picture)
            return; // no picture: the page simply goes and home fades in

        // The picture needs a layout pass before its composition visual is placed.
        Dispatcher.UIThread.Post(() =>
        {
            var topLeft = new Point(Canvas.GetLeft(picture.Ghost), Canvas.GetTop(picture.Ghost));
            HandheldComposition.ScaleAbout(picture.Ghost, origin - topLeft, HandheldMotion.ZoomSheetOutMilliseconds, ease, 0,
                (0f, 1), (1f, HandheldMotion.ZoomSheetFromScale));
            // As in the lab the owner picked from: the page is gone by 70% of its shrink, so home shows through.
            HandheldComposition.Opacity(picture.Ghost, HandheldMotion.ZoomSheetOutMilliseconds, ease, 0,
                (0f, 1), (HandheldMotion.ZoomSheetFadeOutFrom, 0), (1f, 0));
            DispatcherTimer.RunOnce(() => RemoveGhost(picture.Ghost),
                TimeSpan.FromMilliseconds(HandheldMotion.ZoomSheetOutMilliseconds + 60));
        }, DispatcherPriority.Loaded);
    }

    private static void RestSlot(Control slot)
    {
        HandheldComposition.Scale(slot, 1, 1, 1);
        HandheldComposition.Opacity(slot, 1, 1, 1);
    }

    /// <summary>A point on the Launcher canvas in the folder page's own coordinates (it sits at its margin).</summary>
    private Point ToFolderPage(Point onCanvas) =>
        new(onCanvas.X - _folderPage.Margin.Left, onCanvas.Y - _folderPage.Margin.Top);

    /// <summary>Hides the selector for a folder's zoom; it fades back in once the page has landed.</summary>
    private void HideSelector()
    {
        _selectorLayer.Opacity = 0;
        _selectorHidePending = true;
        _selectorHideStart = null;
        RequestTick();
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
        moving |= StepSelectorFade();
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

    /// <summary>The selector layer's opacity across a folder's zoom: hidden, then a short fade back in.</summary>
    private bool StepSelectorFade()
    {
        if (_selectorHidePending)
        {
            _selectorHidePending = false;
            _selectorHideStart = _now;
        }
        if (_selectorHideStart is not { } start)
            return false;
        var elapsed = (_now - start).TotalMilliseconds - HandheldMotion.ZoomSelectorHiddenMilliseconds;
        if (elapsed >= HandheldMotion.ZoomSelectorFadeMilliseconds)
        {
            _selectorHideStart = null;
            _selectorLayer.Opacity = 1;
            return false;
        }
        _selectorLayer.Opacity = elapsed <= 0 ? 0 : Math.Round(elapsed / HandheldMotion.ZoomSelectorFadeMilliseconds, 2);
        return true;
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
