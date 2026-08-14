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
/// Drawn once per system and cached: it depends only on the platform, never on the game. Creation
/// touches Avalonia's rendering stack and must therefore happen on the UI thread, so the shelf
/// warms the cache when its item list changes and the GL frame only ever reads it.
/// </remarks>
public static class CartridgeLabelPlaceholder
{
    // 2.93:1, matching the SNES shell's label panel, so ArtFit.Cover has nothing to crop.
    private const int LabelWidth = 586;
    private const int LabelHeight = 200;

    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    /// <summary>Returns the cached label, or null when it has not been warmed yet.</summary>
    /// <remarks>Safe to call from the render thread; it never creates anything.</remarks>
    public static Bitmap? TryGet(string systemId) =>
        Cache.TryGetValue(systemId, out var cached) ? cached : null;

    /// <summary>Draws and caches one system's label. UI thread only.</summary>
    public static Bitmap Warm(
        string systemId, string systemName, Color accent, IImage? platformArtwork)
    {
        if (Cache.TryGetValue(systemId, out var cached))
        {
            return cached;
        }

        var bitmap = Draw(systemName, accent, platformArtwork);
        Cache[systemId] = bitmap;
        return bitmap;
    }

    private static Bitmap Draw(string systemName, Color accent, IImage? platformArtwork)
    {
        var target = new RenderTargetBitmap(
            new PixelSize(LabelWidth, LabelHeight), new Vector(96, 96));

        using var context = target.CreateDrawingContext();

        // A real blank label is printed paper, not coloured plastic, so the base stays light and
        // the platform's colour arrives as a printed band. That also keeps the text legible on
        // systems whose accent is very dark or very saturated.
        var paper = Color.FromRgb(0xEC, 0xEA, 0xE6);
        context.FillRectangle(
            new SolidColorBrush(paper), new Rect(0, 0, LabelWidth, LabelHeight));
        context.FillRectangle(
            new SolidColorBrush(accent), new Rect(0, 0, LabelWidth, LabelHeight * 0.07));

        var centreY = (LabelHeight * 0.07) + ((LabelHeight - (LabelHeight * 0.07)) * 0.5);
        var medallionRadius = LabelHeight * 0.29;
        var medallionCentre = new Point(LabelHeight * 0.44, centreY);
        context.DrawEllipse(
            new SolidColorBrush(Color.FromRgb(0xDD, 0xDA, 0xD4)),
            new Pen(new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0)), 1),
            medallionCentre,
            medallionRadius,
            medallionRadius);

        if (platformArtwork is not null)
        {
            var artSize = medallionRadius * 1.36;
            context.DrawImage(
                platformArtwork,
                new Rect(
                    medallionCentre.X - (artSize * 0.5),
                    medallionCentre.Y - (artSize * 0.5),
                    artSize,
                    artSize));
        }

        var textLeft = medallionCentre.X + medallionRadius + (LabelHeight * 0.16);
        var heading = Text("ARTWORK MISSING", 27, FontWeight.Bold, Color.FromRgb(0x5A, 0x57, 0x52));
        var subtitle = Text(systemName, 22, FontWeight.Normal, Color.FromRgb(0x8A, 0x86, 0x80));
        var block = heading.Height + (LabelHeight * 0.045) + subtitle.Height;
        var textTop = centreY - (block * 0.5);

        context.DrawText(heading, new Point(textLeft, textTop));
        context.DrawText(
            subtitle, new Point(textLeft, textTop + heading.Height + (LabelHeight * 0.045)));

        return target;
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
