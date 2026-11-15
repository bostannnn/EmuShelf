using System.Numerics;

namespace EmuShelf.Rendering.Stage.Backdrops;

/// <summary>
/// Big Type's recolour of a backdrop toward its console's colour (M47 B13): exactly what the owner approved on backdrop-review.html, a
/// layer of the colour over the animation with CSS <c>mix-blend-mode: color</c> at an opacity equal to the pull. The "color" blend of
/// W3C Compositing and Blending Level 1 takes the hue and saturation of the source (the console's colour) and the luminosity of the
/// backdrop (the animation's pixel), with the specification's <c>SetLum</c> and <c>ClipColor</c>; the layer's opacity then mixes that
/// with the pixel as it was. Colours are sRGB values from 0 to 1, as the browser blends them. <c>backdrop-output.frag</c> does the same
/// in the backdrop's output pass; this is the reference its tests are worked against.
/// </summary>
public static class BackdropRecolour
{
    /// <summary>The specification's <c>Lum</c>: 0.3 R + 0.59 G + 0.11 B.</summary>
    public static float Lum(Vector3 c) => (0.3f * c.X) + (0.59f * c.Y) + (0.11f * c.Z);

    /// <summary>The specification's <c>ClipColor</c>: pulls a colour with a channel outside 0 to 1 back toward its luminosity.</summary>
    public static Vector3 ClipColor(Vector3 c)
    {
        var l = Lum(c);
        var n = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        var x = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        if (n < 0f)
            c = new Vector3(l) + ((c - new Vector3(l)) * l / (l - n));
        if (x > 1f)
            c = new Vector3(l) + ((c - new Vector3(l)) * (1f - l) / (x - l));
        return c;
    }

    /// <summary>The specification's <c>SetLum</c>: the colour moved to luminosity <paramref name="l"/>, then clipped.</summary>
    public static Vector3 SetLum(Vector3 c, float l) => ClipColor(c + new Vector3(l - Lum(c)));

    /// <summary>The "color" blend: <c>B(Cb, Cs) = SetLum(Cs, Lum(Cb))</c>.</summary>
    public static Vector3 Color(Vector3 backdrop, Vector3 source) => SetLum(source, Lum(backdrop));

    /// <summary>
    /// The pixel recoloured: the blend laid over it at <paramref name="pull"/> opacity (an opaque backdrop: <c>(1 - a) Cb + a B</c>).
    /// A pull of 0 returns the pixel unchanged.
    /// </summary>
    public static Vector3 Apply(Vector3 pixel, Vector3 ground, float pull) =>
        pull <= 0f ? pixel : Vector3.Lerp(pixel, Color(pixel, ground), Math.Clamp(pull, 0f, 1f));
}
