using EmuShelf.Rendering.Models;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Gl;

/// <summary>An owned GL texture object.</summary>
public sealed class GlTexture : IDisposable
{
    private readonly GL _gl;
    private uint _handle;

    private GlTexture(GL gl, uint handle, TextureTarget target)
    {
        _gl = gl;
        _handle = handle;
        Target = target;
    }

    public TextureTarget Target { get; }

    public uint Handle => _handle;

    /// <param name="srgb">True for base-colour maps and cover art, whose bytes are sRGB-encoded and
    /// must be linearised before any lighting maths touches them; false for the normal and
    /// metallic-roughness maps, whose values are already linear data rather than colour.</param>
    public static unsafe GlTexture Upload(GL gl, TextureImage image, bool srgb)
    {
        var handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, handle);

        // Row padding defaults to 4 bytes; our rows are tightly packed RGBA8, which is already a
        // multiple of 4, but be explicit so a future single-channel upload does not tear.
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);

        fixed (byte* pixels = image.Rgba)
        {
            gl.TexImage2D(
                TextureTarget.Texture2D,
                0,
                srgb ? InternalFormat.Srgb8Alpha8 : InternalFormat.Rgba8,
                (uint)image.Width,
                (uint)image.Height,
                0,
                PixelFormat.Rgba,
                PixelType.UnsignedByte,
                pixels);
        }

        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.LinearMipmapLinear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        return new GlTexture(gl, handle, TextureTarget.Texture2D);
    }

    /// <summary>A 1x1 texture, used as the neutral stand-in for an unbound sampler.</summary>
    /// <remarks>
    /// GLES is strict about sampling from an incomplete texture, and leaving a unit unbound is
    /// undefined behaviour on some drivers even when a companion "has this map" uniform means the
    /// shader never uses the result. Binding a real 1x1 keeps every unit complete.
    /// </remarks>
    public static GlTexture Solid(GL gl, byte r, byte g, byte b, byte a, bool srgb = false) =>
        Upload(gl, new TextureImage { Width = 1, Height = 1, Rgba = [r, g, b, a] }, srgb);

    /// <summary>An empty float cubemap with a full mip chain, ready to be rendered into.</summary>
    public static unsafe GlTexture CubeMap(GL gl, uint size, int mipLevels)
    {
        var handle = gl.GenTexture();
        gl.BindTexture(TextureTarget.TextureCubeMap, handle);

        for (var face = 0; face < 6; face++)
        {
            var target = TextureTarget.TextureCubeMapPositiveX + face;
            var levelSize = size;
            for (var level = 0; level < mipLevels; level++)
            {
                // RGBA16F, not RGBA8: the softboxes are far brighter than white, and clamping the
                // environment to 1.0 before it is convolved would flatten every reflection.
                gl.TexImage2D(
                    target, level, InternalFormat.Rgba16f, levelSize, levelSize, 0,
                    PixelFormat.Rgba, PixelType.Float, (void*)0);
                levelSize = Math.Max(1u, levelSize / 2);
            }
        }

        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter,
            (int)(mipLevels > 1 ? GLEnum.LinearMipmapLinear : GLEnum.Linear));
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureBaseLevel, 0);
        gl.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMaxLevel, mipLevels - 1);
        gl.BindTexture(TextureTarget.TextureCubeMap, 0);

        return new GlTexture(gl, handle, TextureTarget.TextureCubeMap);
    }

    public void Bind(uint unit)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + (int)unit);
        _gl.BindTexture(Target, _handle);
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        _gl.DeleteTexture(_handle);
        _handle = 0;
    }
}
