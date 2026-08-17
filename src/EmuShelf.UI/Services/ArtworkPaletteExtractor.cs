using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>
/// Derives an <see cref="ArtworkPalette"/> from a game's on-screen cover. <see cref="CopyPixels"/> reads
/// the live bitmap and must run on the UI thread; <see cref="FromBgraPixels"/> is a pure analysis pass
/// safe to run on a worker. Returns <c>null</c> for grayscale/near-monochrome art so the caller falls
/// back to the chosen theme.
/// </summary>
internal static class ArtworkPaletteExtractor
{
    // 12 hue buckets of 30°; each accumulates saturation-weighted colour so the dominant vivid hue wins.
    private const int HueBuckets = 12;

    // A pixel must be at least this saturated, and within this lightness band, to vote for a hue. Pure
    // black/white borders and washed-out pixels are ignored for hue but still count toward brightness.
    private const double MinSaturation = 0.22;
    private const double MinLightness = 0.15;
    private const double MaxLightness = 0.85;

    /// <summary>Copies a live bitmap's BGRA pixels into a plain array. UI-thread only.</summary>
    public static byte[]? CopyPixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
            return null;

        var stride = size.Width * 4;
        var buffer = new byte[(long)stride * size.Height <= int.MaxValue ? stride * size.Height : 0];
        if (buffer.Length == 0)
            return null;

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(
                new PixelRect(0, 0, size.Width, size.Height),
                handle.AddrOfPinnedObject(),
                buffer.Length,
                stride);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            handle.Free();
        }

        return buffer;
    }

    public static ArtworkPalette? FromBgraPixels(ReadOnlySpan<byte> bgra, bool? previousIsDark)
    {
        Span<double> weight = stackalloc double[HueBuckets];
        Span<double> sumR = stackalloc double[HueBuckets];
        Span<double> sumG = stackalloc double[HueBuckets];
        Span<double> sumB = stackalloc double[HueBuckets];

        double luminanceSum = 0;
        var counted = 0;

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 8)
                continue;

            var color = new Rgb(bgra[i + 2], bgra[i + 1], bgra[i]);
            luminanceSum += ArtworkColor.RelativeLuminance(color);
            counted++;

            var (hue, saturation, lightness) = ArtworkColor.ToHsl(color);
            if (saturation < MinSaturation || lightness < MinLightness || lightness > MaxLightness)
                continue;

            var bucket = (int)(hue / 360d * HueBuckets) % HueBuckets;
            if (bucket < 0)
                bucket += HueBuckets;

            weight[bucket] += saturation;
            sumR[bucket] += color.R * saturation;
            sumG[bucket] += color.G * saturation;
            sumB[bucket] += color.B * saturation;
        }

        if (counted == 0)
            return null;

        var best = -1;
        double bestWeight = 0;
        for (var i = 0; i < HueBuckets; i++)
        {
            if (weight[i] <= bestWeight)
                continue;
            bestWeight = weight[i];
            best = i;
        }

        if (best < 0 || bestWeight <= 0)
            return null;

        var vibrant = new Rgb(
            (byte)Math.Clamp(sumR[best] / weight[best], 0, 255),
            (byte)Math.Clamp(sumG[best] / weight[best], 0, 255),
            (byte)Math.Clamp(sumB[best] / weight[best], 0, 255));

        return ArtworkPaletteFactory.Create(vibrant, luminanceSum / counted, previousIsDark);
    }
}
