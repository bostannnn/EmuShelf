using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace EmuShelf.App.Controls;

/// <summary>
/// The couch shelf's rotatable 3D hero: renders the focused game's physical medium (a SNES cartridge
/// or a PS2 keep case) as a Skia software-3D box with the cover art on the front face, spun by
/// <see cref="Yaw"/>/<see cref="Pitch"/>. Only the focused shelf item is ever a live one of these.
///
/// It draws nothing (leaving the flat cover behind it to show through) when it has no supported
/// <see cref="Media"/>, no <see cref="Cover"/>, or the backend has no Skia lease — that "flat cover
/// fallback" is the guardrail from docs/couch-physical-media-shelf.md. All 3D math lives in the pure
/// <see cref="Media3DProjection"/> helper; this type only rasterizes.
/// </summary>
public sealed class Media3DControl : Control
{
    public static readonly StyledProperty<double> YawProperty =
        AvaloniaProperty.Register<Media3DControl, double>(nameof(Yaw));

    public static readonly StyledProperty<double> PitchProperty =
        AvaloniaProperty.Register<Media3DControl, double>(nameof(Pitch));

    public static readonly StyledProperty<MediaType?> MediaProperty =
        AvaloniaProperty.Register<Media3DControl, MediaType?>(nameof(Media));

    public static readonly StyledProperty<Bitmap?> CoverProperty =
        AvaloniaProperty.Register<Media3DControl, Bitmap?>(nameof(Cover));

    public static readonly StyledProperty<Color> AccentProperty =
        AvaloniaProperty.Register<Media3DControl, Color>(nameof(Accent), Color.FromRgb(0x3a, 0x3f, 0x4b));

    static Media3DControl()
    {
        AffectsRender<Media3DControl>(YawProperty, PitchProperty, MediaProperty, CoverProperty, AccentProperty);
    }

    /// <summary>Turntable rotation in radians (right stick / keyboard drive it in later phases).</summary>
    public double Yaw
    {
        get => GetValue(YawProperty);
        set => SetValue(YawProperty, value);
    }

    /// <summary>Tilt in radians.</summary>
    public double Pitch
    {
        get => GetValue(PitchProperty);
        set => SetValue(PitchProperty, value);
    }

    /// <summary>The medium to model, or null to fall back to the flat cover.</summary>
    public MediaType? Media
    {
        get => GetValue(MediaProperty);
        set => SetValue(MediaProperty, value);
    }

    /// <summary>Cover art textured on the front face. Converted to an <see cref="SKImage"/> once and cached.</summary>
    public Bitmap? Cover
    {
        get => GetValue(CoverProperty);
        set => SetValue(CoverProperty, value);
    }

    /// <summary>Per-system tint for the non-front faces (the box body/spine).</summary>
    public Color Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private SKImage? _coverImage;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CoverProperty)
            RebuildCoverImage();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _coverImage?.Dispose();
        _coverImage = null;
    }

    private void RebuildCoverImage()
    {
        _coverImage?.Dispose();
        _coverImage = Cover is { } bitmap ? ToSKImage(bitmap) : null;
    }

    public override void Render(DrawingContext context)
    {
        if (Media is not { } media || _coverImage is null)
            return; // fallback: draw nothing so the flat cover behind shows through

        context.Custom(new Media3DDraw(new Rect(Bounds.Size), media, _coverImage, ToSK(Accent), Yaw, Pitch));
    }

    // Avalonia Bitmap -> SKImage by copying pixels (no re-encode). Cover art is opaque, so the
    // premultiplied/straight alpha distinction does not matter here.
    private static SKImage ToSKImage(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = new SKBitmap(info);
        bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), surface.GetPixels(), info.BytesSize, info.RowBytes);
        return SKImage.FromPixelCopy(surface.Info, surface.GetPixels(), surface.RowBytes);
    }

    private static SKColor ToSK(Color c) => new(c.R, c.G, c.B, c.A);
}

/// <summary>The custom draw operation: leases the live Skia canvas and rasterizes the projected box.</summary>
internal sealed class Media3DDraw : ICustomDrawOperation
{
    private const int GridSubdivisions = 10; // texture-grid resolution for perspective-correct mapping

    private readonly MediaType _media;
    private readonly SKImage _cover;
    private readonly SKColor _accent;
    private readonly double _yaw;
    private readonly double _pitch;

    public Media3DDraw(Rect bounds, MediaType media, SKImage cover, SKColor accent, double yaw, double pitch)
    {
        Bounds = bounds;
        _media = media;
        _cover = cover;
        _accent = accent;
        _yaw = yaw;
        _pitch = pitch;
    }

    public Rect Bounds { get; }

    public bool HitTest(Point p) => false;

    // Pose changes every frame while rotating; never treat two ops as equal (always redraw).
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
        if (lease is null)
            return; // non-Skia backend: flat cover behind shows through

        using (lease)
        {
            var canvas = lease.SkCanvas;
            var width = Bounds.Width;
            var height = Bounds.Height;
            var camera = Media3DProjection.BuildCamera(width, height);
            var faces = Media3DProjection.Project(_media, _yaw, _pitch, width, height);
            if (faces.Count == 0)
                return;

            DrawGroundShadow(canvas, faces);

            foreach (var face in faces)
            {
                if (face.Face == MediaFace.Front)
                    DrawTexturedFront(canvas, camera, face);
                else
                    DrawSolidFace(canvas, face);
            }
        }
    }

    private void DrawGroundShadow(SKCanvas canvas, IReadOnlyList<ProjectedFace> faces)
    {
        double minX = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var face in faces)
            foreach (var c in face.Screen)
            {
                minX = Math.Min(minX, c.X);
                maxX = Math.Max(maxX, c.X);
                maxY = Math.Max(maxY, c.Y);
            }

        var cx = (float)((minX + maxX) / 2);
        var rx = (float)((maxX - minX) * 0.46);
        var ry = (float)((maxX - minX) * 0.06 + 6);
        var cy = (float)(maxY + ry * 0.4);

        using var paint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 120),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(ry * 0.9)),
        };
        canvas.DrawOval(new SKRect(cx - rx, cy - ry, cx + rx, cy + ry), paint);
    }

    private void DrawTexturedFront(SKCanvas canvas, MediaCamera camera, ProjectedFace face)
    {
        var img = _cover;
        var shade = Shade(face.Shade);
        const int n = GridSubdivisions;

        // Subdivide the quad in rotated model space and project each vertex, so the texture stays
        // perspective-correct (plain affine mapping of the 4 corners would skew a turned face).
        var grid = new SKPoint[n + 1, n + 1];
        var texs = new SKPoint[n + 1, n + 1];
        for (var i = 0; i <= n; i++)
        for (var j = 0; j <= n; j++)
        {
            var u = i / (double)n;
            var v = j / (double)n;
            var world = Bilerp(face.World, u, v);
            var p = camera.Project(world);
            grid[i, j] = new SKPoint((float)p.X, (float)p.Y);
            texs[i, j] = new SKPoint((float)(u * img.Width), (float)(v * img.Height));
        }

        var verts = new SKPoint[n * n * 6];
        var uvs = new SKPoint[n * n * 6];
        var colors = new SKColor[n * n * 6];
        var k = 0;
        for (var i = 0; i < n; i++)
        for (var j = 0; j < n; j++)
        {
            Emit(i, j); Emit(i + 1, j); Emit(i + 1, j + 1);
            Emit(i, j); Emit(i + 1, j + 1); Emit(i, j + 1);

            void Emit(int a, int b)
            {
                verts[k] = grid[a, b];
                uvs[k] = texs[a, b];
                colors[k] = shade;
                k++;
            }
        }

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Shader = SKShader.CreateImage(img, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp),
        };
        // The per-vertex shade colours modulate (multiply) the sampled cover texture by default.
        canvas.DrawVertices(SKVertexMode.Triangles, verts, uvs, colors, paint);
    }

    private void DrawSolidFace(SKCanvas canvas, ProjectedFace face)
    {
        using var path = new SKPath();
        path.MoveTo((float)face.Screen[0].X, (float)face.Screen[0].Y);
        path.LineTo((float)face.Screen[1].X, (float)face.Screen[1].Y);
        path.LineTo((float)face.Screen[2].X, (float)face.Screen[2].Y);
        path.LineTo((float)face.Screen[3].X, (float)face.Screen[3].Y);
        path.Close();

        var shade = face.Shade;
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(
                (byte)(_accent.Red * shade),
                (byte)(_accent.Green * shade),
                (byte)(_accent.Blue * shade),
                255),
        };
        canvas.DrawPath(path, paint);
    }

    private static SKColor Shade(double shade)
    {
        var g = (byte)Math.Clamp(shade * 255, 0, 255);
        return new SKColor(g, g, g, 255);
    }

    private static Vec3 Bilerp(Vec3[] q, double u, double v)
    {
        // q order: TL(0), TR(1), BR(2), BL(3). Top edge TL->TR, bottom edge BL->BR.
        var top = Lerp(q[0], q[1], u);
        var bottom = Lerp(q[3], q[2], u);
        return Lerp(top, bottom, v);
    }

    private static Vec3 Lerp(Vec3 a, Vec3 b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}
