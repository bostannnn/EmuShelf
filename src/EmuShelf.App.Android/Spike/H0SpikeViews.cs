#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace EmuShelf.App.Android.Spike;

// M45 H0 device spike: throwaway code, Debug builds only. These are deliberately crude stand-ins
// for the Handheld Launcher and Stage. They exist to measure whether two Avalonia surfaces can run
// the mode's motion at the same time on the Thor. Nothing here is the real H1 design.

/// <summary>The one model both screens and both input paths share: selection on a 5×4 grid.</summary>
internal sealed class H0Model
{
    public const int Columns = 5;
    public const int Rows = 4;
    public const int Count = Columns * Rows;

    public int Index { get; private set; }
    public bool IsSwapped { get; set; }

    /// <summary>When the last move landed, for the top screen's held-direction debounce.</summary>
    public long LastMoveTicks { get; private set; }

    /// <summary>Raised after a move: (old index, new index).</summary>
    public event Action<int, int>? Moved;

    /// <summary>Raised when a move runs into a wall: (dx, dy).</summary>
    public event Action<int, int>? Bumped;

    public void Move(int dx, int dy)
    {
        var col = Index % Columns + dx;
        var row = Index / Columns + dy;
        if (col < 0 || col >= Columns || row < 0 || row >= Rows)
        {
            Bumped?.Invoke(dx, dy);
            return;
        }
        var old = Index;
        Index = row * Columns + col;
        LastMoveTicks = Environment.TickCount64;
        Moved?.Invoke(old, Index);
    }
}

/// <summary>Frame-interval statistics for one surface, gathered from its RequestAnimationFrame ticks.</summary>
internal sealed class H0FrameStats
{
    private readonly List<double> _window = new();
    private readonly List<double> _total = new();
    private TimeSpan? _last;

    public H0FrameStats(string name) => Name = name;

    public string Name { get; }

    /// <summary>The refresh rate of the display currently hosting this surface.</summary>
    public double RefreshHz { get; set; } = 60;

    public void Tick(TimeSpan timestamp)
    {
        if (_last is { } last)
        {
            var ms = (timestamp - last).TotalMilliseconds;
            // A gap far beyond a frame means the surface was idle (nothing animating), not a dropped frame.
            if (ms > 0 && ms < 250)
            {
                _window.Add(ms);
                _total.Add(ms);
            }
        }
        _last = timestamp;
    }

    /// <summary>Marks the end of a continuous run, so the next tick does not count the idle gap.</summary>
    public void Break() => _last = null;

    public string TakeWindow()
    {
        var line = Describe(_window);
        _window.Clear();
        return line;
    }

    public string DescribeTotal() => Describe(_total);

    public void ResetTotal() => _total.Clear();

    private string Describe(List<double> samples)
    {
        if (samples.Count == 0)
            return $"{Name} @{RefreshHz:0}Hz: idle";
        var sorted = samples.OrderBy(x => x).ToArray();
        double P(double p) => sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];
        var budget = 1000.0 / RefreshHz;
        var late = samples.Count(x => x > budget * 1.5);
        var mean = samples.Average();
        return $"{Name} @{RefreshHz:0}Hz: n={samples.Count} fps={1000 / mean:0.0} p50={P(0.5):0.0} " +
               $"p95={P(0.95):0.0} p99={P(0.99):0.0} max={sorted[^1]:0.0}ms " +
               $"over1.5x={late} ({100.0 * late / samples.Count:0.0}%)";
    }
}

/// <summary>Decodes the spike's 20 "covers" off the UI thread: real PNGs, each decoded on its own.</summary>
internal static class H0Covers
{
    private static readonly string[] Sources =
    {
        "3ds.png", "playstation2.png", "playstation3.png", "psp.png", "steam.png", "wii.png",
    };

    public static Task<Bitmap[]> DecodeAsync(int widthPx) => Task.Run(() =>
    {
        var bitmaps = new Bitmap[H0Model.Count];
        for (var i = 0; i < bitmaps.Length; i++)
        {
            var uri = new Uri($"avares://EmuShelf.UI/Assets/PlatformConsoleArt/{Sources[i % Sources.Length]}");
            using var stream = AssetLoader.Open(uri);
            bitmaps[i] = Bitmap.DecodeToWidth(stream, widthPx, BitmapInterpolationMode.MediumQuality);
        }
        return bitmaps;
    });

    public static string Title(int index) => $"Game {index + 1:00} — {Sources[index % Sources.Length].Replace(".png", "")}";
}

/// <summary>A critically-damped-ish spring that keeps its velocity when retargeted.</summary>
internal sealed class H0Spring
{
    private readonly double _stiffness;
    private readonly double _damping;

    public H0Spring(double stiffness, double damping)
    {
        _stiffness = stiffness;
        _damping = damping;
    }

    public double Value { get; set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }

    public bool IsSettled => Math.Abs(Target - Value) < 0.05 && Math.Abs(Velocity) < 0.5;

    public void Step(double dt)
    {
        // Fixed 240 Hz substeps keep the integration stable whatever the frame interval.
        const double sub = 1.0 / 240;
        while (dt > 0)
        {
            var h = Math.Min(sub, dt);
            var accel = -_stiffness * (Value - Target) - _damping * Velocity;
            Velocity += accel * h;
            Value += Velocity * h;
            dt -= h;
        }
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0;
        }
    }
}

/// <summary>The spec's top-screen focus spring (stiffness 190, damping 17) as a composition easing.</summary>
internal sealed class H0SpringEasing : Easing
{
    private const double Omega = 13.784; // sqrt(190)
    private const double Zeta = 0.6166;  // 17 / (2 * sqrt(190))
    public const double DurationSeconds = 0.6;

    public override double Ease(double progress)
    {
        var t = progress * DurationSeconds;
        var wd = Omega * Math.Sqrt(1 - Zeta * Zeta);
        var decay = Math.Exp(-Zeta * Omega * t);
        return 1 - decay * (Math.Cos(wd * t) + Zeta * Omega / wd * Math.Sin(wd * t));
    }
}

internal static class H0Composition
{
    public static void Animate(Visual control, string property, float to, double ms, Easing? easing = null, float? from = null)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
            return;
        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        if (from is { } start)
            anim.InsertKeyFrame(0f, start);
        anim.InsertKeyFrame(1f, to, easing ?? new CubicEaseOut());
        anim.Duration = TimeSpan.FromMilliseconds(ms);
        visual.StartAnimation(property, anim);
    }

    public static void AnimateScale(Visual control, float to, double ms, Easing? easing = null, float? from = null)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
            return;
        visual.CenterPoint = new Vector3D(control.Bounds.Width / 2, control.Bounds.Height / 2, 0);
        var anim = visual.Compositor.CreateVector3DKeyFrameAnimation();
        if (from is { } start)
            anim.InsertKeyFrame(0f, new Vector3D(start, start, 1));
        anim.InsertKeyFrame(1f, new Vector3D(to, to, 1), easing ?? new CubicEaseOut());
        anim.Duration = TimeSpan.FromMilliseconds(ms);
        visual.StartAnimation("Scale", anim);
    }

    public static void Pulse(Visual control, float low, float high, double halfCycleMs)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
            return;
        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, low);
        anim.InsertKeyFrame(1f, high, new SineEaseInOut());
        anim.Duration = TimeSpan.FromMilliseconds(halfCycleMs);
        anim.IterationBehavior = global::Avalonia.Rendering.Composition.Animations.AnimationIterationBehavior.Forever;
        anim.Direction = global::Avalonia.Animation.PlaybackDirection.Alternate;
        visual.StartAnimation("Opacity", anim);
    }
}

/// <summary>Bottom screen stand-in: 20 cover tiles, a springing selector, focus lift, glow, edge bump.</summary>
internal sealed class H0LauncherView : Canvas
{
    private static readonly IBrush Indigo = new SolidColorBrush(Color.Parse("#4F46E5"));

    private readonly H0Model _model;
    private readonly H0FrameStats _stats;
    private readonly Border[] _tiles = new Border[H0Model.Count];
    private readonly Image[] _covers = new Image[H0Model.Count];
    private readonly Image[] _shadows = new Image[H0Model.Count];
    private readonly Border _selector;
    private readonly Image _glow;
    private readonly TranslateTransform _selectorTranslate = new();
    private readonly TranslateTransform _glowTranslate = new();
    private readonly H0Spring _x = new(560, 43);
    private readonly H0Spring _y = new(560, 43);
    private double _tile;
    private double _pitchX;
    private double _pitchY;
    private double _originX;
    private double _originY;
    private bool _ticking;
    private TimeSpan? _lastFrame;
    private int _decodedFor;

    public H0LauncherView(H0Model model, H0FrameStats stats)
    {
        _model = model;
        _stats = stats;
        Background = new SolidColorBrush(Color.Parse("#EEF0F5"));
        ClipToBounds = true;

        for (var i = 0; i < H0Model.Count; i++)
        {
            _shadows[i] = new Image { IsHitTestVisible = false };
            Children.Add(_shadows[i]);
        }
        _glow = new Image { IsHitTestVisible = false, RenderTransform = _glowTranslate };
        Children.Add(_glow);
        for (var i = 0; i < H0Model.Count; i++)
        {
            _covers[i] = new Image { Stretch = Stretch.UniformToFill };
            _tiles[i] = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#D9DCE6")),
                ClipToBounds = true,
                Child = _covers[i],
            };
            Children.Add(_tiles[i]);
        }

        // The selector: thick indigo frame with a white keyline inside and out.
        _selector = new Border
        {
            BorderBrush = Brushes.White,
            IsHitTestVisible = false,
            RenderTransform = _selectorTranslate,
            Child = new Border
            {
                BorderBrush = Indigo,
                Child = new Border { BorderBrush = Brushes.White },
            },
        };
        Children.Add(_selector);

        _model.Moved += OnMoved;
        _model.Bumped += OnBumped;
        SizeChanged += (_, _) => Relayout();
        AttachedToVisualTree += (_, _) =>
        {
            Relayout();
            H0Composition.Pulse(_glow, 0.55f, 1f, 600);
            Dispatcher.UIThread.Post(() => H0Composition.AnimateScale(_tiles[_model.Index], 1.13f, 1), DispatcherPriority.Render);
        };
        DetachedFromVisualTree += (_, _) => { _ticking = false; _stats.Break(); };
    }

    private void Relayout()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;
        // Room for the edge margin and the lifted ring: 5.6 tile pitches across, 4.9 down.
        _tile = Math.Floor(Math.Min(w / 6.0, h / 5.2));
        _pitchX = _tile * 1.18;
        _pitchY = _tile * 1.18;
        _originX = (w - (_pitchX * (H0Model.Columns - 1) + _tile)) / 2;
        _originY = (h - (_pitchY * (H0Model.Rows - 1) + _tile)) / 2;

        var radius = new CornerRadius(_tile * 0.18);
        (_shadows[0].Source as IDisposable)?.Dispose();
        (_glow.Source as IDisposable)?.Dispose();
        var shadow = RenderShadow(_tile, radius, 14, Color.Parse("#40202640"));
        var glowPad = _tile * 0.22;
        var glow = RenderShadow(_tile * 1.13, new CornerRadius(_tile * 0.2), glowPad, Color.Parse("#B04F46E5"));
        for (var i = 0; i < H0Model.Count; i++)
        {
            var (x, y) = Position(i);
            _tiles[i].Width = _tile;
            _tiles[i].Height = _tile;
            _tiles[i].CornerRadius = radius;
            SetLeft(_tiles[i], x);
            SetTop(_tiles[i], y);
            _shadows[i].Source = shadow.Bitmap;
            _shadows[i].Width = shadow.Size;
            _shadows[i].Height = shadow.Size;
            SetLeft(_shadows[i], x - (shadow.Size - _tile) / 2);
            SetTop(_shadows[i], y - (shadow.Size - _tile) / 2 + 6);
        }
        _glow.Source = glow.Bitmap;
        _glow.Width = glow.Size;
        _glow.Height = glow.Size;
        SetLeft(_glow, -(glow.Size - _tile) / 2);
        SetTop(_glow, -(glow.Size - _tile) / 2);

        // Frame ~7.5% of the tile, sized around the 1.13× lifted tile, with 2.5% white keylines.
        var lifted = _tile * 1.13;
        var key = Math.Max(2, _tile * 0.025);
        var frame = Math.Max(4, _tile * 0.075);
        var outer = lifted + 2 * (frame + 2 * key);
        _selector.Width = outer;
        _selector.Height = outer;
        _selector.BorderThickness = new Thickness(key);
        _selector.CornerRadius = new CornerRadius(_tile * 0.18 + frame + 2 * key);
        var mid = (Border)_selector.Child!;
        mid.BorderThickness = new Thickness(frame);
        mid.CornerRadius = new CornerRadius(_tile * 0.18 + frame + key);
        ((Border)mid.Child!).BorderThickness = new Thickness(key);
        ((Border)mid.Child!).CornerRadius = new CornerRadius(_tile * 0.18 + key);
        SetLeft(_selector, -(outer - _tile) / 2);
        SetTop(_selector, -(outer - _tile) / 2);

        var (sx, sy) = Position(_model.Index);
        _x.Value = _x.Target = sx;
        _y.Value = _y.Target = sy;
        ApplySelector();

        var px = (int)Math.Ceiling(_tile * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        if (px > 0 && px != _decodedFor)
        {
            _decodedFor = px;
            _ = DecodeAsync(px);
        }
    }

    private async Task DecodeAsync(int px)
    {
        var started = Environment.TickCount64;
        var bitmaps = await H0Covers.DecodeAsync(px);
        if (px != _decodedFor)
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
            return;
        }
        for (var i = 0; i < bitmaps.Length; i++)
        {
            (_covers[i].Source as IDisposable)?.Dispose();
            _covers[i].Source = bitmaps[i];
        }
        global::Android.Util.Log.Info("EmuShelfH0",
            $"Launcher: decoded {bitmaps.Length} tiles at {px}px in {Environment.TickCount64 - started} ms (off the UI thread)");
    }

    private (double X, double Y) Position(int index) =>
        (_originX + index % H0Model.Columns * _pitchX, _originY + index / H0Model.Columns * _pitchY);

    private void OnMoved(int from, int to)
    {
        var (x, y) = Position(to);
        _x.Target = x;
        _y.Target = y;
        // The lift lives on the tile itself; nothing else animates the tile's scale.
        H0Composition.AnimateScale(_tiles[from], 1f, 160);
        H0Composition.AnimateScale(_tiles[to], 1.13f, 220, new BackEaseOut());
        // Raise the focused tile above its neighbours so the lift never hides under them.
        foreach (var tile in _tiles)
            tile.ZIndex = 0;
        _tiles[to].ZIndex = 1;
        _selector.ZIndex = 2;
        StartTicking();
    }

    private void OnBumped(int dx, int dy)
    {
        var (x, y) = Position(_model.Index);
        _x.Target = x + dx * _tile * 0.07;
        _y.Target = y + dy * _tile * 0.07;
        StartTicking();
        DispatcherTimer.RunOnce(() =>
        {
            var (bx, by) = Position(_model.Index);
            _x.Target = bx;
            _y.Target = by;
            StartTicking();
        }, TimeSpan.FromMilliseconds(90));
    }

    private void StartTicking()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _ticking = true;
        _lastFrame = null;
        top.RequestAnimationFrame(OnFrame);
    }

    private void OnFrame(TimeSpan now)
    {
        _stats.Tick(now);
        var dt = _lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 1.0 / 20) : 1.0 / 120;
        _lastFrame = now;
        _x.Step(dt);
        _y.Step(dt);
        ApplySelector();
        if (_x.IsSettled && _y.IsSettled)
        {
            _ticking = false;
            _stats.Break();
            return;
        }
        if (TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(OnFrame);
        else
            _ticking = false;
    }

    private void ApplySelector()
    {
        _selectorTranslate.X = _x.Value;
        _selectorTranslate.Y = _y.Value;
        _glowTranslate.X = _x.Value;
        _glowTranslate.Y = _y.Value;
    }

    /// <summary>A pre-rendered soft shadow: drawn once into a bitmap, never blurred per frame.</summary>
    private (Bitmap Bitmap, double Size) RenderShadow(double size, CornerRadius radius, double blur, Color color)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var total = size + 2 * blur;
        var px = new PixelSize((int)Math.Ceiling(total * scaling), (int)Math.Ceiling(total * scaling));
        var target = new RenderTargetBitmap(px, new Vector(96 * scaling, 96 * scaling));
        var shape = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = radius,
            BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = color }),
            Margin = new Thickness(blur),
        };
        shape.Measure(new Size(total, total));
        shape.Arrange(new Rect(0, 0, total, total));
        target.Render(shape);
        return (target, total);
    }
}

/// <summary>Top screen stand-in: drifting tile pattern, a big cover crossfading from the centre, a name plate.</summary>
internal sealed class H0StageView : Grid
{
    private readonly H0Model _model;
    private readonly H0FrameStats _stats;
    private readonly Canvas _pattern = new() { IsHitTestVisible = false };
    private readonly TranslateTransform _drift = new();
    private readonly Image[] _art = { new(), new() };
    private readonly TextBlock _title = new() { FontSize = 30, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#1E1B3A")) };
    private readonly TextBlock _meta = new() { FontSize = 18, Foreground = new SolidColorBrush(Color.Parse("#5B5F75")) };
    private readonly Border _plate;
    private readonly DispatcherTimer _settle;
    private Bitmap[]? _bitmaps;
    private int _front;
    private int _shown = -1;
    private int _decodedFor;
    private bool _ticking;
    private TimeSpan? _lastFrame;

    public H0StageView(H0Model model, H0FrameStats stats, bool drift)
    {
        _model = model;
        _stats = stats;
        Drift = drift;
        Background = new SolidColorBrush(Color.Parse("#F5F6FA"));
        ClipToBounds = true;

        _pattern.RenderTransform = _drift;
        Children.Add(_pattern);
        foreach (var art in _art)
        {
            art.Stretch = Stretch.Uniform;
            art.HorizontalAlignment = HorizontalAlignment.Center;
            art.VerticalAlignment = VerticalAlignment.Center;
            art.Opacity = 0;
            Children.Add(art);
        }
        _plate = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F2FFFFFF")),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(26, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 28),
            Child = new StackPanel { Children = { _title, _meta } },
        };
        Children.Add(_plate);

        // Held direction: presses under 150 ms apart wait until movement settles, so the top screen
        // changes once per move, not once per tile passed.
        _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Show(_model.Index);
        };
        _model.Moved += (_, to) =>
        {
            _settle.Stop();
            if (Environment.TickCount64 - _previousMove < 150)
                _settle.Start();
            else
                Show(to);
            _previousMove = Environment.TickCount64;
        };
        SizeChanged += (_, _) => Relayout();
        AttachedToVisualTree += (_, _) =>
        {
            Relayout();
            StartTicking();
        };
        DetachedFromVisualTree += (_, _) => { _ticking = false; _stats.Break(); };
    }

    private long _previousMove;

    public bool Drift { get; set; }

    private void Relayout()
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;
        var artSize = Math.Min(w * 0.6, h * 0.62);
        foreach (var art in _art)
        {
            art.Width = artSize;
            art.Height = artSize;
            art.Margin = new Thickness(0, 0, 0, h * 0.12);
        }
        BuildPattern(w, h);
        var px = (int)Math.Ceiling(artSize * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1));
        if (px > 0 && px != _decodedFor)
        {
            _decodedFor = px;
            _ = DecodeAsync(px);
        }
    }

    private void BuildPattern(double w, double h)
    {
        // Soft rounded tiles, calm and low contrast, one cell wider than the screen so it can drift a cell.
        _pattern.Children.Clear();
        const double cell = 120;
        var brush = new SolidColorBrush(Color.Parse("#E7E9F2"));
        for (var y = -cell; y < h + cell; y += cell)
            for (var x = -cell; x < w + cell; x += cell)
            {
                var square = new Border { Width = cell * 0.7, Height = cell * 0.7, CornerRadius = new CornerRadius(22), Background = brush };
                Canvas.SetLeft(square, x + cell * 0.15);
                Canvas.SetTop(square, y + cell * 0.15);
                _pattern.Children.Add(square);
            }
    }

    private async Task DecodeAsync(int px)
    {
        var started = Environment.TickCount64;
        var bitmaps = await H0Covers.DecodeAsync(px);
        if (px != _decodedFor)
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
            return;
        }
        var old = _bitmaps;
        _bitmaps = bitmaps;
        foreach (var art in _art)
            art.Source = null;
        if (old is not null)
            foreach (var bitmap in old)
                bitmap.Dispose();
        global::Android.Util.Log.Info("EmuShelfH0",
            $"Stage: decoded {bitmaps.Length} hero images at {px}px in {Environment.TickCount64 - started} ms (off the UI thread)");
        _shown = -1;
        Show(_model.Index);
    }

    private void Show(int index)
    {
        if (_bitmaps is null || index == _shown)
            return;
        _shown = index;
        var outgoing = _art[_front];
        _front ^= 1;
        var incoming = _art[_front];
        incoming.Source = _bitmaps[index];
        incoming.ZIndex = 1;
        outgoing.ZIndex = 0;

        // Old art shrinks to 0.86× and fades out (150 ms); new art grows from 0.72× on the spring,
        // fading in over 170 ms. The plate pops from 0.94× at the same time. Direction never matters.
        H0Composition.AnimateScale(outgoing, 0.86f, 150);
        H0Composition.Animate(outgoing, "Opacity", 0f, 150);
        H0Composition.AnimateScale(incoming, 1f, H0SpringEasing.DurationSeconds * 1000, new H0SpringEasing(), 0.72f);
        H0Composition.Animate(incoming, "Opacity", 1f, 170, new LinearEasing(), 0f);
        _title.Text = H0Covers.Title(index);
        _meta.Text = $"Spike tile {index + 1} of {H0Model.Count}";
        H0Composition.AnimateScale(_plate, 1f, 300, new BackEaseOut(), 0.94f);
    }

    private void StartTicking()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _ticking = true;
        _lastFrame = null;
        top.RequestAnimationFrame(OnFrame);
    }

    // The background drift runs every frame, so the Stage always has a full-screen moving layer:
    // the worst case for the top screen, and a continuous frame-interval sample.
    private void OnFrame(TimeSpan now)
    {
        if (!Drift)
        {
            _ticking = false;
            _stats.Break();
            return;
        }
        _stats.Tick(now);
        var dt = _lastFrame is { } last ? Math.Clamp((now - last).TotalSeconds, 0, 0.05) : 0;
        _lastFrame = now;
        _drift.X = (_drift.X - 14 * dt) % 120;
        _drift.Y = (_drift.Y - 9 * dt) % 120;
        if (TopLevel.GetTopLevel(this) is { } top)
            top.RequestAnimationFrame(OnFrame);
        else
            _ticking = false;
    }
}
#endif
