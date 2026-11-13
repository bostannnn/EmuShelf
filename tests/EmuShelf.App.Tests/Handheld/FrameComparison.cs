using System;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// A frame's pixels as four bytes each, rows packed with no padding. The channel order is whatever the decoder gave; a
/// comparison only ever sees two frames decoded the same way, so it never needs to know.
/// </summary>
public sealed record FramePixels(int Width, int Height, byte[] Pixels)
{
    public const int BytesPerPixel = 4;

    public static FramePixels Blank(int width, int height) => new(width, height, new byte[width * height * BytesPerPixel]);

    public int Stride => Width * BytesPerPixel;
}

/// <summary>What <see cref="FrameComparison.Compare"/> found: equal, a different size, or a count of pixels that differ.</summary>
public sealed record FrameDifference(int DifferingPixels, string? SizeMismatch, FramePixels? Diff)
{
    public bool IsIdentical => SizeMismatch is null && DifferingPixels == 0;
}

/// <summary>
/// Pixel-for-pixel comparison of two rendered frames (M47 B0). Any difference in any channel counts; there is no
/// tolerance, because B1 must prove its frames identical, not close. The diff image is the newer frame faded to a quarter
/// of its brightness in grey, with every differing pixel solid magenta, so the changes stand out wherever they are.
/// </summary>
public static class FrameComparison
{
    public static FrameDifference Compare(FramePixels expected, FramePixels actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return new FrameDifference(0, $"{expected.Width}x{expected.Height} became {actual.Width}x{actual.Height}", null);
        if (expected.Pixels.Length != actual.Pixels.Length || expected.Pixels.Length != expected.Width * expected.Height * FramePixels.BytesPerPixel)
            throw new ArgumentException("A frame's buffer does not match its size.");

        var a = expected.Pixels;
        var b = actual.Pixels;
        if (a.AsSpan().SequenceEqual(b))
            return new FrameDifference(0, null, null);
        var count = 0;
        for (var i = 0; i < a.Length; i += FramePixels.BytesPerPixel)
        {
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3])
                count++;
        }
        return new FrameDifference(count, null, DiffImage(a, b, expected.Width, expected.Height));
    }

    private static FramePixels DiffImage(byte[] a, byte[] b, int width, int height)
    {
        var diff = FramePixels.Blank(width, height);
        var d = diff.Pixels;
        for (var i = 0; i < a.Length; i += FramePixels.BytesPerPixel)
        {
            var same = a[i] == b[i] && a[i + 1] == b[i + 1] && a[i + 2] == b[i + 2] && a[i + 3] == b[i + 3];
            if (same)
            {
                // Magenta is symmetric in its first and third channels, and grey in all three, so the image reads the
                // same whether the decoder gave BGRA or RGBA.
                var grey = (byte)((b[i] + b[i + 1] + b[i + 2]) / 12);
                d[i] = grey;
                d[i + 1] = grey;
                d[i + 2] = grey;
            }
            else
            {
                d[i] = 255;
                d[i + 1] = 0;
                d[i + 2] = 255;
            }
            d[i + 3] = 255;
        }
        return diff;
    }
}
