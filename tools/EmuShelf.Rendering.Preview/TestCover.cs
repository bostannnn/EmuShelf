using EmuShelf.Rendering.Models;

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
}
