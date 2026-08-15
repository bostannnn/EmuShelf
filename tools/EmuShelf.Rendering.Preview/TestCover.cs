using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// A synthetic stand-in for scraped cover art.
/// </summary>
/// <remarks>
/// Deliberately asymmetric in both axes — a big letter F, plus a differently coloured square in
/// each corner. A symmetrical test image cannot tell you that a panel is mirrored or upside down,
/// which is exactly the class of bug object-space decal projection invites.
/// </remarks>
internal static class TestCover
{
    /// <summary>
    /// A stand-in at a given cover shape (width over height). The height follows the aspect rather
    /// than the width being trimmed, so every stand-in carries the same detail down its long axis.
    /// </summary>
    /// <remarks>
    /// The guard is not ceremony. Without it a zero or negative ratio divides to infinity, and an
    /// unchecked float-to-int conversion of that is unspecified — in practice int.MinValue, which
    /// surfaces as an OverflowException from the pixel buffer that names neither the aspect nor the
    /// table it came from. The ratios are read from KnownSystems, so the value arrives from a table
    /// this file does not own.
    /// </remarks>
    public static TextureImage Create(double aspect)
    {
        if (!double.IsFinite(aspect) || aspect <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspect), aspect, "A cover aspect must be a finite ratio greater than zero.");
        }

        return Create(512, (int)Math.Round(512 / aspect));
    }

    public static TextureImage Create(int width = 512, int height = 724)
    {
        var pixels = new byte[width * height * 4];

        void Fill(int x0, int y0, int x1, int y1, byte r, byte g, byte b)
        {
            for (var y = Math.Max(0, y0); y < Math.Min(height, y1); y++)
            {
                for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
                {
                    var o = ((y * width) + x) * 4;
                    pixels[o] = r;
                    pixels[o + 1] = g;
                    pixels[o + 2] = b;
                    pixels[o + 3] = 255;
                }
            }
        }

        // Background: a vertical gradient so the top and bottom of the panel are distinguishable.
        for (var y = 0; y < height; y++)
        {
            var t = y / (float)(height - 1);
            var r = (byte)(24 + (60 * t));
            var g = (byte)(30 + (18 * t));
            var b = (byte)(86 + (74 * t));
            Fill(0, y, width, y + 1, r, g, b);
        }

        var unit = width / 16;

        // Title bar across the top.
        Fill(0, 0, width, unit * 2, 236, 232, 220);

        // A blocky capital F, occupying the middle of the panel.
        var fLeft = width / 4;
        var fTop = height / 4;
        var fHeight = height / 2;
        var fWidth = width / 2;
        Fill(fLeft, fTop, fLeft + unit, fTop + fHeight, 255, 255, 255);                     // stem
        Fill(fLeft, fTop, fLeft + fWidth, fTop + unit, 255, 255, 255);                      // top arm
        Fill(fLeft, fTop + (fHeight / 2), fLeft + (int)(fWidth * 0.72), fTop + (fHeight / 2) + unit, 255, 255, 255);

        // Corner keys: red top-left, green top-right, blue bottom-left, yellow bottom-right.
        var key = unit * 2;
        Fill(0, 0, key, key, 220, 40, 40);
        Fill(width - key, 0, width, key, 40, 200, 70);
        Fill(0, height - key, key, height, 60, 90, 235);
        Fill(width - key, height - key, width, height, 240, 210, 40);

        return new TextureImage { Width = width, Height = height, Rgba = pixels };
    }

    /// <summary>
    /// A stand-in for a scraped back inlay, at a given shape.
    /// </summary>
    /// <remarks>
    /// Distinct from the front on purpose, and not merely recoloured: the failure this catches is a
    /// back panel that draws the <em>front</em>'s art, which two blue panels with a letter on them
    /// cannot show. The barcode block is the asymmetry that matters here — a real inlay carries one
    /// in the bottom-right corner, so a mirrored back panel is legible at a glance rather than
    /// needing the corner keys read off in order.
    /// </remarks>
    public static TextureImage CreateBack(double aspect)
    {
        var (width, height) = Dimensions(aspect, 512);
        var pixels = new byte[width * height * 4];
        var fill = Filler(pixels, width, height);

        for (var y = 0; y < height; y++)
        {
            var t = y / (float)(height - 1);
            fill(0, y, width, y + 1, (byte)(18 + (26 * t)), (byte)(20 + (30 * t)), (byte)(24 + (36 * t)));
        }

        var unit = Math.Max(1, width / 16);
        fill(0, 0, width, unit * 2, 214, 210, 200);

        // Three "screenshot" plates down the left, the way a back inlay lays them out.
        for (var i = 0; i < 3; i++)
        {
            var top = (unit * 3) + (i * unit * 3);
            fill(unit, top, unit + (unit * 5), top + (unit * 2), 90, 120, 170);
        }

        // Text-block rules on the right.
        for (var i = 0; i < 6; i++)
        {
            var top = (unit * 3) + (i * unit);
            fill(unit * 7, top, width - unit, top + (unit / 3), 150, 150, 158);
        }

        // The barcode, bottom-right, as a real inlay carries it.
        for (var x = 0; x < unit * 4; x++)
        {
            if (x % 3 == 0)
            {
                continue;
            }

            fill(width - unit - (unit * 4) + x, height - (unit * 3), width - unit - (unit * 4) + x + 1,
                height - unit, 235, 235, 235);
        }

        var key = unit * 2;
        fill(0, 0, key, key, 240, 130, 30);                                   // orange top-left
        fill(width - key, 0, width, key, 150, 60, 200);                       // purple top-right
        fill(0, height - key, key, height, 40, 190, 190);                     // cyan bottom-left
        fill(width - key, height - key, width, height, 250, 250, 250);        // white bottom-right

        return new TextureImage { Width = width, Height = height, Rgba = pixels };
    }

    /// <summary>
    /// A stand-in for a scraped spine, at a given shape.
    /// </summary>
    /// <remarks>
    /// The shape is a parameter rather than the panel's own, and that is the point of this one. A
    /// spine panel is a 13mm strip beside a 135mm face, and every case shell fits art to it with
    /// <see cref="ArtFit.Stretch"/> — which is exactly right when the scrape is a spine scan and
    /// exactly wrong when it is anything else. Rendering a true-shaped strip beside a mis-shaped
    /// one is the only way to see which of those a real scrape behaves like.
    ///
    /// The band and the notch are at the top, so a spine drawn upside down reads immediately.
    /// </remarks>
    public static TextureImage CreateSpine(double aspect)
    {
        var (width, height) = Dimensions(aspect, 64);
        var pixels = new byte[width * height * 4];
        var fill = Filler(pixels, width, height);

        for (var y = 0; y < height; y++)
        {
            var t = y / (float)(height - 1);
            fill(0, y, width, y + 1, (byte)(120 + (70 * t)), (byte)(30 + (20 * t)), (byte)(40 + (30 * t)));
        }

        var band = Math.Max(2, height / 12);
        fill(0, 0, width, band, 16, 16, 20);
        fill(0, band, width, band + Math.Max(1, height / 200), 240, 240, 240);

        // A notch a third of the way down: an unmistakable "this end up" that survives being
        // squeezed to a few pixels wide.
        var notch = height / 3;
        fill(0, notch, Math.Max(1, width / 2), notch + Math.Max(2, height / 60), 250, 240, 120);

        // Title rules running down the strip.
        for (var i = 0; i < 8; i++)
        {
            var top = (height / 2) + (i * Math.Max(2, height / 40));
            fill(Math.Max(1, width / 4), top, Math.Max(2, width * 3 / 4), top + Math.Max(1, height / 160),
                235, 235, 235);
        }

        return new TextureImage { Width = width, Height = height, Rgba = pixels };
    }

    private static (int Width, int Height) Dimensions(double aspect, int width)
    {
        if (!double.IsFinite(aspect) || aspect <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aspect), aspect, "An artwork aspect must be a finite ratio greater than zero.");
        }

        return (width, Math.Max(1, (int)Math.Round(width / aspect)));
    }

    private static Action<int, int, int, int, byte, byte, byte> Filler(byte[] pixels, int width, int height) =>
        (x0, y0, x1, y1, r, g, b) =>
        {
            for (var y = Math.Max(0, y0); y < Math.Min(height, y1); y++)
            {
                for (var x = Math.Max(0, x0); x < Math.Min(width, x1); x++)
                {
                    var o = ((y * width) + x) * 4;
                    pixels[o] = r;
                    pixels[o + 1] = g;
                    pixels[o + 2] = b;
                    pixels[o + 3] = 255;
                }
            }
        };
}
