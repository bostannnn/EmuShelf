using System.Numerics;
using EmuShelf.Rendering.Stage.Backdrops;

namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The shape of the Big Type Stage's veils (M47 B4): the soft shade of the opposite tone that lies behind each text zone. The
/// numbers are the mock's (<c>SHADE</c> and <c>writeShade</c> in docs/prototypes/handheld-big-type/index.html, and tokens.js
/// <c>legibility</c>): on the left a flat band as wide as the name's box plus <see cref="PastName"/>, fading to nothing over
/// <see cref="Fade"/> of the screen's width; in each right corner an elliptical patch, flat to <c>Flat</c> of its radii and
/// fading to their edge. Each veil is a white mask here (alpha only); the Avalonia Stage colours it with the dark ink at the
/// resting strength, and the native Stage colours it and sets its strength for the backdrop that is showing.
/// </summary>
public static class BigTypeVeil
{
    /// <summary>How far past the name's box the left veil stays flat (tokens.js <c>LeftVeilPastName</c>).</summary>
    public const float PastName = 32f;

    /// <summary>How much of the screen's width the left veil takes to fade out (tokens.js <c>LeftVeilFade</c>).</summary>
    public const float Fade = 0.24f;

    /// <summary>The dark ink: the console's colour mixed this far toward <see cref="InkToward"/> (tokens.js <c>InkMix</c>).</summary>
    public const float InkMix = 0.74f;

    /// <summary>What the dark ink is mixed toward (tokens.js <c>InkToward</c>, #0B0A18), 0–255.</summary>
    public static readonly Vector3 InkToward = new(0x0B, 0x0A, 0x18);

    /// <summary>A right corner's patch: its centre and radii as fractions of the canvas, and how far out it stays flat.</summary>
    public readonly record struct Corner(float CentreX, float CentreY, float RadiusX, float RadiusY, float Flat);

    /// <summary>Behind the clock (the mock's <c>tr</c>: 56% by 40% at the top right, flat to 60%).</summary>
    public static Corner TopRight { get; } = new(1f, 0f, 0.56f, 0.40f, 0.6f);

    /// <summary>Behind the hints (the mock's <c>br</c>: 68% by 38% at the bottom right, flat to 62%).</summary>
    public static Corner BottomRight { get; } = new(1f, 1f, 0.68f, 0.38f, 0.62f);

    /// <summary>The dark ink for a console's colour (0–255 sRGB channels in, the same out).</summary>
    public static Vector3 Deep(Vector3 colour) => Vector3.Lerp(colour, InkToward, InkMix);

    /// <summary>The resting strength of a zone's veil (where no shader runs, and the table's least).</summary>
    public static float RestingStrength(StageInkZone zone) =>
        zone == StageInkZone.Left ? BackdropLegibility.BaseVeilLeft : BackdropLegibility.BaseVeilCorner;

    /// <summary>Where the left veil stops being flat: the name box's right edge plus <see cref="PastName"/>, in canvas pixels.</summary>
    public static float LeftFlatEnd(float nameLeft, float nameBoxWidth) => nameLeft + nameBoxWidth + PastName;

    /// <summary>The left veil's coverage at <paramref name="x"/> (canvas pixels).</summary>
    public static float LeftAlpha(float x, float flatEnd, float canvasWidth) =>
        x <= flatEnd ? 1f : Math.Max(0f, 1f - ((x - flatEnd) / (Fade * canvasWidth)));

    /// <summary>A corner patch's coverage at (<paramref name="x"/>, <paramref name="y"/>) on a canvas of the given size.</summary>
    public static float CornerAlpha(Corner corner, float x, float y, float canvasWidth, float canvasHeight)
    {
        var dx = (x - (corner.CentreX * canvasWidth)) / (corner.RadiusX * canvasWidth);
        var dy = (y - (corner.CentreY * canvasHeight)) / (corner.RadiusY * canvasHeight);
        var d = MathF.Sqrt((dx * dx) + (dy * dy));
        return d <= corner.Flat ? 1f : d >= 1f ? 0f : 1f - ((d - corner.Flat) / (1f - corner.Flat));
    }

    /// <summary>The canvas rectangle a zone's veil covers (outside it the veil is nothing).</summary>
    public static (float X, float Y, float Width, float Height) Bounds(StageInkZone zone, float flatEnd, float canvasWidth, float canvasHeight)
    {
        switch (zone)
        {
            case StageInkZone.Left:
                return (0f, 0f, Math.Min(canvasWidth, flatEnd + (Fade * canvasWidth)), canvasHeight);
            case StageInkZone.TopRight:
            case StageInkZone.BottomRight:
            {
                var c = zone == StageInkZone.TopRight ? TopRight : BottomRight;
                var rx = c.RadiusX * canvasWidth;
                var ry = c.RadiusY * canvasHeight;
                var x0 = Math.Max(0f, (c.CentreX * canvasWidth) - rx);
                var y0 = Math.Max(0f, (c.CentreY * canvasHeight) - ry);
                var x1 = Math.Min(canvasWidth, (c.CentreX * canvasWidth) + rx);
                var y1 = Math.Min(canvasHeight, (c.CentreY * canvasHeight) + ry);
                return (x0, y0, x1 - x0, y1 - y0);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(zone));
        }
    }

    /// <summary>
    /// A zone's veil as a white mask, sampled every <paramref name="step"/> canvas pixels (the shape is smooth, so the GPU's
    /// filtering stretches it back without loss): premultiplied RGBA, placed on the canvas. The left veil varies only across, so
    /// it is one row tall.
    /// </summary>
    public static StageImage Mask(StageInkZone zone, float flatEnd, float canvasWidth, float canvasHeight, int step = 4)
    {
        var (x, y, width, height) = Bounds(zone, flatEnd, canvasWidth, canvasHeight);
        var columns = Math.Max(2, (int)MathF.Ceiling(width / step) + 1);
        var rows = zone == StageInkZone.Left ? 1 : Math.Max(2, (int)MathF.Ceiling(height / step) + 1);
        var data = new byte[columns * rows * 4];
        for (var row = 0; row < rows; row++)
        {
            // Sample points sit on the texels' centres, which the stretched quad maps to the bounds' edges and in between.
            var sy = y + (rows == 1 ? 0f : row * height / (rows - 1));
            for (var column = 0; column < columns; column++)
            {
                var sx = x + (column * width / (columns - 1));
                var alpha = zone == StageInkZone.Left
                    ? LeftAlpha(sx, flatEnd, canvasWidth)
                    : CornerAlpha(zone == StageInkZone.TopRight ? TopRight : BottomRight, sx, sy, canvasWidth, canvasHeight);
                var value = (byte)Math.Clamp(MathF.Round(alpha * 255f), 0f, 255f);
                var i = ((row * columns) + column) * 4;
                data[i] = data[i + 1] = data[i + 2] = data[i + 3] = value;
            }
        }
        // The quad spans one texel beyond each sample row and column, half a texel at each edge, so the samples land in place.
        var texelWidth = width / (columns - 1);
        var texelHeight = rows == 1 ? height : height / (rows - 1);
        return rows == 1
            ? new StageImage(new StagePixels(columns, rows, data, IsBgra: false), x - (texelWidth / 2f), y, width + texelWidth, height)
            : new StageImage(new StagePixels(columns, rows, data, IsBgra: false), x - (texelWidth / 2f), y - (texelHeight / 2f),
                width + texelWidth, height + texelHeight);
    }
}
