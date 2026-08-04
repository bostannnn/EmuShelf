namespace EmuShelf.Core.Settings;

/// <summary>
/// A plain 8-bit sRGB colour and the small colour-maths kit the artwork palette needs. Kept in Core and
/// free of any UI framework: the extractor feeds it raw pixels, the factory derives an
/// <see cref="ArtworkPalette"/>, and the App layer parses the resulting hex strings into brushes.
/// </summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

public static class ArtworkColor
{
    /// <summary>WCAG relative luminance in [0,1]. Used for the dark/light decision and contrast checks.</summary>
    public static double RelativeLuminance(Rgb color)
    {
        static double Channel(byte raw)
        {
            var c = raw / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    /// <summary>WCAG contrast ratio in [1,21] between two colours, order-independent.</summary>
    public static double ContrastRatio(Rgb a, Rgb b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Hue in [0,360), saturation and lightness in [0,1].</summary>
    public static (double H, double S, double L) ToHsl(Rgb color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        var l = (max + min) / 2d;
        double h = 0;
        double s = 0;

        if (delta > 1e-9)
        {
            s = l < 0.5 ? delta / (max + min) : delta / (2d - max - min);
            if (max == r)
                h = ((g - b) / delta + (g < b ? 6d : 0d)) * 60d;
            else if (max == g)
                h = ((b - r) / delta + 2d) * 60d;
            else
                h = ((r - g) / delta + 4d) * 60d;
        }

        return (h, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = ((h % 360d) + 360d) % 360d;
        s = Math.Clamp(s, 0d, 1d);
        l = Math.Clamp(l, 0d, 1d);

        if (s <= 1e-9)
        {
            var g = ToByte(l);
            return new Rgb(g, g, g);
        }

        var q = l < 0.5 ? l * (1d + s) : l + s - l * s;
        var p = 2d * l - q;
        var hk = h / 360d;
        return new Rgb(
            ToByte(HueToChannel(p, q, hk + 1d / 3d)),
            ToByte(HueToChannel(p, q, hk)),
            ToByte(HueToChannel(p, q, hk - 1d / 3d)));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        t = ((t % 1d) + 1d) % 1d;
        if (t < 1d / 6d)
            return p + (q - p) * 6d * t;
        if (t < 1d / 2d)
            return q;
        if (t < 2d / 3d)
            return p + (q - p) * (2d / 3d - t) * 6d;
        return p;
    }

    private static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255d), 0, 255);
}
