using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
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
    /// <summary>
    /// A piece of the live screen where layout put it on the canvas, drawn at the canvas's resolution (a piece
    /// outside the canvas's Viewbox, such as a full-window backdrop, is scaled to it); null when hidden or empty.
    /// </summary>
    public static StageImage? Shot(Visual piece, Visual canvas)
    {
        if (!piece.IsVisible || piece.Bounds.Width < 1 || piece.Bounds.Height < 1 ||
            HandheldStageShots.ToCanvas(piece, default(Point), canvas) is not { } at ||
            HandheldStageShots.ToCanvas(piece, new Point(piece.Bounds.Width, piece.Bounds.Height), canvas) is not { } end)
        {
            return null;
        }
        var scale = (end.X - at.X) / piece.Bounds.Width;
        if (Math.Abs(scale - 1) < 0.001)
            return Picture(piece, (float)at.X, (float)at.Y, (float)piece.Bounds.Width, (float)piece.Bounds.Height);
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(end.X - at.X));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(end.Y - at.Y));
        using var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96 * scale, 96 * scale));
        bitmap.Render(piece);
        return new StageImage(Pixels(bitmap), (float)at.X, (float)at.Y, (float)(end.X - at.X), (float)(end.Y - at.Y));
    }

    /// <summary>
    /// Maps a point in <paramref name="visual"/> onto <paramref name="canvas"/> through the window. A direct
    /// TranslatePoint between two Viewboxes (the Launcher's canvas and its Title page) drops one Viewbox's scale,
    /// the same trap the Avalonia selector works around.
    /// </summary>
    public static Point? ToCanvas(Visual visual, Point point, Visual canvas)
    {
        if (TopLevel.GetTopLevel(canvas) is not Visual root)
            return visual.TranslatePoint(point, canvas);
        if (visual.TransformToVisual(root) is not { } toRoot || canvas.TransformToVisual(root) is not { } canvasToRoot ||
            !canvasToRoot.TryInvert(out var rootToCanvas))
        {
            return null;
        }
        return point * toRoot * rootToCanvas;
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
