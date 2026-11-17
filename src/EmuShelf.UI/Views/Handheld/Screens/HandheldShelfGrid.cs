using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// All apps as the Shelf (M47 B15, proposals-screens.html option 2, the owner's pick of 16 November 2026): every app's icon on the colour
/// in a grid like home, five across (eight on the 16:9 screen) and four rows at a time, the focused one grown with its neighbours
/// stepping away and bobbing, under the one white ring. Opening, the icons grow in from the focused one outward; closing, they move
/// away from it and fade. Rows scroll by whole rows to keep the focus in sight. Drawn as one control: only the rows in sight are drawn,
/// so a device with hundreds of apps costs what four rows cost. Touch is today's: a tap opens an app, a held finger pins it, a drag
/// scrolls. Every number is tokens.js's (<c>screens.Shelf…</c>, <c>motion.Shelf…</c>), the timing <see cref="BigTypeScreensMotion"/>'s.
/// </summary>
public sealed class HandheldShelfGrid : Panel
{
    private HandheldCompanionViewModel? _companion;
    private readonly HandheldScreenRing _ring;
    private readonly Border _focusTile;
    private readonly Image _focusImage = new() { Stretch = Stretch.Uniform };
    private int _bobbing = -1;
    private readonly BigTypeGlide _firstRow = new(0f, BigTypeScreensMotion.ShelfScrollMilliseconds, BigTypeScreensMotion.Soft);
    private readonly Dictionary<int, (float From, float To, float Start)> _scale = [];
    private readonly Dictionary<int, (Vector From, Vector To, float Start)> _push = [];
    private float _openStart = float.NegativeInfinity;
    private float _closeStart = float.NaN;
    private float _focusStart;
    private int _focus = -1;
    private int _closeFocus;
    private bool _ticking;
    private bool _ringShown;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public HandheldShelfGrid()
    {
        ClipToBounds = true;
        Focusable = false;
        Background = Brushes.Transparent;
        _surface = new Surface(Draw) { IsHitTestVisible = false };
        Children.Add(_surface);
        // The focused icon is a child of its own, so its bob runs on the compositor (a frame drawn from the controls never catches it
        // mid-bob), and the ring is drawn over it.
        _focusTile = new Border
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Width = HandheldTokens.ShelfIcon,
            Height = HandheldTokens.ShelfIcon,
            CornerRadius = HandheldTokens.ShelfIconCorner,
            ClipToBounds = true,
            IsHitTestVisible = false,
            IsVisible = false,
            Child = _focusImage,
        };
        Children.Add(_focusTile);
        _ring = new HandheldScreenRing(this);
        SetValue(InputElement.IsHoldingEnabledProperty, true);
        AddHandler(InputElement.HoldingEvent, OnHolding);
    }

    private float Now => (float)_clock.Elapsed.TotalMilliseconds;

    private bool Still => _companion?.HoldsStill == true || !HandheldRollingText.GetRolls(this);

    private int Columns => _companion?.IsOnWideScreen == true ? HandheldTokens.ShelfWideColumns : HandheldTokens.ShelfColumns;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_companion is { } old)
        {
            old.PropertyChanged -= OnCompanionChanged;
            old.DrawerApps.CollectionChanged -= OnAppsChanged;
        }
        _companion = DataContext as HandheldCompanionViewModel;
        if (_companion is { } companion)
        {
            companion.PropertyChanged += OnCompanionChanged;
            companion.DrawerApps.CollectionChanged += OnAppsChanged;
        }
        Request();
    }

    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Request();

    private void OnCompanionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HandheldCompanionViewModel.DrawerIndex) && _companion is { IsDrawerLayer: true } companion && float.IsNaN(_closeStart))
            Follow(companion.DrawerIndex, opening: false);
    }

    /// <summary>All apps opened: the icons grow in around the focused one and the ring pops in on it.</summary>
    public void Open()
    {
        var now = Now;
        _closeStart = float.NaN;
        _scale.Clear();
        _push.Clear();
        _focus = -1;
        _ring.Motion.HoldStill = Still;
        _openStart = Still ? float.NegativeInfinity : now;
        Follow(_companion?.DrawerIndex ?? 0, opening: true);
    }

    /// <summary>All apps closed: the icons move away from the focused one and fade; the ring goes at once.</summary>
    public void Close()
    {
        _closeStart = Still ? float.NegativeInfinity : Now;
        _closeFocus = Math.Max(0, _focus);
        _ringShown = false;
        _ring.Hide();
        Request();
    }

    /// <summary>How long the closing takes.</summary>
    public static float CloseMilliseconds(bool still) => BigTypeScreensMotion.ShelfOutTotal(still);

    private void Follow(int index, bool opening)
    {
        var now = Now;
        var still = Still;
        _ring.Motion.HoldStill = still;
        _ring.Motion.Input(now);
        var columns = Columns;
        var row = index / columns;
        var first = (int)MathF.Round(_firstRow.Target);
        if (opening)
            first = Math.Clamp(first, 0, Math.Max(0, row));
        if (row < first)
            first = row;
        if (row > first + HandheldTokens.ShelfRows - 1)
            first = row - (HandheldTokens.ShelfRows - 1);
        if (opening)
            _firstRow.Jump(first);
        else
            _firstRow.To(first, now, still);
        var old = _focus;
        _focus = index;
        _focusStart = now;
        // The focused icon grows and its eight neighbours step away (the page's --s and --px/--py), each from where it is drawn.
        void Scale(int i, float to)
        {
            var from = ScaleAt(i, now);
            _scale[i] = (from, to, still ? float.NegativeInfinity : now);
        }
        void Push(int i, Vector to)
        {
            var from = PushAt(i, now);
            _push[i] = (from, to, still ? float.NegativeInfinity : now);
        }
        var affected = new HashSet<int>();
        foreach (var centre in new[] { old, index })
        {
            if (centre < 0)
                continue;
            for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
            {
                var c = (centre % columns) + dc;
                var r = (centre / columns) + dr;
                if (c >= 0 && c < columns && r >= 0)
                    affected.Add((r * columns) + c);
            }
        }
        foreach (var i in affected)
        {
            Scale(i, i == index ? (float)HandheldTokens.ShelfFocusScale : 1f);
            var dc = (i % columns) - (index % columns);
            var dr = (i / columns) - row;
            var near = i != index && Math.Abs(dc) <= 1 && Math.Abs(dr) <= 1;
            Push(i, near ? new Vector(dc * HandheldTokens.ShelfPush, dr * HandheldTokens.ShelfPush) : default);
        }
        _ring.Motion.MoveRing(now, instant: opening || !_ringShown || still, pop: false);
        if (opening && !still)
            _ring.Motion.PopIn(now, BigTypeMotion.PopFromScale);
        _ringShown = true;
        Request();
    }

    private float ScaleAt(int i, float now) => _scale.TryGetValue(i, out var s)
        ? BigTypeScreensMotion.Focus(s.From, s.To, now - s.Start, Still) : 1f;

    private Vector PushAt(int i, float now)
    {
        if (!_push.TryGetValue(i, out var p))
            return default;
        var t = BigTypeScreensMotion.Focus(0f, 1f, now - p.Start, Still);
        return p.From + ((p.To - p.From) * t);
    }

    // The icon's box at rest on this control (the clip's top is the control's top).
    private Rect Cell(int index, float firstRow)
    {
        var columns = Columns;
        var x = HandheldTokens.ShelfLeft + ((index % columns) * HandheldTokens.ShelfPitchX);
        var y = HandheldTokens.ShelfFirstTop + (((index / columns) - firstRow) * HandheldTokens.ShelfPitchY);
        return new Rect(x, y, HandheldTokens.ShelfIcon, HandheldTokens.ShelfIcon);
    }

    // What the grid draws itself, under its children (the focused icon, the ring); Panel's own Render is sealed.
    private sealed class Surface(Action<DrawingContext> draw) : Control
    {
        public override void Render(DrawingContext context) => draw(context);
    }

    private readonly Surface _surface;

    private void Draw(DrawingContext context)
    {
        if (_companion is not { } companion)
            return;
        var apps = companion.DrawerApps;
        var now = Now;
        var still = Still;
        var first = _firstRow.At(now);
        var moving = !_firstRow.Settled(now);
        var columns = Columns;
        var closing = !float.IsNaN(_closeStart);
        var top = Math.Max(0, (int)MathF.Floor(first) - 1);
        var bottom = (int)MathF.Ceiling(first) + HandheldTokens.ShelfRows + 1;
        var focusColumn = (closing ? _closeFocus : Math.Max(0, _focus)) % columns;
        var focusRow = (closing ? _closeFocus : Math.Max(0, _focus)) / columns;
        var corner = HandheldTokens.ShelfIconCorner.TopLeft;
        (HandheldDrawerAppViewModel App, Rect Box, double Opacity)? focus = null;
        BigTypeRing? ringTarget = null;
        for (var i = top * columns; i < Math.Min(apps.Count, bottom * columns); i++)
        {
            var cell = Cell(i, first);
            var dc = (i % columns) - focusColumn;
            var dr = (i / columns) - focusRow;
            double scale = ScaleAt(i, now), opacity = 1;
            var shift = PushAt(i, now);
            if (closing)
            {
                var (x, y, s, o) = BigTypeScreensMotion.ShelfIconOut(dc, dr, now - _closeStart, still);
                shift += new Vector(x, y);
                scale *= s;
                opacity = o;
                moving |= now - _closeStart < BigTypeScreensMotion.ShelfOutTotal(still);
            }
            else if (now - _openStart < BigTypeScreensMotion.ShelfInTotal(still))
            {
                var (s, o) = BigTypeScreensMotion.ShelfIconIn(dc, dr, now - _openStart, still);
                scale *= s;
                opacity = o;
                moving = true;
            }
            if (opacity <= 0)
                continue;
            var centre = cell.Center + shift;
            var box = new Rect(centre.X - (cell.Width * scale / 2), centre.Y - (cell.Height * scale / 2), cell.Width * scale, cell.Height * scale);
            if (i == _focus && !closing)
            {
                // Drawn by its own child (it bobs on the compositor), placed once this pass is over.
                focus = (apps[i], box, opacity);
                continue;
            }
            using (context.PushOpacity(opacity))
            using (context.PushClip(new RoundedRect(box, corner * scale)))
            {
                if (apps[i].App.Icon is { } icon)
                    context.DrawImage(icon, new Rect(icon.Size), Fit(icon.Size, box));
                else
                    context.FillRectangle(HandheldTokens.TileRest, box);
            }
        }
        moving |= _scale.Count > 0 && !AllSettled(now);
        // The one ring, on the focused icon's resting box (the page's ring frames the box, not the grown icon).
        if (_ringShown && !closing && _focus >= 0 && _focus < apps.Count)
        {
            var cell = Cell(_focus, first).Inflate(HandheldTokens.ShelfRingOutset);
            ringTarget = new BigTypeRing((float)cell.X, (float)cell.Y, (float)cell.Width, (float)cell.Height, (float)HandheldTokens.ShelfRingRadius);
        }
        // The children (the focused icon, the ring) are placed after this pass: a render pass may not change the tree it draws.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PlaceChildren(focus, ringTarget, now, still), Avalonia.Threading.DispatcherPriority.Background);
        if (moving)
            Request();
    }

    private void PlaceChildren((HandheldDrawerAppViewModel App, Rect Box, double Opacity)? focus, BigTypeRing? ringTarget, float now, bool still)
    {
        if (focus is { } f)
            PlaceFocus(f.App, f.Box, f.Opacity, now, still);
        else
            HideFocus();
        if (ringTarget is { } target)
        {
            if (_ring.Place(target, now, still, IsEffectivelyVisible))
                Request();
        }
        else
        {
            _ring.Hide();
        }
    }

    private void PlaceFocus(HandheldDrawerAppViewModel app, Rect box, double opacity, float now, bool still)
    {
        _focusTile.IsVisible = true;
        _focusImage.Source = app.App.Icon;
        _focusTile.Background = app.App.Icon is null ? HandheldTokens.TileRest : null;
        var scale = box.Width / HandheldTokens.ShelfIcon;
        var matrix = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(Math.Round(box.X, 2), Math.Round(box.Y, 2));
        if (_focusTile.RenderTransform?.Value != matrix)
            _focusTile.RenderTransform = new MatrixTransform(matrix);
        _focusTile.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        _focusTile.Opacity = Math.Round(opacity, 3);
        if (_bobbing != _focus)
        {
            HandheldScreenRing.StopBob(_focusTile);
            _bobbing = _focus;
            _ring.Bob(_focusTile, now, still || !IsEffectivelyVisible);
        }
    }

    private void HideFocus()
    {
        if (_bobbing >= 0)
            HandheldScreenRing.StopBob(_focusTile);
        _bobbing = -1;
        _focusTile.IsVisible = false;
    }

    private bool AllSettled(float now)
    {
        foreach (var (_, s) in _scale)
            if (now - s.Start < BigTypeScreensMotion.ShelfFocusMilliseconds)
                return false;
        return true;
    }

    // An app's icon fitted whole into its box (Uniform), centred.
    private static Rect Fit(Size source, Rect box)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return box;
        var k = Math.Min(box.Width / source.Width, box.Height / source.Height);
        var w = source.Width * k;
        var h = source.Height * k;
        return new Rect(box.X + ((box.Width - w) / 2), box.Y + ((box.Height - h) / 2), w, h);
    }

    private long _requestedAt;

    private void Request(float afterMilliseconds = 0f)
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
        {
            if (!_ticking)
                _surface.InvalidateVisual();
            return;
        }
        _ticking = true;
        _requestedAt = Stopwatch.GetTimestamp();
        top.RequestAnimationFrame(_ => Tick(afterMilliseconds));
    }

    private void Tick(float afterMilliseconds)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            _ticking = false;
            return;
        }
        // At rest the breath and the bob step at 30 Hz (B5's rule), not at the panel's rate.
        if (afterMilliseconds > 0f && Stopwatch.GetElapsedTime(_requestedAt).TotalMilliseconds < afterMilliseconds)
        {
            top.RequestAnimationFrame(_ => Tick(afterMilliseconds));
            return;
        }
        _ticking = false;
        if (IsEffectivelyVisible)
            _surface.InvalidateVisual();
    }

    // ---- Touch: today's (a tap opens, a held finger pins, a drag scrolls whole rows) ----

    private Point? _press;
    private float _pressFirstRow;
    private bool _dragged;
    private DateTimeOffset _lastHoldAt = DateTimeOffset.MinValue;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _press = e.GetPosition(this);
        _pressFirstRow = _firstRow.Target;
        _dragged = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_press is not { } start || _companion is not { } companion)
            return;
        var dy = e.GetPosition(this).Y - start.Y;
        if (!_dragged && Math.Abs(dy) < 24)
            return;
        _dragged = true;
        var rows = (companion.DrawerApps.Count + Columns - 1) / Columns;
        var target = Math.Clamp(MathF.Round(_pressFirstRow - (float)(dy / HandheldTokens.ShelfPitchY)), 0f, Math.Max(0, rows - HandheldTokens.ShelfRows));
        _firstRow.To(target, Now, Still);
        Request();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var start = _press;
        _press = null;
        if (start is null || _dragged || _companion is not { } companion)
            return;
        // The tap some input stacks send as a long press ends must not also open the app just pinned.
        if (DateTimeOffset.UtcNow - _lastHoldAt < TimeSpan.FromMilliseconds(500))
            return;
        if (IndexAt(e.GetPosition(this)) is { } index)
            companion.TapDrawerApp(companion.DrawerApps[index]);
    }

    private void OnHolding(object? sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != HoldingState.Started || _press is not { } at || _dragged || _companion is not { } companion || companion.Model.CanClearSlot)
            return;
        if (IndexAt(at) is not { } index)
            return;
        _lastHoldAt = DateTimeOffset.UtcNow;
        companion.PinToDock(companion.DrawerApps[index]);
        e.Handled = true;
    }

    private int? IndexAt(Point point)
    {
        if (_companion is not { } companion)
            return null;
        var first = _firstRow.At(Now);
        var columns = Columns;
        var column = (int)Math.Floor((point.X - HandheldTokens.ShelfLeft + ((HandheldTokens.ShelfPitchX - HandheldTokens.ShelfIcon) / 2)) / HandheldTokens.ShelfPitchX);
        var row = (int)Math.Floor(((point.Y - HandheldTokens.ShelfFirstTop + ((HandheldTokens.ShelfPitchY - HandheldTokens.ShelfIcon) / 2)) / HandheldTokens.ShelfPitchY) + first);
        if (column < 0 || column >= columns || row < 0)
            return null;
        var index = (row * columns) + column;
        return index < companion.DrawerApps.Count ? index : null;
    }
}
