using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The Stage's era motif (HANDHELD-MODE.md, Top screen; picked in the folder lab): behind a cartridge
/// system, a light field in its hue with a soft 22 px pixel grid and large blocks drifting diagonally; behind
/// a disc system, a radial field with 2 px scanlines, a slow rolling light bar and a faint vignette. Drawn on
/// a 1920×1080 canvas that fills the window (cropped, never letterboxed). The grid, blocks and scanlines are
/// tiny bitmaps built once per colour and tiled. The only motion is the blocks or the bar, stepped a few times a
/// second in whole pixels (a continuous animation redrew the whole Stage at the panel's 120 Hz for a drift of
/// 0.05 px a frame), and it stops while the Stage is hidden or the app is in the background. A change of
/// colour or era crossfades (450 ms) once navigation pauses (250 ms), with at most one layer leaving at a time.
/// </summary>
public sealed class HandheldEraBackdrop : Decorator
{
    private const double CanvasWidth = 1920;
    private const double CanvasHeight = 1080;
    private const int MaxLayers = 2;

    public static readonly StyledProperty<HandheldBackdrop?> BackdropProperty =
        AvaloniaProperty.Register<HandheldEraBackdrop, HandheldBackdrop?>(nameof(Backdrop));

    private readonly Grid _canvas = new() { Width = CanvasWidth, Height = CanvasHeight, ClipToBounds = true };
    private readonly List<Layer> _layers = [];
    private readonly DispatcherTimer _drift = new(DispatcherPriority.Background);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private IActivatableLifetime? _lifetime;
    private bool _backgrounded;
    private readonly DispatcherTimer _settle = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(HandheldMotion.BackdropSettleMilliseconds),
    };
    private HandheldBackdrop? _pending;
    private HandheldBackdrop? _shown;
    private bool _hasShown;

    static HandheldEraBackdrop()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<HandheldEraBackdrop>(false);
        ClipToBoundsProperty.OverrideDefaultValue<HandheldEraBackdrop>(true);
    }

    public HandheldEraBackdrop()
    {
        Child = new Viewbox { Stretch = Stretch.UniformToFill, Child = _canvas };
        _drift.Tick += (_, _) => Step();
        _settle.Tick += (_, _) => Settle();
        AttachedToVisualTree += (_, _) =>
        {
            _lifetime = Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) as IActivatableLifetime;
            if (_lifetime is not null)
            {
                _lifetime.Activated += OnActivated;
                _lifetime.Deactivated += OnDeactivated;
            }
            UpdateDrift();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_lifetime is not null)
            {
                _lifetime.Activated -= OnActivated;
                _lifetime.Deactivated -= OnDeactivated;
                _lifetime = null;
            }
            UpdateDrift();
        };
        EffectiveViewportChanged += (_, _) => UpdateDrift();
    }

    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Background)
        {
            _backgrounded = false;
            UpdateDrift();
        }
    }

    private void OnDeactivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Background)
        {
            _backgrounded = true;
            UpdateDrift();
        }
    }

    public HandheldBackdrop? Backdrop
    {
        get => GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BackdropProperty)
            Request(change.GetNewValue<HandheldBackdrop?>());
        else if (change.Property == IsVisibleProperty)
            UpdateDrift();
    }

    /// <summary>
    /// The first backdrop shows at once; after that a change waits until navigation pauses, so scrolling across
    /// home (every folder a different system) crossfades the full screen once, when it stops, not on every press.
    /// </summary>
    private void Request(HandheldBackdrop? backdrop)
    {
        _pending = backdrop;
        _settle.Stop();
        if (!_hasShown)
        {
            Show(backdrop);
            return;
        }
        _settle.Start();
    }

    private void Settle()
    {
        _settle.Stop();
        if (!Equals(_pending, _shown))
            Show(_pending);
    }

    private void Show(HandheldBackdrop? backdrop)
    {
        _hasShown = true;
        _shown = backdrop;
        // The leaving layers fade out (the plain gradient behind shows through) or under the arriving one.
        var fade = HandheldMotion.BackdropCrossfadeMilliseconds;
        foreach (var leaving in _layers.ToArray())
        {
            // A layer already on its way out goes at once: only one crossfade runs at a time.
            if (leaving.IsLeaving)
            {
                Remove(leaving);
                continue;
            }
            leaving.IsLeaving = true;
            if (backdrop is null)
            {
                leaving.Root.Opacity = 0;
                HandheldComposition.Opacity(leaving.Root, 1, 0, fade, new CubicEaseInOut());
            }
            DispatcherTimer.RunOnce(() => Remove(leaving), TimeSpan.FromMilliseconds(fade + 60));
        }
        if (backdrop is null)
            return;

        var layer = Build(backdrop);
        _layers.Add(layer);
        _canvas.Children.Add(layer.Root);
        // Rapid changes stack at most a few layers; the oldest goes at once.
        while (_layers.Count > MaxLayers)
            Remove(_layers[0]);
        HandheldComposition.Opacity(layer.Root, 0, 1, fade, new CubicEaseOut());
        UpdateDrift();
        Step();
    }

    private void Remove(Layer layer)
    {
        if (_layers.Remove(layer))
            _canvas.Children.Remove(layer.Root);
    }

    /// <summary>The arriving (or resting) layer, the only one that moves; leaving layers stay where they are.</summary>
    private Layer? Moving
    {
        get
        {
            for (var i = _layers.Count - 1; i >= 0; i--)
            {
                if (!_layers[i].IsLeaving)
                    return _layers[i];
            }
            return null;
        }
    }

    private void UpdateDrift()
    {
        var run = Moving is not null && this.IsAttachedToVisualTree() && IsEffectivelyVisible && !_backgrounded;
        if (!run)
        {
            _drift.Stop();
            return;
        }
        var interval = TimeSpan.FromMilliseconds(Moving!.StepMilliseconds);
        if (_drift.Interval != interval)
            _drift.Interval = interval;
        if (!_drift.IsEnabled)
            _drift.Start();
    }

    /// <summary>Puts the moving layer where the clock says, in whole canvas pixels (one per panel pixel on the Thor).</summary>
    private void Step()
    {
        if (Moving is not { } layer)
        {
            _drift.Stop();
            return;
        }
        var progress = (_clock.Elapsed.TotalMilliseconds / layer.LoopMilliseconds) % 1;
        var x = Math.Round(layer.Travel.X * progress);
        var y = Math.Round(layer.Travel.Y * progress);
        if (layer.Shift.X != x)
            layer.Shift.X = x;
        if (layer.Shift.Y != y)
            layer.Shift.Y = y;
    }

    // ---- The two eras ----

    private static Layer Build(HandheldBackdrop backdrop)
    {
        var hue = backdrop.Color.ToHsl().H;
        var tiles = TileSet.For(backdrop);
        var root = new Panel { Width = CanvasWidth, Height = CanvasHeight };
        if (backdrop.Era == SystemEra.Cartridge)
        {
            root.Children.Add(Fill(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Hsl(hue, 0.40, 0.92), 0), new GradientStop(Hsl(hue, 0.38, 0.86), 1) },
            }));
            root.Children.Add(Fill(Tiled(tiles.Grid, HandheldMotion.CartridgeGridPitch, HandheldMotion.CartridgeGridPitch)));
            var tileWidth = HandheldMotion.CartridgeBlockTileWidth;
            var tileHeight = HandheldMotion.CartridgeBlockTileHeight;
            // One tile bigger than the canvas on every side, so a drift of one tile never shows an edge.
            var blocks = new Border
            {
                Margin = new Thickness(-tileWidth),
                Background = Tiled(tiles.Blocks, tileWidth, tileHeight),
            };
            root.Children.Add(blocks);
            // A lighter pool in the middle, so the artwork sits on calm ground.
            root.Children.Add(Fill(Radial(0.5, 0.46, 0.55, 0.70, (Hsl(hue, 0.35, 0.97, 0.8), 0), (Hsl(hue, 0.35, 0.97, 0), 0.75))));
            return new Layer(root, blocks, new Vector(tileWidth, tileHeight), HandheldMotion.CartridgeDriftSeconds * 1000,
                HandheldMotion.CartridgeDriftStepMilliseconds);
        }

        root.Children.Add(Fill(Radial(0.5, 0.40, 0.90, 1.20, (Hsl(hue, 0.35, 0.90), 0), (Hsl(hue, 0.30, 0.78), 1))));
        root.Children.Add(Fill(Tiled(tiles.Scanlines, HandheldMotion.DiscScanlinePitch, HandheldMotion.DiscScanlinePitch)));
        var barHeight = HandheldMotion.DiscBarHeight;
        var bar = new Border
        {
            Height = barHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -barHeight, 0, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Hsl(hue, 0.60, 0.96, 0), 0),
                    new GradientStop(Hsl(hue, 0.60, 0.96, 0.35), 0.5),
                    new GradientStop(Hsl(hue, 0.60, 0.96, 0), 1),
                },
            },
        };
        root.Children.Add(bar);
        root.Children.Add(Fill(Radial(0.5, 0.5, 0.80, 1.0, (Hsl(hue, 0.40, 0.40, 0), 0.55), (Hsl(hue, 0.40, 0.40, 0.18), 1))));
        return new Layer(root, bar, new Vector(0, CanvasHeight + barHeight), HandheldMotion.DiscBarSeconds * 1000,
            HandheldMotion.DiscBarStepMilliseconds);
    }

    private static Border Fill(IBrush brush) => new() { Background = brush };

    private static ImageBrush Tiled(Bitmap tile, double width, double height) => new(tile)
    {
        TileMode = TileMode.Tile,
        Stretch = Stretch.Fill,
        DestinationRect = new RelativeRect(0, 0, width, height, RelativeUnit.Absolute),
    };

    private static RadialGradientBrush Radial(double x, double y, double radiusX, double radiusY, params (Color Color, double Offset)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(x, y, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(radiusX, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(radiusY, RelativeUnit.Relative),
        };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        return brush;
    }

    /// <summary>A colour in the backdrop's hue. Transparent stops keep the hue, so no grey creeps into a fade.</summary>
    internal static Color Hsl(double hue, double saturation, double lightness, double alpha = 1) =>
        new HslColor(alpha, ((hue % 360) + 360) % 360, saturation, lightness).ToRgb();

    private sealed class Layer
    {
        public Layer(Panel root, Control mover, Vector travel, double loopMilliseconds, double stepMilliseconds)
        {
            Root = root;
            Travel = travel;
            LoopMilliseconds = loopMilliseconds;
            StepMilliseconds = stepMilliseconds;
            mover.RenderTransform = Shift;
        }

        public Panel Root { get; }
        public TranslateTransform Shift { get; } = new();
        public Vector Travel { get; }
        public double LoopMilliseconds { get; }
        public double StepMilliseconds { get; }
        public bool IsLeaving { get; set; }
    }

    /// <summary>
    /// The tiny tiles an era repeats, built once per colour as premultiplied pixels: the cartridge grid (22 px,
    /// 2 px lines at 10%), the cartridge block tile (8 blocks of 22 px on 176×132, 22%, hue ±20°) and the disc
    /// scanlines (2 px at 9% on a 5 px pitch: 2 px, because 1 px lines alias on the Thor's 1080p panel).
    /// </summary>
    internal sealed class TileSet
    {
        private static readonly Dictionary<HandheldBackdrop, TileSet> Cache = [];
        private const int MaxCached = 24;

        // The lab's block layout: (x, y) in 22 px cells' pixels on the 176×132 tile; the hue steps -20, 0, +20.
        internal static readonly (int X, int Y)[] BlockCells =
            [(0, 0), (22, 44), (66, 22), (44, 88), (110, 66), (88, 0), (132, 110), (154, 44)];

        public Bitmap Grid { get; private init; } = null!;
        public Bitmap Blocks { get; private init; } = null!;
        public Bitmap Scanlines { get; private init; } = null!;

        public static TileSet For(HandheldBackdrop backdrop)
        {
            if (Cache.TryGetValue(backdrop, out var cached))
                return cached;
            if (Cache.Count >= MaxCached)
                Cache.Clear(); // a few tiny bitmaps; layers still drawing one keep it alive
            var hue = backdrop.Color.ToHsl().H;
            var set = new TileSet
            {
                Grid = Build(GridPixels(Hsl(hue, 0.50, 0.60, 0.10)), Pitch, Pitch),
                Blocks = Build(BlockPixels(hue), BlockWidth, BlockHeight),
                Scanlines = Build(ScanlinePixels(Hsl(hue, 0.40, 0.30, 0.09)), Scan, Scan),
            };
            Cache[backdrop] = set;
            return set;
        }

        private const int Pitch = (int)HandheldMotion.CartridgeGridPitch;
        private const int Line = (int)HandheldMotion.CartridgeGridLine;
        private const int Block = (int)HandheldMotion.CartridgeBlockSize;
        private const int BlockWidth = (int)HandheldMotion.CartridgeBlockTileWidth;
        private const int BlockHeight = (int)HandheldMotion.CartridgeBlockTileHeight;
        private const int Scan = (int)HandheldMotion.DiscScanlinePitch;
        private const int ScanLine = (int)HandheldMotion.DiscScanlineHeight;

        /// <summary>A vertical line on the tile's left and a horizontal one on its bottom; they overlap at the corner.</summary>
        internal static Color[] GridPixels(Color line)
        {
            var pixels = new Color[Pitch * Pitch];
            for (var y = 0; y < Pitch; y++)
            {
                for (var x = 0; x < Pitch; x++)
                {
                    var vertical = x < Line;
                    var horizontal = y >= Pitch - Line;
                    pixels[(y * Pitch) + x] = vertical && horizontal ? Over(line, line)
                        : vertical || horizontal ? line : default;
                }
            }
            return pixels;
        }

        internal static Color[] BlockPixels(double hue)
        {
            var pixels = new Color[BlockWidth * BlockHeight];
            for (var i = 0; i < BlockCells.Length; i++)
            {
                var color = Hsl(hue + ((i % 3) * 20) - 20, 0.55, 0.62, 0.22);
                var (left, top) = BlockCells[i];
                for (var y = top; y < Math.Min(top + Block, BlockHeight); y++)
                    for (var x = left; x < Math.Min(left + Block, BlockWidth); x++)
                        pixels[(y * BlockWidth) + x] = color;
            }
            return pixels;
        }

        /// <summary>A 2 px line at the bottom of every 5 px row (the tile is 5 wide so the brush has a real width).</summary>
        internal static Color[] ScanlinePixels(Color line)
        {
            var pixels = new Color[Scan * Scan];
            for (var y = Scan - ScanLine; y < Scan; y++)
                for (var x = 0; x < Scan; x++)
                    pixels[(y * Scan) + x] = line;
            return pixels;
        }

        // Source-over of two straight-alpha colours of the same hue (the grid's crossing).
        private static Color Over(Color top, Color under)
        {
            var a = (top.A / 255d) + ((under.A / 255d) * (1 - (top.A / 255d)));
            return Color.FromArgb((byte)Math.Round(a * 255), top.R, top.G, top.B);
        }

        private static WriteableBitmap Build(Color[] pixels, int width, int height)
        {
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = bitmap.Lock();
            var row = new int[width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = pixels[(y * width) + x];
                    var alpha = c.A / 255d;
                    var b = (int)Math.Round(c.B * alpha);
                    var g = (int)Math.Round(c.G * alpha);
                    var r = (int)Math.Round(c.R * alpha);
                    row[x] = b | (g << 8) | (r << 16) | (c.A << 24);
                }
                Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), width);
            }
            return bitmap;
        }
    }
}
