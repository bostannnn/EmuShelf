using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The Big Type Stage's three veils as the Avalonia Stage draws them (M47 B4): the shapes of <see cref="BigTypeVeil"/> in the
/// dark ink at their resting strength, which is what the rule gives white ink where no backdrop shader runs. The native Stage
/// draws the same masks itself, coloured for the backdrop that is showing.
/// </summary>
public sealed class HandheldBigTypeVeils : Control
{
    public static readonly StyledProperty<Color> DeepProperty =
        AvaloniaProperty.Register<HandheldBigTypeVeils, Color>(nameof(Deep), Colors.Black);

    /// <summary>The name's box width: the left veil stays flat to its right edge plus <see cref="BigTypeVeil.PastName"/>.</summary>
    public static readonly StyledProperty<double> NameBoxWidthProperty =
        AvaloniaProperty.Register<HandheldBigTypeVeils, double>(nameof(NameBoxWidth), 1360);

    public static readonly StyledProperty<double> NameLeftProperty =
        AvaloniaProperty.Register<HandheldBigTypeVeils, double>(nameof(NameLeft), 80);

    static HandheldBigTypeVeils()
    {
        AffectsRender<HandheldBigTypeVeils>(DeepProperty, NameBoxWidthProperty, NameLeftProperty);
    }

    public HandheldBigTypeVeils()
    {
        IsHitTestVisible = false;
        // The masks are sampled every few pixels and stretched: smooth filtering puts them back as the gradient they are.
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.LowQuality);
    }

    public Color Deep
    {
        get => GetValue(DeepProperty);
        set => SetValue(DeepProperty, value);
    }

    public double NameBoxWidth
    {
        get => GetValue(NameBoxWidthProperty);
        set => SetValue(NameBoxWidthProperty, value);
    }

    public double NameLeft
    {
        get => GetValue(NameLeftProperty);
        set => SetValue(NameLeftProperty, value);
    }

    private static readonly StageInkZone[] Zones = [StageInkZone.Left, StageInkZone.TopRight, StageInkZone.BottomRight];
    private readonly (string Key, WriteableBitmap Bitmap, Rect Rect)?[] _cache = new (string, WriteableBitmap, Rect)?[3];

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
            return;
        var flatEnd = BigTypeVeil.LeftFlatEnd((float)NameLeft, (float)NameBoxWidth);
        // The left veil lies under the corner ones, as the mock's layers do.
        for (var i = 0; i < Zones.Length; i++)
        {
            var zone = Zones[i];
            var key = FormattableString.Invariant($"{Deep}|{(zone == StageInkZone.Left ? flatEnd : 0)}|{size.Width}|{size.Height}");
            if (_cache[i] is not { } cached || cached.Key != key)
            {
                _cache[i]?.Bitmap.Dispose();
                var mask = BigTypeVeil.Mask(zone, flatEnd, (float)size.Width, (float)size.Height);
                cached = (key, Colour(mask.Pixels, Deep, BigTypeVeil.RestingStrength(zone)), new Rect(mask.X, mask.Y, mask.Width, mask.Height));
                _cache[i] = cached;
            }
            context.DrawImage(cached.Bitmap, new Rect(cached.Bitmap.Size), cached.Rect);
        }
    }

    /// <summary>A white mask coloured: premultiplied <paramref name="colour"/> at <paramref name="strength"/> of its coverage.</summary>
    internal static WriteableBitmap Colour(StagePixels mask, Color colour, float strength)
    {
        var bitmap = new WriteableBitmap(new PixelSize(mask.Width, mask.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Premul);
        var data = new byte[mask.Data.Length];
        for (var i = 0; i < data.Length; i += 4)
        {
            var a = mask.Data[i + 3] / 255f * strength;
            data[i] = (byte)MathF.Round(colour.R * a);
            data[i + 1] = (byte)MathF.Round(colour.G * a);
            data[i + 2] = (byte)MathF.Round(colour.B * a);
            data[i + 3] = (byte)MathF.Round(255f * a);
        }
        using var buffer = bitmap.Lock();
        for (var row = 0; row < mask.Height; row++)
            Marshal.Copy(data, row * mask.Width * 4, buffer.Address + (row * buffer.RowBytes), mask.Width * 4);
        return bitmap;
    }
}
