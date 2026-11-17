using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Every achievement as the Wall (M47 B15, proposals-screens.html option 4, the owner's pick of 16 November 2026): all of a game's badges on
/// the colour, seven across (eleven on the 16:9 screen) and three rows at a time, a locked one grey and dimmed, a hardcore unlock edged in
/// gold, a hidden one a question mark until A reveals it, the focused one grown under the one white ring. Opening, the badges grow in from
/// the top left; closing, they drop away. A filter (LB / RB, or a tap on its word) slides the badges it keeps to their new places and
/// shrinks the rest away; a refresh that finds a new unlock pulses its badge as it takes its colour. Drawn as one control: only the rows in
/// sight are drawn and only their badges are loaded, so a set of hundreds costs what three rows cost. Touch is today's: a tap selects a
/// badge, a drag scrolls whole rows. Numbers: tokens.js <c>screens.Wall…</c> and <c>motion.Wall…</c>, timing <see cref="BigTypeScreensMotion"/>.
/// </summary>
public sealed class HandheldBadgeWall : Panel
{
    private HandheldCompanionViewModel? _companion;
    private readonly HandheldScreenRing _ring;
    private readonly BigTypeGlide _firstRow = new(0f, BigTypeScreensMotion.ShelfScrollMilliseconds, BigTypeScreensMotion.Soft);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<AchievementRowViewModel, Placed> _placed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<AchievementRowViewModel> _requested = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AchievementRowViewModel, (float From, float To, float Start)> _scale = new(ReferenceEqualityComparer.Instance);
    private static readonly ConditionalWeakTable<Bitmap, Bitmap> Greyed = [];
    private IReadOnlyList<AchievementRowViewModel> _items = [];
    private AchievementRowViewModel? _focus;
    private float _openStart = float.NegativeInfinity;
    private float _closeStart = float.NaN;
    private bool _ringShown;
    private bool _ticking;

    // Where a badge is: its slot now, the slot it is moving from, when it left it; a badge the filter left out shrinks from its last slot.
    private sealed class Placed(int slot)
    {
        public int Slot { get; set; } = slot;
        public int FromSlot { get; set; } = slot;
        public float MoveStart { get; set; } = float.NegativeInfinity;
        public float OutStart { get; set; } = float.NaN;
        public bool WasUnlocked { get; set; }
        public float PulseStart { get; set; } = float.NegativeInfinity;
    }

    public HandheldBadgeWall()
    {
        ClipToBounds = true;
        Focusable = false;
        Background = Brushes.Transparent;
        _surface = new Surface(Draw) { IsHitTestVisible = false };
        Children.Add(_surface);
        _ring = new HandheldScreenRing(this);
    }

    private float Now => (float)_clock.Elapsed.TotalMilliseconds;

    private bool Still => _companion?.HoldsStill == true || !HandheldRollingText.GetRolls(this);

    private int Columns => _companion?.IsOnWideScreen == true ? HandheldTokens.WallWideColumns : HandheldTokens.WallColumns;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_companion is { } old)
        {
            old.PropertyChanged -= OnCompanionChanged;
            old.Model.PropertyChanged -= OnModelChanged;
        }
        _companion = DataContext as HandheldCompanionViewModel;
        if (_companion is { } companion)
        {
            companion.PropertyChanged += OnCompanionChanged;
            companion.Model.PropertyChanged += OnModelChanged;
        }
        Request();
    }

    private void OnCompanionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HandheldCompanionViewModel.WallItems) && float.IsNaN(_closeStart) && _companion is { IsAchievementsLayer: true })
            Arrange(animate: true);
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SecondScreenViewModel.SelectedAchievement) && float.IsNaN(_closeStart) && _companion is { IsAchievementsLayer: true })
            Follow(opening: false);
        else if (e.PropertyName is nameof(SecondScreenViewModel.UnlockedAchievementCount) or nameof(SecondScreenViewModel.AchievementCount))
            Request();
    }

    /// <summary>Every badge opened: the badges grow in, the ring pops in on the focused one.</summary>
    public void Open()
    {
        _closeStart = float.NaN;
        _placed.Clear();
        _scale.Clear();
        _focus = null;
        _ringShown = false;
        _openStart = Still ? float.NegativeInfinity : Now;
        Arrange(animate: false);
        Follow(opening: true);
    }

    /// <summary>The wall closed: its badges drop away where they are (their places kept, whatever the filter does next).</summary>
    public void Close()
    {
        _closeStart = Still ? float.NegativeInfinity : Now;
        _ringShown = false;
        _ring.Hide();
        Request();
    }

    /// <summary>How long the closing takes.</summary>
    public float CloseMilliseconds(bool still) => BigTypeScreensMotion.WallOutTotal(Columns, still);

    // The filter's list changed: each badge kept slides from where it was to its new slot, each left out shrinks away, each new one is placed.
    private void Arrange(bool animate)
    {
        if (_companion is not { } companion)
            return;
        var now = Now;
        var still = Still;
        _items = companion.WallItems;
        var kept = new HashSet<AchievementRowViewModel>(ReferenceEqualityComparer.Instance);
        for (var slot = 0; slot < _items.Count; slot++)
        {
            var row = _items[slot];
            kept.Add(row);
            if (_placed.TryGetValue(row, out var placed) && float.IsNaN(placed.OutStart))
            {
                if (placed.Slot != slot)
                {
                    placed.FromSlot = animate && !still ? placed.Slot : slot;
                    placed.MoveStart = now;
                    placed.Slot = slot;
                }
            }
            else
            {
                _placed[row] = new Placed(slot) { WasUnlocked = row.IsUnlocked };
            }
        }
        foreach (var (row, placed) in _placed.ToArray())
        {
            if (kept.Contains(row))
                continue;
            if (!animate || still)
                _placed.Remove(row);
            else if (float.IsNaN(placed.OutStart))
                placed.OutStart = now;
        }
        Follow(opening: false);
    }

    private void Follow(bool opening)
    {
        if (_companion is not { } companion)
            return;
        var now = Now;
        var still = Still;
        _ring.Motion.HoldStill = still;
        _ring.Motion.Input(now);
        var focus = companion.Model.SelectedAchievement;
        var slot = focus is null ? -1 : IndexOf(focus);
        var columns = Columns;
        if (slot >= 0)
        {
            var row = slot / columns;
            var first = (int)MathF.Round(_firstRow.Target);
            if (row < first)
                first = row;
            if (row > first + HandheldTokens.WallRows - 1)
                first = row - (HandheldTokens.WallRows - 1);
            if (_items.Count <= columns * HandheldTokens.WallRows)
                first = 0;
            if (opening)
                _firstRow.Jump(first);
            else
                _firstRow.To(first, now, still);
        }
        if (!ReferenceEquals(focus, _focus))
        {
            void Scale(AchievementRowViewModel? row, float to)
            {
                if (row is null)
                    return;
                var from = ScaleAt(row, now);
                _scale[row] = (from, to, still ? float.NegativeInfinity : now);
            }
            Scale(_focus, 1f);
            Scale(focus, (float)HandheldTokens.WallFocusScale);
            var hadFocus = _focus is not null && _ringShown;
            _focus = focus;
            if (slot >= 0)
            {
                _ring.Motion.MoveRing(now, instant: opening || !hadFocus || still, pop: false);
                if (opening && !still)
                    _ring.Motion.PopIn(now, BigTypeMotion.PopFromScale);
                _ringShown = true;
            }
        }
        Request();
    }

    private int IndexOf(AchievementRowViewModel row)
    {
        for (var i = 0; i < _items.Count; i++)
            if (ReferenceEquals(_items[i], row))
                return i;
        return -1;
    }

    private float ScaleAt(AchievementRowViewModel row, float now) => _scale.TryGetValue(row, out var s)
        ? BigTypeScreensMotion.Focus(s.From, s.To, now - s.Start, Still) : 1f;

    private Point SlotOrigin(float slot, float firstRow)
    {
        var columns = Columns;
        var s = (int)slot;
        return new Point(HandheldTokens.WallLeft + ((s % columns) * HandheldTokens.WallPitch),
            HandheldTokens.WallFirstTop + (((s / columns) - firstRow) * HandheldTokens.WallPitch));
    }

    // What the grid draws itself, under its children (the focused icon, the ring); Panel's own Render is sealed.
    private sealed class Surface(Action<DrawingContext> draw) : Control
    {
        public override void Render(DrawingContext context) => draw(context);
    }

    private readonly Surface _surface;

    private void Draw(DrawingContext context)
    {
        if (_companion is null)
            return;
        var now = Now;
        var still = Still;
        var first = _firstRow.At(now);
        var moving = !_firstRow.Settled(now);
        var columns = Columns;
        var closing = !float.IsNaN(_closeStart);
        var size = HandheldTokens.WallBadge;
        var lastRow = first + HandheldTokens.WallRows + 1;
        foreach (var (row, placed) in _placed)
        {
            // Where it is between its old slot and its new one (WallMove).
            var moved = BigTypeScreensMotion.WallMoved(now - placed.MoveStart, still);
            moving |= moved < 1f;
            var from = SlotOrigin(placed.FromSlot, first);
            var to = SlotOrigin(placed.Slot, first);
            var origin = from + ((to - from) * moved);
            var rowAt = (origin.Y - HandheldTokens.WallFirstTop) / HandheldTokens.WallPitch;
            if (rowAt < -1.2 || rowAt > lastRow)
                continue;
            RequestBadge(row);
            double scale = ScaleAt(row, now), opacity = 1, dropY = 0;
            if (!float.IsNaN(placed.OutStart))
            {
                var (s, o) = BigTypeScreensMotion.WallBadgeFiltered(now - placed.OutStart, still);
                scale *= s;
                opacity = o;
                if (o <= 0f && now - placed.OutStart >= BigTypeScreensMotion.WallFilterOutMilliseconds)
                    continue;
                moving = true;
            }
            var column = placed.Slot % columns;
            var visibleRow = Math.Max(0, (int)Math.Round(rowAt));
            if (closing)
            {
                var (drop, o) = BigTypeScreensMotion.WallBadgeOut(column, now - _closeStart, still);
                dropY = drop;
                opacity *= o;
                moving |= now - _closeStart < BigTypeScreensMotion.WallOutTotal(columns, still);
            }
            else if (now - _openStart < BigTypeScreensMotion.WallInTotal(columns, HandheldTokens.WallRows + 1, still))
            {
                var (s, o) = BigTypeScreensMotion.WallBadgeIn(column, visibleRow, now - _openStart, still);
                scale *= s;
                opacity *= o;
                moving = true;
            }
            // A refresh's new unlock pulses as it takes its colour.
            if (!placed.WasUnlocked && row.IsUnlocked)
            {
                placed.WasUnlocked = true;
                placed.PulseStart = still ? float.NegativeInfinity : now;
            }
            var pulse = BigTypeScreensMotion.Pulse(now - placed.PulseStart, still);
            moving |= pulse != 1f;
            scale *= pulse;
            if (opacity <= 0)
                continue;
            var centre = new Point(origin.X + (size / 2), origin.Y + (size / 2) + dropY);
            var box = new Rect(centre.X - (size * scale / 2), centre.Y - (size * scale / 2), size * scale, size * scale);
            using (context.PushOpacity(opacity))
                DrawBadge(context, row, box, scale);
        }
        foreach (var (row, placed) in _placed.ToArray())
            if (!float.IsNaN(placed.OutStart) && now - placed.OutStart >= BigTypeScreensMotion.WallFilterOutMilliseconds)
                _placed.Remove(row);
        foreach (var (_, s) in _scale)
            moving |= now - s.Start < BigTypeScreensMotion.ShelfFocusMilliseconds;
        // The one ring on the focused badge's resting box.
        if (_ringShown && !closing && _focus is { } focus && IndexOf(focus) is var slotOfFocus and >= 0)
        {
            var origin = SlotOrigin(slotOfFocus, first);
            var cell = new Rect(origin, new Size(size, size)).Inflate(HandheldTokens.WallRingOutset);
            var target = new BigTypeRing((float)cell.X, (float)cell.Y, (float)cell.Width, (float)cell.Height, (float)HandheldTokens.WallRingRadius);
            PostRing(target, now, still);
        }
        else
        {
            PostRing(null, now, still);
        }
        if (moving)
            Request();
    }

    // The ring is a child: it is placed after this pass (a render pass may not change the tree it draws).
    private void PostRing(BigTypeRing? target, float now, bool still) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (target is not { } t)
                _ring.Hide();
            else if (_ring.Place(t, now, still, IsEffectivelyVisible))
                Request();
        }, Avalonia.Threading.DispatcherPriority.Background);

    private static void DrawBadge(DrawingContext context, AchievementRowViewModel row, Rect box, double scale)
    {
        var corner = HandheldTokens.WallBadgeCorner.TopLeft * scale;
        var shape = new RoundedRect(box, corner);
        // A hardcore unlock: a gold edge just outside (the page's box-shadow 0 0 0 3px).
        if (row.IsHardcore)
        {
            var edge = HandheldTokens.WallHardcoreEdge * scale;
            context.DrawRectangle(HandheldTokens.WallHardcoreBrush, null, new RoundedRect(box.Inflate(edge), corner + edge));
        }
        if (row.CanReveal)
        {
            // Hidden until A reveals it: a faint ground and a question mark.
            context.DrawRectangle(HandheldTokens.WallHiddenGround, null, shape);
            var mark = new FormattedText("?", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(HandheldTokens.Face ?? FontFamily.Default, FontStyle.Normal, HandheldTokens.WeightDisplay),
                HandheldTokens.WallHiddenMark * scale, HandheldTokens.OnColour);
            context.DrawText(mark, new Point(box.Center.X - (mark.Width / 2), box.Center.Y - (mark.Height / 2)));
            return;
        }
        using (context.PushClip(shape))
        {
            if (row.Badge is { } badge)
            {
                var image = row.IsLocked ? Grey(badge) : badge;
                context.DrawImage(image, new Rect(image.Size), box);
            }
            else
            {
                context.FillRectangle(HandheldTokens.TileRest, box);
            }
        }
    }

    // A locked badge: grey at WallLockedBrightness (the page's grayscale(1) brightness(.62)), made once per picture.
    private static Bitmap Grey(Bitmap badge) => Greyed.GetValue(badge, MakeGrey);

    private static Bitmap MakeGrey(Bitmap badge)
    {
        var size = badge.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var target = new WriteableBitmap(size, badge.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = target.Lock();
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            badge.CopyPixels(new PixelRect(size), pin.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            pin.Free();
        }
        var k = HandheldTokens.WallLockedBrightness;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var luma = ((pixels[i] * 0.114) + (pixels[i + 1] * 0.587) + (pixels[i + 2] * 0.299)) * k;
            var v = (byte)Math.Clamp(Math.Round(luma), 0, 255);
            pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
        }
        for (var y = 0; y < size.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(pixels, y * stride, buffer.Address + (y * buffer.RowBytes), stride);
        return target;
    }

    // A badge in sight loads once (today's grid loads a tile's badge as it appears).
    private void RequestBadge(AchievementRowViewModel row)
    {
        if (row.Badge is not null || !_requested.Add(row))
            return;
        _ = LoadAsync(row);
    }

    private async System.Threading.Tasks.Task LoadAsync(AchievementRowViewModel row)
    {
        try
        {
            await row.LoadBadgeAsync(row.BadgeName);
        }
        catch (Exception)
        {
            // No badge: its place stays on the ground.
        }
        Request();
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
        if (afterMilliseconds > 0f && Stopwatch.GetElapsedTime(_requestedAt).TotalMilliseconds < afterMilliseconds)
        {
            top.RequestAnimationFrame(_ => Tick(afterMilliseconds));
            return;
        }
        _ticking = false;
        if (IsEffectivelyVisible)
            _surface.InvalidateVisual();
    }

    // ---- Touch: today's (a tap selects a badge, a drag scrolls whole rows) ----

    private Point? _press;
    private float _pressFirstRow;
    private bool _dragged;

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
        if (_press is not { } start)
            return;
        var dy = e.GetPosition(this).Y - start.Y;
        if (!_dragged && Math.Abs(dy) < 24)
            return;
        _dragged = true;
        var rows = (_items.Count + Columns - 1) / Columns;
        var target = Math.Clamp(MathF.Round(_pressFirstRow - (float)(dy / HandheldTokens.WallPitch)), 0f, Math.Max(0, rows - HandheldTokens.WallRows));
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
        var point = e.GetPosition(this);
        var first = _firstRow.At(Now);
        var columns = Columns;
        var gap = (HandheldTokens.WallPitch - HandheldTokens.WallBadge) / 2;
        var column = (int)Math.Floor((point.X - HandheldTokens.WallLeft + gap) / HandheldTokens.WallPitch);
        var row = (int)Math.Floor(((point.Y - HandheldTokens.WallFirstTop + gap) / HandheldTokens.WallPitch) + first);
        if (column < 0 || column >= columns || row < 0)
            return;
        var index = (row * columns) + column;
        if (index < _items.Count)
            companion.Model.SelectAchievement(_items[index]);
    }
}
