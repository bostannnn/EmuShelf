using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Ibl;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering;

/// <summary>
/// Draws one physical-media shell, lit by a baked studio environment, into a caller-supplied
/// framebuffer.
/// </summary>
/// <remarks>
/// Deliberately host-agnostic: it takes a <see cref="GL"/> whose context somebody else has made
/// current and a framebuffer id to draw into. That is what lets the same renderer serve Avalonia's
/// <c>OpenGlControlBase</c> in the app and a headless EGL context in the preview tool, so the thing
/// that ships is the thing that was looked at.
/// </remarks>
public sealed class MediaShellRenderer : IDisposable
{
    /// <summary>
    /// Linear scale the scene is rendered at before being filtered down to the target. The hero is
    /// a large, slowly rotating object against a flat backdrop, which is the worst case for stair
    /// stepping along its silhouette; 2x2 is the cheapest supersample that removes it.
    /// </summary>
    private const uint SupersampleFactor = 2;

    /// <summary>A long lens. Product photography avoids wide angles because perspective divergence
    /// on the near corner reads as a toy; ~22 degrees keeps the case's edges close to parallel.</summary>
    private const float FieldOfViewDegrees = 22f;

    // Just enough headroom that a shell turned to its widest diagonal still clears the control
    // edge. Measured: every shell lands ~336px tall in the shelf's 560x360 box, against the 300px
    // of the flat cover it replaces, so the focused item reads as the largest thing on screen.
    private const float FramingMargin = 1.07f;

    private readonly GL _gl;
    private readonly GlslDialect _dialect;
    private readonly int _majorVersion;
    private readonly int _minorVersion;
    private readonly GlProgram _program;
    private readonly GlTexture _whitePixel;
    private readonly GlTexture _flatNormal;
    private readonly Dictionary<MediaShell, ShellResources> _shells = [];

    private StudioEnvironment _environment;
    private Vector3 _accent;
    private GlTexture? _coverArt;
    private float _coverAspect = 1f;
    private uint _sceneFramebuffer;
    private uint _sceneColour;
    private uint _sceneDepth;
    private uint _sceneWidth;
    private uint _sceneHeight;

    private MediaShellRenderer(
        GL gl,
        GlslDialect dialect,
        int majorVersion,
        int minorVersion,
        GlProgram program,
        StudioEnvironment environment,
        Vector3 accent,
        GlTexture whitePixel,
        GlTexture flatNormal)
    {
        _gl = gl;
        _dialect = dialect;
        _majorVersion = majorVersion;
        _minorVersion = minorVersion;
        _program = program;
        _environment = environment;
        _accent = accent;
        _whitePixel = whitePixel;
        _flatNormal = flatNormal;
    }

    /// <param name="accent">The focused system's accent in linear space; see <see cref="ToLinear"/>.</param>
    public static MediaShellRenderer Create(
        GL gl,
        GlslDialect dialect,
        int majorVersion,
        int minorVersion,
        Vector3 accent)
    {
        var program = GlProgram.Create(
            gl,
            ShaderLibrary.Load("pbr.vert", dialect, majorVersion, minorVersion),
            ShaderLibrary.Load("pbr.frag", dialect, majorVersion, minorVersion),
            "pbr");

        var environment = StudioEnvironment.Bake(
            gl, dialect, majorVersion, minorVersion, accent, AccentMix, StudioIntensity);

        return new MediaShellRenderer(
            gl, dialect, majorVersion, minorVersion, program, environment, accent,
            GlTexture.Solid(gl, 255, 255, 255, 255),
            // (0.5, 0.5, 1) is a flat tangent-space normal.
            GlTexture.Solid(gl, 128, 128, 255, 255));
    }

    /// <summary>How much of the focused system's colour the studio's ambient takes on.</summary>
    private const float AccentMix = 0.55f;

    private const float StudioIntensity = 1f;

    /// <summary>
    /// Scales the studio's radiance before tone mapping.
    /// </summary>
    /// <remarks>
    /// Tuned against the light grey of a SNES cartridge, which is the shell most at risk of
    /// blowing out: it is a near-white diffuse surface with almost no specular to hide behind, so
    /// whatever exposure keeps its plastic reading as grey keeps the darker shells safe too.
    /// </remarks>
    private const float Exposure = 0.62f;

    /// <summary>Converts an sRGB colour (0..1 per channel) to the linear space the shader works in.</summary>
    public static Vector3 ToLinear(float r, float g, float b) =>
        new(ToLinear(r), ToLinear(g), ToLinear(b));

    private static float ToLinear(float channel) =>
        channel <= 0.04045f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);

    /// <summary>
    /// Re-bakes the studio for a new accent. Cheap enough to call on every focus change but far
    /// too expensive per frame, so callers should compare first.
    /// </summary>
    public void SetAccent(Vector3 linearAccent)
    {
        if (Vector3.DistanceSquared(linearAccent, _accent) < 1e-6f)
        {
            return;
        }

        _accent = linearAccent;
        var replacement = StudioEnvironment.Bake(
            _gl, _dialect, _majorVersion, _minorVersion, linearAccent, AccentMix, StudioIntensity);
        _environment.Dispose();
        _environment = replacement;
    }

    /// <summary>Replaces the artwork projected onto the shell's cover panel; null clears it.</summary>
    public void SetCoverArt(TextureImage? art)
    {
        _coverArt?.Dispose();
        _coverArt = art is null ? null : GlTexture.Upload(_gl, art, srgb: true);
        _coverAspect = art is null || art.Height == 0 ? 1f : art.Width / (float)art.Height;
    }

    /// <summary>Draws <paramref name="shell"/> into <paramref name="targetFramebuffer"/>.</summary>
    /// <param name="yaw">Rotation about the shell's up axis, in radians. 0 faces the viewer.</param>
    /// <param name="pitch">Rotation about the shell's right axis, in radians.</param>
    public void Render(
        MediaShell shell,
        uint targetFramebuffer,
        uint width,
        uint height,
        float yaw,
        float pitch)
    {
        if (width == 0 || height == 0)
        {
            return;
        }

        var resources = Resources(shell);
        EnsureSceneTarget(width * SupersampleFactor, height * SupersampleFactor);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);
        _gl.Viewport(0, 0, _sceneWidth, _sceneHeight);
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.Blend);
        // Every shipped shell is authored double-sided, and a keep case is genuinely open at the
        // hinge, so back faces are real geometry rather than something to cull. The shader flips
        // their normals instead.
        _gl.Disable(EnableCap.CullFace);

        DrawShell(resources, yaw, pitch);

        // Resolve the supersampled scene down onto whatever surface the host handed us.
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _sceneFramebuffer);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, targetFramebuffer);
        _gl.BlitFramebuffer(
            0, 0, (int)_sceneWidth, (int)_sceneHeight,
            0, 0, (int)width, (int)height,
            (uint)ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Linear);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
        _gl.Viewport(0, 0, width, height);
    }

    private void DrawShell(ShellResources resources, float yaw, float pitch)
    {
        var model = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw);
        var aspect = _sceneWidth / (float)_sceneHeight;
        var (view, projection, cameraPosition) = Camera(resources.Asset, aspect);

        _program.Use();
        _program.Set("uModel", model);
        _program.Set("uViewProjection", view * projection);
        // Rotation only, so the inverse transpose is the rotation itself.
        _program.SetMatrix3("uNormalMatrix", model);
        _program.Set("uCameraPosition", cameraPosition);
        _program.Set("uExposure", Exposure);

        _environment.Irradiance.Bind(3);
        _program.Set("uIrradianceMap", 3);
        _environment.Specular.Bind(4);
        _program.Set("uSpecularMap", 4);
        _program.Set("uSpecularMaxLod", _environment.SpecularMaxLod);

        BindPanels(resources);

        foreach (var mesh in resources.Meshes)
        {
            BindMaterial(resources, mesh.MaterialIndex);
            mesh.Draw();
        }
    }

    private void BindPanels(ShellResources resources)
    {
        var definition = resources.Definition;
        var panels = new List<ArtPanel> { definition.CoverPanel };
        panels.AddRange(definition.ExtraPanels);

        _program.Set("uPanelCount", panels.Count);
        _program.Set("uPanelRoughness", definition.PanelRoughness);
        _program.Set("uPanelFlattenNormal", definition.FlattenPanelNormal ? 1f : 0f);

        // The unlit sides of the sleeve get the system's colour rather than the model's own printed
        // artwork, which belongs to whichever game the model was scanned from.
        var tint = new Vector4(_accent * 0.85f, 1f);

        for (var i = 0; i < panels.Count; i++)
        {
            var placement = MediaShellCatalog.Place(panels[i], resources.Asset);
            _program.Set($"uPanelOrigin[{i}]", placement.Origin);
            _program.Set($"uPanelUEdge[{i}]", placement.UEdge);
            _program.Set($"uPanelVEdge[{i}]", placement.VEdge);
            _program.Set($"uPanelNormal[{i}]", placement.Normal);
            _program.Set($"uPanelTint[{i}]", tint);

            // Only the cover panel carries scraped art today; the back and spine are tinted until
            // the scraper's box-back and box-spine media are wired up.
            var hasArt = i == 0 && _coverArt is not null;
            _program.Set($"uPanelHasArt[{i}]", hasArt ? 1f : 0f);
            _program.Set($"uPanelArtScale[{i}]", ArtScale(definition.ArtFit, placement, _coverAspect));
            (hasArt ? _coverArt! : _whitePixel).Bind((uint)(5 + i));
            _program.Set($"uPanelArt{i}", 5 + i);
        }

        // Keep every declared sampler pointing at a complete texture even when the shell uses fewer
        // panels than the shader declares.
        for (var i = panels.Count; i < 3; i++)
        {
            _whitePixel.Bind((uint)(5 + i));
            _program.Set($"uPanelArt{i}", 5 + i);
        }
    }

    /// <summary>
    /// The centred sub-rectangle of the artwork a panel samples, chosen so the art keeps its shape.
    /// </summary>
    /// <remarks>
    /// The sub-rectangle's aspect in artwork pixels is <c>(scale.X / scale.Y) * artAspect</c>, and
    /// setting that equal to the panel's aspect is what removes the distortion; Cover then grows it
    /// until it fills the panel, Contain shrinks it until it fits inside.
    /// </remarks>
    private static Vector2 ArtScale(ArtFit fit, ArtPanelPlacement placement, float artAspect)
    {
        if (fit == ArtFit.Stretch || artAspect <= 0f)
        {
            return Vector2.One;
        }

        var panelAspect = placement.UEdge.Length() / MathF.Max(placement.VEdge.Length(), 1e-6f);
        var ratio = panelAspect / artAspect;

        return fit == ArtFit.Cover
            ? (ratio >= 1f ? new Vector2(1f, 1f / ratio) : new Vector2(ratio, 1f))
            : (ratio >= 1f ? new Vector2(ratio, 1f) : new Vector2(1f, 1f / ratio));
    }

    private void BindMaterial(ShellResources resources, int materialIndex)
    {
        var material = materialIndex >= 0 && materialIndex < resources.Asset.Materials.Count
            ? resources.Asset.Materials[materialIndex]
            : null;

        _program.Set("uBaseColorFactor", material?.BaseColorFactor ?? Vector4.One);
        _program.Set("uMetallicFactor", material?.MetallicFactor ?? 0f);
        _program.Set("uRoughnessFactor", material?.RoughnessFactor ?? 0.6f);

        BindMaterialTexture(resources, material?.BaseColorTexture ?? -1, 0, "uBaseColorMap", "uHasBaseColorMap", _whitePixel);
        BindMaterialTexture(resources, material?.MetallicRoughnessTexture ?? -1, 1, "uMetallicRoughnessMap", "uHasMetallicRoughnessMap", _whitePixel);
        BindMaterialTexture(resources, material?.NormalTexture ?? -1, 2, "uNormalMap", "uHasNormalMap", _flatNormal);
    }

    private void BindMaterialTexture(
        ShellResources resources,
        int textureIndex,
        uint unit,
        string samplerUniform,
        string presenceUniform,
        GlTexture fallback)
    {
        var present = textureIndex >= 0 && textureIndex < resources.Textures.Count;
        (present ? resources.Textures[textureIndex] : fallback).Bind(unit);
        _program.Set(samplerUniform, (int)unit);
        _program.Set(presenceUniform, present ? 1f : 0f);
    }

    /// <summary>
    /// Frames the shell so it fills the viewport whatever its proportions and however it is turned.
    /// </summary>
    /// <remarks>
    /// The fit radius is measured against the shell's diagonal rather than its face, so a SNES
    /// cartridge turned side-on does not grow past the edge of the control mid-spin — the hero
    /// holds a constant size while it rotates instead of breathing.
    /// </remarks>
    private static (Matrix4x4 View, Matrix4x4 Projection, Vector3 CameraPosition) Camera(
        ModelAsset asset, float aspect)
    {
        var half = asset.Size * 0.5f;
        var horizontalRadius = MathF.Sqrt((half.X * half.X) + (half.Z * half.Z));
        var verticalRadius = MathF.Sqrt((half.Y * half.Y) + (half.Z * half.Z));

        var fovY = FieldOfViewDegrees * MathF.PI / 180f;
        var tanY = MathF.Tan(fovY * 0.5f);
        var tanX = tanY * aspect;

        var distance = MathF.Max(verticalRadius / tanY, horizontalRadius / tanX) * FramingMargin;
        // Clear the object itself, so a chunky cartridge cannot poke through the near plane.
        distance += MathF.Max(half.Z, 0.05f);

        var cameraPosition = new Vector3(0f, 0f, distance);
        var view = Matrix4x4.CreateLookAt(cameraPosition, Vector3.Zero, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            fovY, aspect, MathF.Max(0.01f, distance - 2f), distance + 4f);

        return (view, projection, cameraPosition);
    }

    private ShellResources Resources(MediaShell shell)
    {
        if (_shells.TryGetValue(shell, out var existing))
        {
            return existing;
        }

        var definition = MediaShellCatalog.Definition(shell);
        var asset = MediaShellCatalog.Load(shell);

        var textures = asset.Textures
            .Select((image, index) => GlTexture.Upload(_gl, image, srgb: IsColourMap(asset, index)))
            .ToList();
        var meshes = asset.Meshes.Select(mesh => GlMesh.Upload(_gl, mesh)).ToList();

        var resources = new ShellResources(definition, asset, meshes, textures);
        _shells[shell] = resources;
        return resources;
    }

    // Base-colour maps hold sRGB-encoded colour; normal and metallic-roughness maps hold linear
    // data that must not be gamma-decoded on the way in.
    private static bool IsColourMap(ModelAsset asset, int textureIndex) =>
        asset.Materials.Any(material => material.BaseColorTexture == textureIndex);

    private void EnsureSceneTarget(uint width, uint height)
    {
        if (_sceneFramebuffer != 0 && _sceneWidth == width && _sceneHeight == height)
        {
            return;
        }

        DisposeSceneTarget();

        _sceneWidth = width;
        _sceneHeight = height;

        _sceneFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);

        _sceneColour = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _sceneColour);
        unsafe
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D, 0, InternalFormat.Rgba8, width, height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, (void*)0);
        }

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _sceneColour, 0);

        _sceneDepth = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _sceneDepth);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, width, height);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _sceneDepth);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Scene framebuffer incomplete: {status}.");
        }
    }

    private void DisposeSceneTarget()
    {
        if (_sceneFramebuffer == 0)
        {
            return;
        }

        _gl.DeleteFramebuffer(_sceneFramebuffer);
        _gl.DeleteTexture(_sceneColour);
        _gl.DeleteRenderbuffer(_sceneDepth);
        _sceneFramebuffer = 0;
        _sceneColour = 0;
        _sceneDepth = 0;
    }

    public void Dispose()
    {
        foreach (var resources in _shells.Values)
        {
            resources.Dispose();
        }

        _shells.Clear();
        DisposeSceneTarget();
        _coverArt?.Dispose();
        _whitePixel.Dispose();
        _flatNormal.Dispose();
        _environment.Dispose();
        _program.Dispose();
    }

    private sealed record ShellResources(
        MediaShellDefinition Definition,
        ModelAsset Asset,
        IReadOnlyList<GlMesh> Meshes,
        IReadOnlyList<GlTexture> Textures) : IDisposable
    {
        public void Dispose()
        {
            foreach (var mesh in Meshes)
            {
                mesh.Dispose();
            }

            foreach (var texture in Textures)
            {
                texture.Dispose();
            }
        }
    }
}
