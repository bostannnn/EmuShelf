using System.Numerics;
using EmuShelf.Rendering.Models;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Writes a model's base-colour atlas out as PNG, plus a copy with the front face's UV triangles
/// drawn over it.
/// </summary>
/// <remarks>
/// Sourcing a shell stalls on one question that no measurement answers reliably: which island in
/// the atlas is the printed game label. It has to be neutralized before the asset can ship, and
/// guessing wrong either leaves someone's copyrighted artwork in the build or wipes the moulding.
/// A variance sweep only narrows it to candidates. Seeing the front face's UVs drawn on the atlas
/// settles it immediately, which turns a blocked task into a thirty-second one.
/// </remarks>
internal static class AtlasDump
{
    public static void Write(string modelPath, string outputDirectory)
    {
        var model = GlbLoader.Load(File.ReadAllBytes(modelPath), Matrix4x4.Identity, 2048);
        Directory.CreateDirectory(outputDirectory);
        var stem = Path.GetFileNameWithoutExtension(modelPath);

        var material = model.Materials.FirstOrDefault(m => m.BaseColorTexture >= 0);
        if (material is null)
        {
            Console.WriteLine($"{stem}: no base-colour map.");
            return;
        }

        var atlas = model.Textures[material.BaseColorTexture];
        var plain = Path.Combine(outputDirectory, $"{stem}-atlas.png");
        File.WriteAllBytes(plain, PngWriter.Encode(atlas.Width, atlas.Height, atlas.Rgba));

        // Copy before drawing, so the clean atlas stays clean.
        var overlaid = (byte[])atlas.Rgba.Clone();
        foreach (var (label, test, colour) in new (string, Func<float, bool>, (byte R, byte G, byte B))[]
        {
            ("front (+Z)", nz => nz > 0.9f, (0, 255, 0)),
            ("back (-Z)", nz => nz < -0.9f, (255, 0, 0)),
        })
        {
            var drawn = 0;
            foreach (var mesh in model.Meshes)
            {
                var v = mesh.Vertices;
                for (var t = 0; t < mesh.Indices.Length; t += 3)
                {
                    var i0 = (int)mesh.Indices[t] * 8;
                    var i1 = (int)mesh.Indices[t + 1] * 8;
                    var i2 = (int)mesh.Indices[t + 2] * 8;
                    if (!test(v[i0 + 5])) continue;
                    drawn++;
                    Line(overlaid, atlas, v[i0 + 6], v[i0 + 7], v[i1 + 6], v[i1 + 7], colour);
                    Line(overlaid, atlas, v[i1 + 6], v[i1 + 7], v[i2 + 6], v[i2 + 7], colour);
                    Line(overlaid, atlas, v[i2 + 6], v[i2 + 7], v[i0 + 6], v[i0 + 7], colour);
                }
            }

            Console.WriteLine($"  {label}: {drawn} triangles drawn in rgb{colour}");
        }

        var marked = Path.Combine(outputDirectory, $"{stem}-atlas-uv.png");
        File.WriteAllBytes(marked, PngWriter.Encode(atlas.Width, atlas.Height, overlaid));
        Console.WriteLine($"{stem}: wrote {plain} and {marked} ({atlas.Width}x{atlas.Height})");
    }

    private static void Line(
        byte[] pixels, TextureImage atlas, float u0, float v0, float u1, float v1,
        (byte R, byte G, byte B) colour)
    {
        var x0 = u0 * (atlas.Width - 1);
        var y0 = v0 * (atlas.Height - 1);
        var x1 = u1 * (atlas.Width - 1);
        var y1 = v1 * (atlas.Height - 1);
        var steps = (int)MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0)) + 1;
        for (var step = 0; step <= steps; step++)
        {
            var amount = step / (float)steps;
            Plot(pixels, atlas, (int)MathF.Round(x0 + ((x1 - x0) * amount)),
                (int)MathF.Round(y0 + ((y1 - y0) * amount)), colour);
        }
    }

    private static void Plot(
        byte[] pixels, TextureImage atlas, int x, int y, (byte R, byte G, byte B) colour)
    {
        if (x < 0 || y < 0 || x >= atlas.Width || y >= atlas.Height)
        {
            return;
        }

        var offset = ((y * atlas.Width) + x) * 4;
        pixels[offset] = colour.R;
        pixels[offset + 1] = colour.G;
        pixels[offset + 2] = colour.B;
        pixels[offset + 3] = 255;
    }
}
