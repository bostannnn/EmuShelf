using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EmuShelf.Rendering.Stage;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A folder's art for the native screens, as parts that can glide: the shadow and back plate (still), the
/// three peeking covers and the front flap, each pictured untransformed at the size it is shown, with the
/// canvas-space transform each state (closed, peek, open) gives it. The transforms are read from the art's
/// own styles (the `peek` and `open` classes), so the native glide lands exactly where the XAML says.
/// </summary>
internal static class HandheldFolderArtShots
{
    private const float CoverMilliseconds = 360;
    private const float FlapMilliseconds = 300;

    public static FolderArt? Capture(HandheldFolderArt art, Visual canvas)
    {
        if (art.FindControl<Panel>("ArtRoot") is not { } root || !art.IsVisible || root.Bounds.Width <= 0 ||
            root.TranslatePoint(default, canvas) is not { } origin ||
            root.TranslatePoint(new Point(root.Bounds.Width, 0), canvas) is not { } right)
        {
            return null;
        }
        // The art is a 416 px drawing in a Viewbox: one uniform scale and an offset onto the canvas.
        var scale = (right.X - origin.X) / root.Bounds.Width;
        var toCanvas = Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y);
        var hadPeek = art.Classes.Contains("peek");
        var hadOpen = art.Classes.Contains("open");
        var hadFrozen = art.Classes.Contains("frozen");
        var parts = new List<FolderArtPart>();
        try
        {
            // States are read straight from the styles, never from a transition on its way there.
            art.Classes.Set("frozen", true);
            Still(art, "ArtShadow", root, toCanvas, scale, parts);
            Still(art, "ArtBack", root, toCanvas, scale, parts);
            foreach (var name in new[] { "CoverLeft", "CoverRight", "CoverMiddle" })
                Moving(art, name, root, toCanvas, scale, CoverMilliseconds, parts);
            Moving(art, "ArtFront", root, toCanvas, scale, FlapMilliseconds, parts);
        }
        finally
        {
            art.Classes.Set("peek", hadPeek);
            art.Classes.Set("open", hadOpen);
            art.Classes.Set("frozen", hadFrozen);
        }
        return new FolderArt(parts);
    }

    /// <summary>The state a folder's art is in now.</summary>
    public static FolderArtState StateOf(HandheldFolderArt art) =>
        art.Classes.Contains("open") ? FolderArtState.Open : art.Classes.Contains("peek") ? FolderArtState.Peek : FolderArtState.Closed;

    private static void Still(HandheldFolderArt art, string name, Visual root, Matrix toCanvas, double scale, List<FolderArtPart> parts)
    {
        if (art.FindControl<Control>(name) is not { IsVisible: true } part || Picture(part, root, toCanvas, scale) is not { } image)
            return;
        parts.Add(new FolderArtPart(image, StageAffine.Identity, StageAffine.Identity, StageAffine.Identity, 0));
    }

    private static void Moving(HandheldFolderArt art, string name, Visual root, Matrix toCanvas, double scale, float milliseconds,
        List<FolderArtPart> parts)
    {
        if (art.FindControl<Control>(name) is not { IsVisible: true } part || Picture(part, root, toCanvas, scale) is not { } image)
            return;
        var place = Offset(part, root);
        StageAffine At(bool peek, bool open)
        {
            art.Classes.Set("peek", peek);
            art.Classes.Set("open", open);
            var local = part.RenderTransform?.Value ?? Matrix.Identity;
            var pivot = part.RenderTransformOrigin.ToPixels(part.Bounds.Size);
            // The part's own transform about its origin, in the art's 416 space, then onto the canvas.
            var inArt = Matrix.CreateTranslation(-pivot.X, -pivot.Y) * local * Matrix.CreateTranslation(pivot.X, pivot.Y);
            var toArt = Matrix.CreateTranslation(place.X, place.Y);
            toArt.TryInvert(out var fromArt);
            toCanvas.TryInvert(out var fromCanvas);
            var m = fromCanvas * fromArt * inArt * toArt * toCanvas;
            return new StageAffine((float)m.M11, (float)m.M12, (float)m.M21, (float)m.M22, (float)m.M31, (float)m.M32);
        }
        parts.Add(new FolderArtPart(image, At(false, false), At(true, false), At(false, true), milliseconds));
    }

    /// <summary>A part's untransformed picture, drawn at the size it is shown, placed on the canvas.</summary>
    private static StageImage? Picture(Control part, Visual root, Matrix toCanvas, double scale)
    {
        var size = part.Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return null;
        var place = Offset(part, root);
        var topLeft = new Point(place.X, place.Y) * toCanvas;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(size.Width * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(size.Height * scale));
        var transform = part.RenderTransform;
        part.RenderTransform = null;
        try
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96 * scale, 96 * scale));
            bitmap.Render(part);
            return new StageImage(HandheldStageShots.Pixels(bitmap), (float)topLeft.X, (float)topLeft.Y,
                (float)(size.Width * scale), (float)(size.Height * scale));
        }
        finally
        {
            part.ClearValue(Visual.RenderTransformProperty);
            if (transform is not null && part.RenderTransform is null)
                part.RenderTransform = transform;
        }
    }

    /// <summary>Where a part sits in the art's 416 space, by layout alone (its own transform left out).</summary>
    private static Point Offset(Control part, Visual root)
    {
        var at = part.Bounds.Position;
        for (var parent = part.Parent as Visual; parent is not null && !ReferenceEquals(parent, root); parent = (parent as Control)?.Parent as Visual)
            at += parent.Bounds.Position;
        return at;
    }
}
