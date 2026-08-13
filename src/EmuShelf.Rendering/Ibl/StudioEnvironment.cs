using System.Numerics;
using EmuShelf.Rendering.Gl;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Ibl;

/// <summary>
/// The baked lighting the shell sits in: a procedural studio cubemap, its diffuse irradiance, and
/// its GGX-prefiltered specular chain.
/// </summary>
/// <remarks>
/// Baked on the GPU at load and again whenever the accent colour changes, which on the shelf means
/// when the player moves to a different console — not per frame, and not per game.
/// </remarks>
public sealed class StudioEnvironment : IDisposable
{
    /// <summary>Face size of the source studio cubemap.</summary>
    private const uint EnvironmentSize = 256;

    /// <summary>Face size of the diffuse irradiance cubemap. Irradiance has no high frequencies.</summary>
    private const uint IrradianceSize = 32;

    /// <summary>Face size of the roughest-to-sharpest specular chain's base level.</summary>
    private const uint SpecularSize = 128;

    /// <summary>Mip levels in the specular chain; level N is roughness N/(levels-1).</summary>
    private const int SpecularMipLevels = 5;

    private const int PrefilterSamples = 64;

    private readonly GL _gl;
    private readonly GlTexture _environment;
    private readonly GlTexture _irradiance;
    private readonly GlTexture _specular;

    private StudioEnvironment(GL gl, GlTexture environment, GlTexture irradiance, GlTexture specular)
    {
        _gl = gl;
        _environment = environment;
        _irradiance = irradiance;
        _specular = specular;
    }

    public GlTexture Irradiance => _irradiance;

    public GlTexture Specular => _specular;

    /// <summary>Highest mip index in the specular chain, i.e. the roughness=1 level.</summary>
    public float SpecularMaxLod => SpecularMipLevels - 1;

    /// <param name="accent">The focused system's accent, in linear space.</param>
    /// <param name="accentMix">0 leaves the studio neutral; 1 fully tints its ambient.</param>
    /// <param name="intensity">Overall brightness of the room.</param>
    public static StudioEnvironment Bake(
        GL gl,
        GlslDialect dialect,
        int majorVersion,
        int minorVersion,
        Vector3 accent,
        float accentMix,
        float intensity)
    {
        var environment = GlTexture.CubeMap(gl, EnvironmentSize, MipLevelsFor(EnvironmentSize));
        var irradiance = GlTexture.CubeMap(gl, IrradianceSize, 1);
        var specular = GlTexture.CubeMap(gl, SpecularSize, SpecularMipLevels);

        var vertexSource = ShaderLibrary.Load("fullscreen.vert", dialect, majorVersion, minorVersion);
        using var environmentProgram = GlProgram.Create(
            gl, vertexSource,
            ShaderLibrary.Load("environment.frag", dialect, majorVersion, minorVersion, "cubeface"),
            "environment");
        using var irradianceProgram = GlProgram.Create(
            gl, vertexSource,
            ShaderLibrary.Load("irradiance.frag", dialect, majorVersion, minorVersion, "cubeface"),
            "irradiance");
        using var prefilterProgram = GlProgram.Create(
            gl, vertexSource,
            ShaderLibrary.Load("prefilter.frag", dialect, majorVersion, minorVersion, "cubeface"),
            "prefilter");

        // Core profiles refuse to draw with no vertex array bound, even for an attribute-less
        // fullscreen triangle that reads only gl_VertexID.
        var vao = gl.GenVertexArray();
        var framebuffer = gl.GenFramebuffer();

        // Save what we are about to trample: on the Avalonia path this runs inside the host's own
        // frame, and handing the context back with a different framebuffer bound loses the window.
        gl.GetInteger(GetPName.DrawFramebufferBinding, out var previousFramebuffer);
        Span<int> previousViewport = stackalloc int[4];
        gl.GetInteger(GetPName.Viewport, previousViewport);

        gl.BindVertexArray(vao);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.Blend);
        gl.Disable(EnableCap.CullFace);

        if (dialect == GlslDialect.Desktop)
        {
            // ES 3.0 filters across cube faces by definition; desktop GL has to be asked, and
            // without it the irradiance map shows creases along the face joins.
            gl.Enable(EnableCap.TextureCubeMapSeamless);
        }

        try
        {
            environmentProgram.Use();
            environmentProgram.Set("uAccent", accent);
            environmentProgram.Set("uAccentMix", accentMix);
            environmentProgram.Set("uIntensity", intensity);
            RenderCube(gl, environmentProgram, environment, EnvironmentSize, 0);

            // The prefilter samples progressively blurrier mips of the source to keep the bright
            // softboxes from aliasing, so the chain has to exist before it runs.
            environment.Bind(0);
            gl.GenerateMipmap(TextureTarget.TextureCubeMap);

            irradianceProgram.Use();
            irradianceProgram.Set("uEnvironment", 0);
            environment.Bind(0);
            RenderCube(gl, irradianceProgram, irradiance, IrradianceSize, 0);

            prefilterProgram.Use();
            prefilterProgram.Set("uEnvironment", 0);
            prefilterProgram.Set("uEnvironmentSize", EnvironmentSize);
            prefilterProgram.Set("uSampleCount", PrefilterSamples);
            environment.Bind(0);

            for (var level = 0; level < SpecularMipLevels; level++)
            {
                var size = Math.Max(1u, SpecularSize >> level);
                prefilterProgram.Set("uRoughness", level / (float)(SpecularMipLevels - 1));
                RenderCube(gl, prefilterProgram, specular, size, level);
            }
        }
        finally
        {
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
            gl.Viewport(previousViewport[0], previousViewport[1], (uint)previousViewport[2], (uint)previousViewport[3]);
            gl.BindVertexArray(0);
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteVertexArray(vao);
        }

        return new StudioEnvironment(gl, environment, irradiance, specular);
    }

    private static void RenderCube(GL gl, GlProgram program, GlTexture cube, uint size, int level)
    {
        gl.Viewport(0, 0, size, size);

        for (var face = 0; face < 6; face++)
        {
            gl.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.TextureCubeMapPositiveX + face,
                cube.Handle,
                level);

            var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
            {
                throw new InvalidOperationException(
                    $"Cubemap framebuffer incomplete while baking face {face} at mip {level}: {status}.");
            }

            program.Set("uFace", face);
            gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }

    private static int MipLevelsFor(uint size) => (int)Math.Log2(size) + 1;

    public void Dispose()
    {
        _environment.Dispose();
        _irradiance.Dispose();
        _specular.Dispose();
    }
}
