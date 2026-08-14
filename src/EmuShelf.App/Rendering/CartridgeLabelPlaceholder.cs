using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace EmuShelf.App.Rendering;

/// <summary>
/// The blank label a cartridge wears when no artwork has been scraped for it.
/// </summary>
/// <remarks>
/// Previously this was a flat accent-coloured rectangle, which reads as an unfinished render rather
/// than as an unlabelled cartridge. This paints the same vocabulary the 2D grid already uses for a
/// missing cover — platform medallion, "ARTWORK MISSING", system name — onto a label-shaped canvas,
/// so the two placeholders are recognisably the same thing in two places.
///
/// It is drawn at the shell's own label proportions. The first version was fixed at the SNES panel's
/// 2.93:1, which <see cref="Shells.ArtFit.Cover"/> then cropped to fit every other shell — a
/// portrait NES label showed "TWORK MI" and half a medallion. Shells differ far too much for one
/// canvas: SNES is nearly 3:1, NES is portrait, a DS card is square.
///
/// Drawn once per system and cached: it depends only on the platform, never on the game. Creation
/// touches Avalonia's rendering stack and must therefore happen on the UI thread, so the shelf
/// warms the cache when its item list changes and the GL frame only ever reads it.
/// </remarks>
public static class CartridgeLabelPlaceholder
{
    /// <summary>Shortest edge of the drawn label, in pixels; the other follows the aspect.</summary>
    private const int ShortEdge = 260;

    /// <summary>Above this, the medallion sits beside the text; below it, above the text.</summary>
    /// <remarks>
    /// A wide cartridge label has room for a badge and two lines side by side. A square or portrait
    /// one does not, and forcing that layout is what leaves no width for the words.
    /// </remarks>
    private const float SideBySideAspect = 1.9f;

    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    /// <summary>Returns the cached label, or null when it has not been warmed yet.</summary>
    /// <remarks>Safe to call from the render thread; it never creates anything.</remarks>
    public static Bitmap? TryGet(string systemId) =>
        Cache.TryGetValue(systemId, out var cached) ? cached : null;

    /// <summary>Draws and caches one system's label at its shell's proportions. UI thread only.</summary>
    /// <param name="panelAspect">Width over height of the shell's label panel.</param>
    public static Bitmap Warm(
        string systemId, string systemName, Color accent, IImage? platformArtwork, float panelAspect)
    {
        if (Cache.TryGetValue(systemId, out var cached))
        {
            return cached;
        }

        var bitmap = Draw(systemName, accent, platformArtwork, panelAspect);
        Cache[systemId] = bitmap;
        return bitmap;
    }

    private static Bitmap Draw(
        string systemName, Color accent, IImage? platformArtwork, float panelAspect)
    {
        var aspect = Math.Clamp(panelAspect, 0.35f, 4f);
        var width = aspect >= 1f ? (int)MathF.Round(ShortEdge * aspect) : ShortEdge;
        var height = aspect >= 1f ? ShortEdge : (int)MathF.Round(ShortEdge / aspect);

        var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using var context = target.CreateDrawingContext();

        // A real blank label is printed paper, not coloured plastic, so the base stays light and
        // the platform's colour arrives as a printed band. That also keeps the text legible on
        // systems whose accent is very dark or very saturated.
        var paper = Color.FromRgb(0xEC, 0xEA, 0xE6);
        var band = Math.Max(6d, height * 0.07);
        context.FillRectangle(new SolidColorBrush(paper), new Rect(0, 0, width, height));
        context.FillRectangle(new SolidColorBrush(accent), new Rect(0, 0, width, band));

        var sideBySide = aspect >= SideBySideAspect;
        // Type is sized against the shorter edge so a wide label does not get enormous text and a
        // portrait one unreadable text.
        var scale = Math.Min(width, height);
        var heading = Text(
            "ARTWORK MISSING", scale * (sideBySide ? 0.105 : 0.098), FontWeight.Bold,
            Color.FromRgb(0x5A, 0x57, 0x52));
        var subtitle = Text(
            systemName, scale * (sideBySide ? 0.086 : 0.080), FontWeight.Normal,
            Color.FromRgb(0x8A, 0x86, 0x80));
        var gap = scale * 0.045;

        if (sideBySide)
        {
            DrawSideBySide(context, platformArtwork, heading, subtitle, width, height, band, gap);
        }
        else
        {
            DrawStacked(context, platformArtwork, heading, subtitle, width, height, band, gap);
        }

        return target;
    }

    private static void DrawSideBySide(
        DrawingContext context, IImage? artwork, FormattedText heading, FormattedText subtitle,
        int width, int height, double band, double gap)
    {
        var centreY = band + ((height - band) * 0.5);
        var radius = height * 0.29;
        var centre = new Point(height * 0.44, centreY);
        DrawMedallion(context, artwork, centre, radius);

        var left = centre.X + radius + (height * 0.16);
        var block = heading.Height + gap + subtitle.Height;
        var top = centreY - (block * 0.5);
        // Keep the longest line inside the label rather than letting it run off the edge.
        heading.MaxTextWidth = Math.Max(24, width - left - (height * 0.10));
        subtitle.MaxTextWidth = heading.MaxTextWidth;
        context.DrawText(heading, new Point(left, top));
        context.DrawText(subtitle, new Point(left, top + heading.Height + gap));
    }

    private static void DrawStacked(
        DrawingContext context, IImage? artwork, FormattedText heading, FormattedText subtitle,
        int width, int height, double band, double gap)
    {
        var usable = Math.Max(24, width * 0.88);
        heading.MaxTextWidth = usable;
        subtitle.MaxTextWidth = usable;
        heading.TextAlignment = TextAlignment.Center;
        subtitle.TextAlignment = TextAlignment.Center;

        var radius = Math.Min(width, height) * 0.20;
        var block = (radius * 2) + gap + heading.Height + (gap * 0.5) + subtitle.Height;
        var top = band + ((height - band - block) * 0.5);

        DrawMedallion(context, artwork, new Point(width * 0.5, top + radius), radius);

        var textTop = top + (radius * 2) + gap;
        var textLeft = (width - usable) * 0.5;
        context.DrawText(heading, new Point(textLeft, textTop));
        context.DrawText(
            subtitle, new Point(textLeft, textTop + heading.Height + (gap * 0.5)));
    }

    private static void DrawMedallion(
        DrawingContext context, IImage? artwork, Point centre, double radius)
    {
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(0xDD, 0xDA, 0xD4)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)), 1),
            centre,
            radius,
            radius);

        if (artwork is null)
        {
            return;
        }

        var size = radius * 1.36;
        context.DrawImage(
            artwork,
            new Rect(centre.X - (size * 0.5), centre.Y - (size * 0.5), size, size));
    }

    private static FormattedText Text(string value, double size, FontWeight weight, Color colour) =>
        new(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight),
            size,
            new SolidColorBrush(colour));
}
