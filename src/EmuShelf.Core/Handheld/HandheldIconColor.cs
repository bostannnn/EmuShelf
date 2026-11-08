namespace EmuShelf.Core.Handheld;

/// <summary>
/// Picks a dock app's colour from its launcher icon (M45 H6d; HANDHELD-MODE.md, Top screen → Focused dock app), the way
/// Android's Palette picks a vibrant swatch: transparent, white, black and grey pixels are ignored, the rest are
/// counted by hue, and the strongest hue wins, so a white-background icon still gets its brand colour. An icon with no
/// colour at all gets a neutral slate. Picked once per app, then kept with the dock.
/// </summary>
public static class HandheldIconColor
{
    /// <summary>The slate an icon with no colour of its own gets.</summary>
    public const uint Slate = 0xFF64708A;

    private const int HueBuckets = 24;

    // A hue needs this share of the icon's opaque pixels to count as the icon's colour (a stray anti-aliased edge never does).
    private const double MinimumShare = 0.02;

    /// <summary>
    /// The icon's colour as 0xAARRGGBB (fully opaque), its lightness kept between 35% and 62% so it reads as a sweep
    /// behind a white card. <paramref name="argb"/> is the icon's pixels as 0xAARRGGBB, straight (not premultiplied) alpha.
    /// </summary>
    public static uint Pick(ReadOnlySpan<uint> argb)
    {
        Span<double> weight = stackalloc double[HueBuckets];
        Span<double> red = stackalloc double[HueBuckets];
        Span<double> green = stackalloc double[HueBuckets];
        Span<double> blue = stackalloc double[HueBuckets];
        var opaque = 0;
        foreach (var pixel in argb)
        {
            var alpha = (pixel >> 24) / 255.0;
            if (alpha < 0.5)
                continue;
            opaque++;
            var (r, g, b) = (((pixel >> 16) & 0xFF) / 255.0, ((pixel >> 8) & 0xFF) / 255.0, (pixel & 0xFF) / 255.0);
            var (hue, saturation, lightness) = ToHsl(r, g, b);
            // White, black and grey say nothing about the brand.
            if (saturation < 0.25 || lightness < 0.12 || lightness > 0.92)
                continue;
            var bucket = (int)(hue / 360 * HueBuckets) % HueBuckets;
            // Vivid, mid-light pixels count most (Palette's "vibrant" target).
            var w = saturation * (1 - Math.Abs((2 * lightness) - 1)) * alpha;
            weight[bucket] += w;
            red[bucket] += r * w;
            green[bucket] += g * w;
            blue[bucket] += b * w;
        }

        var best = -1;
        for (var i = 0; i < HueBuckets; i++)
        {
            if (best < 0 || weight[i] > weight[best])
                best = i;
        }
        if (opaque == 0 || best < 0 || weight[best] < opaque * MinimumShare * 0.25)
            return Slate;

        var (h, s, l) = ToHsl(red[best] / weight[best], green[best] / weight[best], blue[best] / weight[best]);
        return FromHsl(h, Math.Max(s, 0.45), Math.Clamp(l, 0.35, 0.62));
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta < 1e-9)
            return (0, 0, lightness);
        var saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
        double hue;
        if (max == r)
            hue = 60 * (((g - b) / delta) % 6);
        else if (max == g)
            hue = 60 * (((b - r) / delta) + 2);
        else
            hue = 60 * (((r - g) / delta) + 4);
        if (hue < 0)
            hue += 360;
        return (hue, Math.Min(1, saturation), lightness);
    }

    private static uint FromHsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = chroma * (1 - Math.Abs(((hue / 60) % 2) - 1));
        var m = lightness - (chroma / 2);
        var (r, g, b) = (hue % 360) switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        static uint Channel(double value) => (uint)Math.Clamp(Math.Round(value * 255), 0, 255);
        return 0xFF000000 | (Channel(r + m) << 16) | (Channel(g + m) << 8) | Channel(b + m);
    }
}
