using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using EmuShelf.Rendering.Stage;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Pictures of the Avalonia Stage's pieces for the native Stage: each piece is drawn once per change, at the
/// canvas's own resolution, and handed over as premultiplied pixels in the order Skia keeps them.
/// </summary>
internal static class HandheldStageShots
{
    /// <summary>A piece of the live Stage where layout put it on the canvas; null when it is hidden or empty.</summary>
    public static StageImage? Shot(Visual piece, Visual canvas)
    {
        if (!piece.IsVisible || piece.Bounds.Width < 1 || piece.Bounds.Height < 1 ||
            piece.TranslatePoint(default, canvas) is not { } at)
        {
            return null;
        }
        return Picture(piece, (float)at.X, (float)at.Y, (float)piece.Bounds.Width, (float)piece.Bounds.Height);
    }

    /// <summary>Draws <paramref name="visual"/> into a picture placed at (x, y) on the canvas.</summary>
    public static StageImage Picture(Visual visual, float x, float y, float width, float height)
    {
        LayOutDetached(visual);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height));
        using var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight));
        bitmap.Render(visual);
        return new StageImage(Pixels(bitmap), x, y, pixelWidth, pixelHeight);
    }

    /// <summary>A bitmap's premultiplied pixels, top row first, in its own channel order.</summary>
    public static StagePixels Pixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var data = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), data.Length, stride);
        }
        finally
        {
            handle.Free();
        }
        return new StagePixels(size.Width, size.Height, data, IsBgra: bitmap.Format != PixelFormat.Rgba8888);
    }

    // A piece built only to be pictured (the backdrop's layers) has no layout manager: lay its root out at
    // its own size first.
    private static void LayOutDetached(Visual visual)
    {
        if (visual.IsAttachedToVisualTree())
            return;
        Visual top = visual;
        while (top.GetVisualParent() is { } parent)
            top = parent;
        if (top is Layoutable root && !double.IsNaN(root.Width) && !double.IsNaN(root.Height))
        {
            var size = new Size(root.Width, root.Height);
            root.Measure(size);
            root.Arrange(new Rect(size));
        }
    }
}
