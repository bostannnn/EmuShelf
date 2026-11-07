using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A soft shadow (or glow) drawn from a bitmap rendered once per size and colour, never blurred per frame:
/// the selector glides over tiles every frame, and a live BoxShadow under it would re-blur each time
/// (HANDHELD-MODE.md Motion spec; the Android reduced-effects lesson). The shape is this control's bounds
/// deflated by <see cref="Blur"/>, so place it with a negative margin of the blur around what casts it.
/// </summary>
public sealed class HandheldShadow : Control
{
    public static readonly StyledProperty<double> RadiusProperty =
        AvaloniaProperty.Register<HandheldShadow, double>(nameof(Radius));

    public static readonly StyledProperty<double> BlurProperty =
        AvaloniaProperty.Register<HandheldShadow, double>(nameof(Blur), 20);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<HandheldShadow, Color>(nameof(Color), Color.FromArgb(0x24, 0, 0, 0));

    // Shared across every shadow of the same look: a home page of tiles is one bitmap, not twenty.
    private static readonly Dictionary<(int W, int H, int R, int B, uint C, int S), RenderTargetBitmap> Cache = [];
    private const int MaxCached = 48;

    private RenderTargetBitmap? _bitmap;

    static HandheldShadow()
    {
        AffectsRender<HandheldShadow>(RadiusProperty, BlurProperty, ColorProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<HandheldShadow>(false);
    }

    public double Radius { get => GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double Blur { get => GetValue(BlurProperty); set => SetValue(BlurProperty, value); }
    public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == RadiusProperty ||
            change.Property == BlurProperty || change.Property == ColorProperty)
        {
            _bitmap = null;
        }
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;
        _bitmap ??= Lookup(size);
        if (_bitmap is { } bitmap)
            // The source rectangle is in pixels.
            context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), new Rect(size));
    }

    private RenderTargetBitmap? Lookup(Size size)
    {
        var blur = Blur;
        var scaling = Math.Min(2, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        var key = ((int)Math.Round(size.Width), (int)Math.Round(size.Height), (int)Math.Round(Radius), (int)Math.Round(blur),
            Color.ToUInt32(), (int)Math.Round(scaling * 100));
        if (Cache.TryGetValue(key, out var cached))
            return cached;
        var shapeWidth = size.Width - (2 * blur);
        var shapeHeight = size.Height - (2 * blur);
        if (shapeWidth <= 0 || shapeHeight <= 0)
            return null;
        // Half resolution is plenty for something this soft, and keeps a page of shadows small.
        var pixelScale = scaling * 0.5;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width * pixelScale)), Math.Max(1, (int)Math.Ceiling(size.Height * pixelScale)));
        var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * pixelScale, 96 * pixelScale));
        var shape = new Border
        {
            Width = shapeWidth,
            Height = shapeHeight,
            CornerRadius = new CornerRadius(Radius),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = Color }),
        };
        var host = new Border { Width = size.Width, Height = size.Height, Padding = new Thickness(blur), Child = shape };
        host.Measure(size);
        host.Arrange(new Rect(size));
        bitmap.Render(host);
        if (Cache.Count >= MaxCached)
        {
            // Shadows are tiny; dropping the whole cache once in a while is simpler than an LRU. Bitmaps still
            // drawn by a control stay alive in that control's field until it next re-renders.
            Cache.Clear();
        }
        Cache[key] = bitmap;
        return bitmap;
    }
}
