using System.Numerics;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Stage;

/// <summary>
/// A 2D affine transform in row-vector form (as Avalonia's <c>Matrix</c>): x' = x·M11 + y·M21 + M31,
/// y' = x·M12 + y·M22 + M32.
/// </summary>
public readonly record struct StageAffine(float M11, float M12, float M21, float M22, float M31, float M32)
{
    public static StageAffine Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>Rotation, scale and translation, for interpolating without shearing (the folder parts never shear).</summary>
    internal (float Angle, float ScaleX, float ScaleY, float X, float Y) Decompose()
    {
        var scaleX = MathF.Sqrt((M11 * M11) + (M12 * M12));
        var angle = MathF.Atan2(M12, M11);
        var scaleY = scaleX == 0 ? 0 : ((M11 * M22) - (M12 * M21)) / scaleX;
        return (angle, scaleX, scaleY, M31, M32);
    }

    internal static StageAffine Compose(float angle, float scaleX, float scaleY, float x, float y)
    {
        var cos = MathF.Cos(angle);
        var sin = MathF.Sin(angle);
        return new StageAffine(cos * scaleX, sin * scaleX, -sin * scaleY, cos * scaleY, x, y);
    }

    /// <summary>Interpolates part by part (angle, scales, translation); <paramref name="t"/> may overshoot.</summary>
    public static StageAffine Lerp(StageAffine from, StageAffine to, float t)
    {
        var a = from.Decompose();
        var b = to.Decompose();
        return Compose(
            a.Angle + ((b.Angle - a.Angle) * t),
            a.ScaleX + ((b.ScaleX - a.ScaleX) * t),
            a.ScaleY + ((b.ScaleY - a.ScaleY) * t),
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t));
    }
}

/// <summary>Which way a folder's flap is: shut, tilted so the covers peek out (focus), or swung open (A).</summary>
public enum FolderArtState
{
    Closed,
    Peek,
    Open,
}

/// <summary>
/// One piece of a folder's art, pictured untransformed at its place on the canvas, with where each state puts
/// it (a canvas-space transform of that picture) and how long it glides between states.
/// </summary>
public sealed record FolderArtPart(StageImage Image, StageAffine Closed, StageAffine Peek, StageAffine Open, float Milliseconds)
{
    public StageAffine For(FolderArtState state) => state switch
    {
        FolderArtState.Peek => Peek,
        FolderArtState.Open => Open,
        _ => Closed,
    };
}

/// <summary>A folder's art as parts, drawn in order (shadow, back plate, covers, front flap).</summary>
public sealed record FolderArt(IReadOnlyList<FolderArtPart> Parts);

/// <summary>
/// A folder's art on the GPU: its parts' textures and the glide between states, on the curve the XAML's
/// transitions use, cubic-bezier(.34, 1.3, .64, 1) (300 ms for the flap, 360 ms for the covers).
/// </summary>
internal sealed class FolderArtState2D : IDisposable
{
    private readonly GL _gl;
    private readonly uint[] _textures;
    private StageAffine[] _from;
    private FolderArtState _state;
    private float _start = float.NegativeInfinity;

    public FolderArtState2D(GL gl, FolderArt art, FolderArtState state)
    {
        _gl = gl;
        Art = art;
        _textures = art.Parts.Select(part => SpriteTextures.Upload(gl, part.Image.Pixels, repeat: false)).ToArray();
        _state = state;
        _from = art.Parts.Select(part => part.For(state)).ToArray();
    }

    public FolderArt Art { get; }

    public FolderArtState State => _state;

    public void SetState(FolderArtState state, float now, bool animate)
    {
        if (state == _state)
            return;
        // Glide on from wherever each part is now.
        _from = Art.Parts.Select((_, i) => Current(i, now)).ToArray();
        _state = state;
        _start = animate ? now : float.NegativeInfinity;
    }

    public bool IsAnimating(float now) => Art.Parts.Any(part => now - _start < part.Milliseconds);

    private StageAffine Current(int index, float now)
    {
        var part = Art.Parts[index];
        var to = part.For(_state);
        var t = part.Milliseconds <= 0 ? 1f : (now - _start) / part.Milliseconds;
        if (t >= 1f)
            return to;
        return StageAffine.Lerp(_from[index], to, StageCurves.CubicBezier(0.34f, 1.3f, 0.64f, 1f, Math.Max(0f, t)));
    }

    /// <summary>Hands each part's texture, picture and transform to <paramref name="draw"/>, in order.</summary>
    public void Draw(float now, Action<uint, StageImage, StageAffine> draw)
    {
        for (var i = 0; i < _textures.Length; i++)
            draw(_textures[i], Art.Parts[i].Image, Current(i, now));
    }

    public void Dispose()
    {
        foreach (var texture in _textures)
            _gl.DeleteTexture(texture);
    }
}
