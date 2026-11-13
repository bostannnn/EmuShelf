using System.Globalization;

namespace EmuShelf.Core.Handheld;

/// <summary>
/// The Big Type look's ground colours (M47 B2): the flat colour a console gives the Launcher, and the floors every one of
/// them holds so white type stays readable on it. The approved colours themselves are per system
/// (<see cref="Systems.GameSystem.GroundColor"/>, authored in <c>KnownSystems</c>); this is the arithmetic: WCAG contrast,
/// the tile drawn over a ground, CIE76 distance in Lab, and the colour derived for a system the table does not cover.
/// Colours are 0xAARRGGBB, as <see cref="HandheldIconColor"/>'s are.
/// </summary>
/// <remarks>The values mirror <c>colour</c> in docs/prototypes/handheld-big-type/tokens.js.</remarks>
public static class HandheldGround
{
    /// <summary>White type on a ground reaches at least this contrast (WCAG 2, normal text).</summary>
    public const double TextFloor = 4.5;

    /// <summary>White type on a tile over a ground reaches at least this contrast.</summary>
    public const double TileFloor = 3.0;

    /// <summary>A tile at rest is white at this opacity over the ground (tokens.js <c>colour.TileRest</c>).</summary>
    public const double TileWhite = 0.17;

    /// <summary>No two consoles' grounds are closer than this (CIE76 ΔE in Lab), so each is easy to tell apart.</summary>
    public const double MinimumDistance = 18;

    private const uint White = 0xFFFFFFFF;

    /// <summary>Reads <c>#RRGGBB</c> or <c>#AARRGGBB</c> as an opaque 0xAARRGGBB colour; false for anything else.</summary>
    public static bool TryParse(string? text, out uint argb)
    {
        argb = 0;
        if (text is not { Length: 7 or 9 } || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            return false;
        argb = 0xFF000000 | (value & 0xFFFFFF);
        return true;
    }

    /// <summary>The ground a system with no approved colour gets: its accent, darkened until both floors hold.</summary>
    /// <remarks>
    /// Darkening scales the three channels together, in steps of one percent, so the hue is kept. A ground that already
    /// holds the floors comes back unchanged. Black holds them, so this always ends.
    /// </remarks>
    public static uint Derive(uint accent)
    {
        for (var step = 100; step >= 0; step--)
        {
            var scale = step / 100.0;
            var candidate = 0xFF000000
                | ((uint)Math.Round(Channel(accent, 16) * scale) << 16)
                | ((uint)Math.Round(Channel(accent, 8) * scale) << 8)
                | (uint)Math.Round(Channel(accent, 0) * scale);
            if (HoldsFloors(candidate))
                return candidate;
        }
        return 0xFF000000;
    }

    /// <summary>White type reaches <see cref="TextFloor"/> on the ground and <see cref="TileFloor"/> on a tile over it.</summary>
    public static bool HoldsFloors(uint ground) =>
        Contrast(White, ground) >= TextFloor && Contrast(White, Tile(ground)) >= TileFloor;

    /// <summary>A tile at rest over <paramref name="ground"/>: white at <see cref="TileWhite"/>, composited as a browser does (in sRGB).</summary>
    public static uint Tile(uint ground)
    {
        static uint Over(double channel) => (uint)Math.Round(channel + (TileWhite * (255 - channel)));
        return 0xFF000000 | (Over(Channel(ground, 16)) << 16) | (Over(Channel(ground, 8)) << 8) | Over(Channel(ground, 0));
    }

    /// <summary>WCAG 2 contrast ratio between two opaque colours, from 1 to 21.</summary>
    public static double Contrast(uint a, uint b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>WCAG 2 relative luminance of an opaque colour.</summary>
    public static double Luminance(uint argb) =>
        (0.2126 * Linear(Channel(argb, 16))) + (0.7152 * Linear(Channel(argb, 8))) + (0.0722 * Linear(Channel(argb, 0)));

    /// <summary>CIE76 distance between two colours: straight-line distance in CIE Lab (D65).</summary>
    public static double Distance(uint a, uint b)
    {
        var (l1, a1, b1) = Lab(a);
        var (l2, a2, b2) = Lab(b);
        return Math.Sqrt(((l1 - l2) * (l1 - l2)) + ((a1 - a2) * (a1 - a2)) + ((b1 - b2) * (b1 - b2)));
    }

    /// <summary>The colour in CIE Lab, D65 white.</summary>
    public static (double L, double A, double B) Lab(uint argb)
    {
        var (r, g, b) = (Linear(Channel(argb, 16)), Linear(Channel(argb, 8)), Linear(Channel(argb, 0)));
        var x = ((0.4124 * r) + (0.3576 * g) + (0.1805 * b)) / 0.95047;
        var y = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        var z = ((0.0193 * r) + (0.1192 * g) + (0.9505 * b)) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : (7.787 * t) + (16.0 / 116);
        var (fx, fy, fz) = (F(x), F(y), F(z));
        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>The colour as <c>#RRGGBB</c>.</summary>
    public static string ToHex(uint argb) => "#" + (argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    private static double Channel(uint argb, int shift) => (argb >> shift) & 0xFF;

    private static double Linear(double channel)
    {
        var v = channel / 255;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
