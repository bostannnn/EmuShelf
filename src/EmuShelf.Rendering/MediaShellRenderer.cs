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
    private const float MaximumSupersampleFactor = 2f;

    // Bound the off-screen scene independently of display resolution. At 1080p this selects 1.33x
    // (3.7 MP rather than 8.3 MP); at 1280x800 it still reaches 1.8x, where silhouette filtering is
    // most visible. A 4K output stays native instead of allocating and shading a 7680x4320 surface.
    private const uint MaximumSceneWidth = 2560;
    private const uint MaximumSceneHeight = 1440;
    private const uint SceneTargetBucket = 256;
    private const int SceneTargetShrinkDelayFrames = 30;

    /// <summary>A long lens. Product photography avoids wide angles because perspective divergence
    /// on the near corner reads as a toy; ~22 degrees keeps the case's edges close to parallel.</summary>
    private const float FieldOfViewDegrees = 22f;

    // Perspective makes the corner nearest the camera project larger than the diagonal radius at
    // the origin. Keep enough headroom for combined yaw and pitch so a controller-driven turn never
    // clips a cartridge's rim against the render surface.
    private const float FramingMargin = 1.30f;

    // Composition constants for the shared physical world. The floor stays just below the common
    // baseline; profile clearance then lifts small cartridges enough to open their cast shadow
    // without changing the measured size ratios or making cases hover.
    private const float ShelfBaselineY = -0.50f;
    private const float ShelfPlaneY = ShelfBaselineY - 0.008f;
    private const float FocusLift = 0.035f;

    /// <summary>
    /// Fraction of the viewport height the tallest medium in the library view fills.
    /// </summary>
    /// <remarks>
    /// This is the knob for "the cartridges are too small". The camera was previously fixed at a
    /// distance chosen for a 190mm keep case, so a SNES cartridge — barely 40% of that height —
    /// occupied under a third of the frame with the rest left empty above and below. That is worst
    /// on a Steam Deck, where the panel is short to begin with.
    ///
    /// It is deliberately framed against the tallest medium in the whole library view rather than
    /// the visible window, so relative physical scale still holds — a keep case beside a cartridge
    /// still dwarfs it — while the world does not zoom as items scroll past. Raising this fills the
    /// frame further and pushes the neighbouring media off its edges; that is the trade.
    /// </remarks>
    private const float ShelfFrameFill = 0.50f;

    /// <summary>How far the camera sits above the media band's centre, as a fraction of distance.</summary>
    private const float ShelfCameraElevation = 0.075f;

    /// <summary>How far the focused medium steps toward the camera.</summary>
    /// <remarks>
    /// Shared with the shadow pass on purpose. These were two separate literals, and the shadow
    /// pass used <see cref="FocusLift"/> — a vertical constant — as its depth, so the focused
    /// item's contact shadow trailed it and swam while focus interpolated.
    /// </remarks>
    private const float FocusDepth = 0.08f;

    /// <summary>
    /// Fraction of the studio exposure an item away from focus keeps.
    /// </summary>
    /// <remarks>
    /// Physical scale is data and focus must not change it, so size cannot say which medium is
    /// selected: the focused item is only about 2% larger from its depth step. A row of grey
    /// cartridges therefore needs a light falloff to read at couch distance, the way the flat
    /// shelf dimmed its neighbours.
    /// </remarks>
    private const float NeighbourExposure = 0.48f;

    // Each visible item receives its own self-shadow pass. 1024px resolves cartridge-scale moulding
    // more finely than the former 2048px map stretched across the whole seven-item row, avoids one
    // tall case blacking out a neighbour, and keeps the aggregate clear/sample cost reasonable.
    private const uint KeyShadowSize = 1024;

    private readonly GL _gl;
    private readonly GlProgram _program;
    private readonly GlProgram _shadowProgram;
    private readonly GlProgram _keyShadowProgram;
    private readonly GlMesh _receivingPlane;
    private readonly GlTexture _whitePixel;
    private readonly GlTexture _flatNormal;
    private readonly Dictionary<MediaShell, ShellResources> _shells = [];
    private readonly HashSet<MediaShell> _inspectionShellsWithoutPanels = [];

    private readonly StudioEnvironment _environment;
    private Vector3 _accent;
    private readonly Dictionary<long, PanelArtSet> _coverArt = [];
    private readonly List<ShelfDrawItem> _shelfDrawItems = new(7);
    private readonly List<ShadowFootprint> _shadowFootprints = new(7);
    private PanelArtSet? _activePanelArt;
    private Vector3 _drawAccent;
    private MaterialVariantAppearance _activeMaterialAppearance;
    private uint _sceneFramebuffer;
    private uint _sceneColour;
    private uint _sceneDepth;
    private uint _sceneWidth;
    private uint _sceneHeight;
    private uint _sceneCapacityWidth;
    private uint _sceneCapacityHeight;
    private int _sceneTargetUnderuseFrames;
    private uint _keyShadowFramebuffer;
    private uint _keyShadowDepth;
    private uint _keyShadowColour;

    private MediaShellRenderer(
        GL gl,
        GlProgram program,
        GlProgram shadowProgram,
        GlProgram keyShadowProgram,
        GlMesh receivingPlane,
        StudioEnvironment environment,
        Vector3 accent,
        GlTexture whitePixel,
        GlTexture flatNormal)
    {
        _gl = gl;
        _program = program;
        _shadowProgram = shadowProgram;
        _keyShadowProgram = keyShadowProgram;
        _receivingPlane = receivingPlane;
        _environment = environment;
        _accent = accent;
        _drawAccent = accent;
        _activeMaterialAppearance = MaterialVariantAppearance.Default;
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
        var shadowProgram = GlProgram.Create(
            gl,
            ShaderLibrary.Load("shadow.vert", dialect, majorVersion, minorVersion),
            ShaderLibrary.Load("shadow.frag", dialect, majorVersion, minorVersion),
            "soft contact shadow");
        var keyShadowProgram = GlProgram.Create(
            gl,
            ShaderLibrary.Load("key-shadow.vert", dialect, majorVersion, minorVersion),
            ShaderLibrary.Load("key-shadow.frag", dialect, majorVersion, minorVersion),
            "studio key shadow");
        var receivingPlane = GlMesh.Upload(gl, CreateReceivingPlane());

        // Bake the expensive convolution once. Platform colour is applied as a lightweight shader
        // tint to the dim room contribution, so changing systems never compiles shaders or renders
        // six cubemap faces in the middle of shelf navigation.
        var environment = StudioEnvironment.Bake(
            gl, dialect, majorVersion, minorVersion, Vector3.One, accentMix: 0f, intensity: StudioIntensity);

        return new MediaShellRenderer(
            gl, program, shadowProgram, keyShadowProgram, receivingPlane,
            environment, accent,
            GlTexture.Solid(gl, 255, 255, 255, 255),
            // (0.5, 0.5, 1) is a flat tangent-space normal.
            GlTexture.Solid(gl, 128, 128, 255, 255));
    }

    /// <summary>How much of the room contribution takes on the focused system's colour.</summary>
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

    // A high raking key, well left of the camera. The previous front-biased direction put almost
    // equal light across the neutral cartridge face, so its shadow map became visible mainly after
    // the player rotated it. This angle makes the label recess, grip rails, screws and lower slot
    // cast across the face in the basic shelf pose as well.
    private static readonly Vector3 KeyDirection = Vector3.Normalize(new(-0.78f, 0.56f, 0.29f));
    // The grazing angle contributes less NoL to a front face, so it needs more radiance than the
    // former camera-axis key while retaining the same warm-neutral product-light character.
    private static readonly Vector3 KeyRadiance = new(1.42f, 1.31f, 1.18f);

    /// <summary>
    /// TEMPORARY shading probe. Zero unless <c>EMUSHELF_SHADING_DEBUG</c> names a mode, so a normal
    /// run cannot reach it. Here to find out why the macOS desktop-GL path renders the SNES shell
    /// darker than the Windows ANGLE path; delete once that is understood.
    /// </summary>
    private static readonly int DebugMode =
        Environment.GetEnvironmentVariable("EMUSHELF_SHADING_DEBUG") switch
        {
            "albedo" => 1,
            "irradiance" => 2,
            "key-visibility" => 3,
            _ => 0,
        };

    /// <summary>Converts an sRGB colour (0..1 per channel) to the linear space the shader works in.</summary>
    public static Vector3 ToLinear(float r, float g, float b) =>
        new(ToLinear(r), ToLinear(g), ToLinear(b));

    private static float ToLinear(float channel) =>
        channel <= 0.04045f ? channel / 12.92f : MathF.Pow((channel + 0.055f) / 1.055f, 2.4f);

    /// <summary>Changes the platform tint without rebuilding the shared studio environment.</summary>
    public void SetAccent(Vector3 linearAccent) => _accent = linearAccent;

    /// <summary>Replaces the legacy single-object cover; null clears it.</summary>
    public void SetCoverArt(TextureImage? art) => SetPanelArt(0, 0, art);

    /// <summary>Uploads or replaces the artwork on a game's front panel.</summary>
    public void SetCoverArt(long key, TextureImage? art) => SetPanelArt(key, 0, art);

    /// <summary>
    /// Uploads or replaces the artwork on one of a game's panels — front, back or spine.
    /// </summary>
    /// <remarks>
    /// Faces are set independently because they arrive independently: a keep case's front comes
    /// from the cover the library already has decoded, while its back and spine are separate
    /// scraped files that may be absent, arrive late, or never arrive at all. A panel with no
    /// artwork falls back to the platform tint rather than blocking the others.
    /// </remarks>
    public void SetPanelArt(long key, int panelIndex, TextureImage? art)
    {
        if (panelIndex is < 0 or >= MaxPanels)
        {
            return;
        }

        if (!_coverArt.TryGetValue(key, out var set))
        {
            if (art is null)
            {
                return;
            }

            set = new PanelArtSet();
            _coverArt[key] = set;
        }

        set.Replace(
            panelIndex,
            art is null
                ? null
                : new CoverResource(
                    GlTexture.Upload(_gl, art, srgb: true),
                    art.Height == 0 ? 1f : art.Width / (float)art.Height));

        if (set.IsEmpty)
        {
            _coverArt.Remove(key);
        }
    }

    /// <summary>Releases every face of one game that moved outside the shelf's visible window.</summary>
    public void RemoveCoverArt(long key)
    {
        if (_coverArt.Remove(key, out var existing))
        {
            existing.Dispose();
        }
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

        if (!TryResources(shell, out var resources))
        {
            return;
        }
        var sceneSize = SceneSize(width, height);
        EnsureSceneTarget(sceneSize.Width, sceneSize.Height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);
        _gl.Viewport(0, 0, _sceneWidth, _sceneHeight);
        ClearVisibleSceneTarget();

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.Blend);
        // Every shipped shell is authored double-sided, and a keep case is genuinely open at the
        // hinge, so back faces are real geometry rather than something to cull. The shader flips
        // their normals instead.
        _gl.Disable(EnableCap.CullFace);

        _activePanelArt = _coverArt.GetValueOrDefault(0);
        _drawAccent = _accent;
        _activeMaterialAppearance = MaterialVariantAppearance.Default;
        var aspect = _sceneWidth / (float)_sceneHeight;
        var (view, projection, _) = Camera(resources.Asset, aspect);
        var model = Matrix4x4.CreateRotationX(pitch) * Matrix4x4.CreateRotationY(yaw);
        var keyViewProjection = DrawKeyShadow(resources, model);
        DrawHeroShadow(resources.Asset, yaw, view * projection);
        DrawShell(resources, model, keyViewProjection);

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

    /// <summary>
    /// Draws the bounded row of visible games through one fixed camera. Item scale comes from real
    /// dimensions, so focus changes move media through one world instead of reframing each object.
    /// </summary>
    /// <param name="mediaHeightInShelfUnits">Height of the tallest medium in the whole library
    /// view, not just the visible window, so the camera does not zoom as items scroll past.</param>
    public void RenderShelf(
        IReadOnlyList<MediaShelfRenderItem> items,
        float mediaHeightInShelfUnits,
        uint targetFramebuffer,
        uint width,
        uint height)
    {
        if (width == 0 || height == 0)
        {
            return;
        }

        var sceneSize = SceneSize(width, height);
        EnsureSceneTarget(sceneSize.Width, sceneSize.Height);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);
        _gl.Viewport(0, 0, _sceneWidth, _sceneHeight);
        ClearVisibleSceneTarget();
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.CullFace);

        var aspect = _sceneWidth / (float)_sceneHeight;
        var (view, projection, cameraPosition) = ShelfCamera(aspect, mediaHeightInShelfUnits);
        var viewProjection = view * projection;

        _shelfDrawItems.Clear();
        foreach (var item in items)
        {
            if (TryResources(item.Profile.Shell, out var resources))
            {
                _shelfDrawItems.Add(new ShelfDrawItem(item, resources, ShelfModel(item, resources.Asset)));
            }
        }
        DrawShelfShadows(_shelfDrawItems, viewProjection);

        foreach (var item in _shelfDrawItems)
        {
            // Visibility, not focus, is the quality boundary. Removing the depth pass as an item
            // left centre made its moulding flatten while it was still plainly on screen. The
            // scene is already bounded to seven submitted items; games outside that window incur
            // no pass at all, while every visible medium retains identical material depth.
            var keyViewProjection = DrawKeyShadow(item.Resources, item.Model);
            DrawShelfItem(
                item, viewProjection, cameraPosition, keyViewProjection, hasKeyShadow: true);
        }

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

    private void DrawShelfItem(
        ShelfDrawItem drawItem,
        Matrix4x4 viewProjection,
        Vector3 cameraPosition,
        Matrix4x4 keyViewProjection,
        bool hasKeyShadow)
    {
        var item = drawItem.Item;
        var resources = drawItem.Resources;
        var model = drawItem.Model;

        Matrix4x4.Invert(model, out var inverseModel);
        var normalMatrix = Matrix4x4.Transpose(inverseModel);

        _activePanelArt = _coverArt.GetValueOrDefault(item.Key);
        _drawAccent = item.Accent;
        _activeMaterialAppearance = MaterialVariantAppearance.For(item.Profile.MaterialVariant);

        _program.Use();
        _program.Set("uModel", model);
        _program.Set("uViewProjection", viewProjection);
        _program.SetMatrix3("uNormalMatrix", normalMatrix);
        _program.Set("uCameraPosition", cameraPosition);
        _program.Set("uExposure", Exposure * ExposureForFocus(item.FocusAmount));
        BindDirectLight(resources, keyViewProjection, hasKeyShadow);

        _environment.Irradiance.Bind(3);
        _program.Set("uIrradianceMap", 3);
        _environment.Specular.Bind(4);
        _program.Set("uSpecularMap", 4);
        _program.Set("uSpecularMaxLod", _environment.SpecularMaxLod);

        DrawResources(resources);
    }

    internal static Matrix4x4 ShelfModel(MediaShelfRenderItem item, ModelAsset asset)
    {
        var profile = item.Profile;
        var focus = Math.Clamp(item.FocusAmount, 0f, 1f);

        // Match all three canonical asset extents to measured dimensions. This is intentionally
        // non-uniform: temporary downloaded geometry is close to, but not guaranteed to equal, the
        // real package proportions recorded by the profile.
        var scale = new Vector3(
            profile.WidthInShelfUnits / MathF.Max(asset.Size.X, 1e-5f),
            profile.HeightInShelfUnits / MathF.Max(asset.Size.Y, 1e-5f),
            profile.DepthInShelfUnits / MathF.Max(asset.Size.Z, 1e-5f)) * item.LaunchScale;

        var centreY = ShelfBaselineY
            + profile.FloorClearanceInShelfUnits
            + (profile.HeightInShelfUnits * 0.5f)
            + (focus * FocusLift)
            + item.LaunchVerticalOffset;
        var centreZ = (focus * FocusDepth) + item.LaunchDepthOffset;
        return Matrix4x4.CreateScale(scale)
            * profile.CanonicalOrientation
            * Matrix4x4.CreateRotationX(item.Pitch)
            * Matrix4x4.CreateRotationY(item.Yaw)
            * Matrix4x4.CreateTranslation(item.CentreX, centreY, centreZ);
    }

    /// <summary>
    /// The studio exposure one item receives, falling off with its distance from focus.
    /// </summary>
    /// <remarks>
    /// Deliberately a light change rather than a material one: the shell keeps its colour and
    /// reflections, it simply stands further out of the key. Internal so the falloff can be pinned
    /// by a test without a GPU.
    /// </remarks>
    internal static float ExposureForFocus(float focusAmount) =>
        float.Lerp(NeighbourExposure, 1f, Math.Clamp(focusAmount, 0f, 1f));

    /// <summary>
    /// One product-photography camera for the whole world, pulled back only as far as the tallest
    /// medium in the library view requires.
    /// </summary>
    /// <remarks>
    /// The lens stays long and the distance does the framing, so the media keep the near-parallel
    /// edges product photography wants; only the empty space around them changes. Aiming at the
    /// centre of the media band rather than a fixed height is what removes the lopsided gap above
    /// the cartridges — the band, not the world origin, is what the viewer is looking at.
    /// </remarks>
    internal static (Matrix4x4 View, Matrix4x4 Projection, Vector3 CameraPosition) ShelfCamera(
        float aspect, float mediaHeightInShelfUnits)
    {
        var band = MathF.Max(mediaHeightInShelfUnits, 0.05f);
        var fovY = FieldOfViewDegrees * MathF.PI / 180f;
        var distance = band / ShelfFrameFill * 0.5f / MathF.Tan(fovY * 0.5f);

        var centreY = ShelfBaselineY + (band * 0.5f);
        var cameraPosition = new Vector3(0f, centreY + (distance * ShelfCameraElevation), distance);
        var view = Matrix4x4.CreateLookAt(cameraPosition, new Vector3(0f, centreY, 0f), Vector3.UnitY);
        // Near/far follow the distance now that it is no longer fixed; the old 0.1..12 pair would
        // clip the shelf's far neighbours once the camera moved in.
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            fovY, aspect, MathF.Max(0.05f, distance * 0.2f), distance + 8f);
        return (view, projection, cameraPosition);
    }

    internal static (uint Width, uint Height) SceneSize(uint outputWidth, uint outputHeight)
    {
        if (outputWidth == 0 || outputHeight == 0)
        {
            return (outputWidth, outputHeight);
        }

        var scale = MathF.Min(
            MaximumSupersampleFactor,
            MathF.Min(
                MaximumSceneWidth / (float)outputWidth,
                MaximumSceneHeight / (float)outputHeight));
        scale = MathF.Max(1f, scale);
        return (
            Math.Max(outputWidth, (uint)MathF.Round(outputWidth * scale)),
            Math.Max(outputHeight, (uint)MathF.Round(outputHeight * scale)));
    }

    private void DrawShell(
        ShellResources resources,
        Matrix4x4 model,
        Matrix4x4 keyViewProjection)
    {
        var aspect = _sceneWidth / (float)_sceneHeight;
        var (view, projection, cameraPosition) = Camera(resources.Asset, aspect);

        _program.Use();
        _program.Set("uModel", model);
        _program.Set("uViewProjection", view * projection);
        // Rotation only, so the inverse transpose is the rotation itself.
        _program.SetMatrix3("uNormalMatrix", model);
        _program.Set("uCameraPosition", cameraPosition);
        _program.Set("uExposure", Exposure);
        BindDirectLight(resources, keyViewProjection, hasKeyShadow: true);

        _environment.Irradiance.Bind(3);
        _program.Set("uIrradianceMap", 3);
        _environment.Specular.Bind(4);
        _program.Set("uSpecularMap", 4);
        _program.Set("uSpecularMaxLod", _environment.SpecularMaxLod);

        DrawResources(resources);
    }

    private void BindDirectLight(
        ShellResources resources,
        Matrix4x4 keyViewProjection,
        bool hasKeyShadow)
    {
        _program.Set("uDebugMode", DebugMode);
        _program.Set("uKeyDirection", KeyDirection);
        _program.Set("uKeyRadiance", KeyRadiance);
        _program.Set("uKeyLightViewProjection", keyViewProjection);
        _program.Set("uHasKeyShadow", hasKeyShadow ? 1f : 0f);
        _gl.ActiveTexture(TextureUnit.Texture8);
        _gl.BindTexture(TextureTarget.Texture2D, _keyShadowDepth);
        _program.Set("uKeyShadowMap", 8);
        _program.Set(
            "uDielectricReflectance",
            resources.Definition.DielectricReflectance * _activeMaterialAppearance.ReflectanceScale);
        _program.Set("uAmbientIntensity", resources.Definition.AmbientIntensity);
        _program.Set("uShadowFillOcclusion", resources.Definition.ShadowFillOcclusion);
        _program.Set("uCavityStrength", resources.Definition.CavityStrength);
        _program.Set("uNormalStrength", resources.Definition.NormalStrength);
        _program.Set("uAmbientAccent", _drawAccent);
        _program.Set("uAmbientAccentMix", AccentMix);
        _program.Set("uBodyTint", _activeMaterialAppearance.BodyTint);
        _program.Set("uBodyTintMix", _activeMaterialAppearance.BodyTintMix);
        _program.Set("uBodyAlbedoScale", resources.Definition.BodyAlbedoScale);
    }

    private void DrawResources(ShellResources resources)
    {
        BindPanels(resources);

        foreach (var mesh in resources.Meshes)
        {
            BindMaterial(resources, mesh.MaterialIndex);
            mesh.Draw();
        }
    }

    /// <summary>
    /// Renders one important physical medium from the studio key and returns the matching light
    /// transform for its immediately following colour pass.
    /// </summary>
    private Matrix4x4 DrawKeyShadow(ShellResources resources, Matrix4x4 model)
    {
        EnsureKeyShadowTarget();
        var lightViewProjection = KeyLightViewProjection(resources.Asset, model);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _keyShadowFramebuffer);
        _gl.Viewport(0, 0, KeyShadowSize, KeyShadowSize);
        _gl.ClearColor(1f, 1f, 1f, 1f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        // The source meshes are deliberately double-sided. Keeping both sides in the depth pass
        // is important for open case geometry and avoids losing thin cartridge lips.
        _gl.Disable(EnableCap.CullFace);
        // Push caster depth away from the light just enough to prevent a surface from shadowing
        // itself at the grazing studio angle. Receiver bias in the shader handles the remaining
        // slope component; using both avoids the large detached shadows a single huge bias creates.
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(1.5f, 3f);

        _keyShadowProgram.Use();
        _keyShadowProgram.Set("uLightViewProjection", lightViewProjection);
        _keyShadowProgram.Set("uModel", model);
        foreach (var mesh in resources.Meshes)
        {
            mesh.Draw();
        }
        _gl.Disable(EnableCap.PolygonOffsetFill);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);
        _gl.Viewport(0, 0, _sceneWidth, _sceneHeight);
        return lightViewProjection;
    }

    private static Matrix4x4 KeyLightViewProjection(ModelAsset asset, Matrix4x4 model)
    {
        var worldMin = new Vector3(float.PositiveInfinity);
        var worldMax = new Vector3(float.NegativeInfinity);
        Span<Vector3> corners = stackalloc Vector3[8];
        FillBoundsCorners(asset, corners);
        foreach (var corner in corners)
        {
            var world = Vector3.Transform(corner, model);
            worldMin = Vector3.Min(worldMin, world);
            worldMax = Vector3.Max(worldMax, world);
        }

        var centre = (worldMin + worldMax) * 0.5f;
        var radius = MathF.Max((worldMax - worldMin).Length() * 0.5f, 0.1f);
        var lightDistance = (radius * 3f) + 0.5f;
        var view = Matrix4x4.CreateLookAt(
            centre + (KeyDirection * lightDistance), centre, Vector3.UnitY);

        var halfWidth = 0.05f;
        var halfHeight = 0.05f;
        var nearest = float.PositiveInfinity;
        var farthest = 0f;
        foreach (var corner in corners)
        {
            var lightSpace = Vector3.Transform(Vector3.Transform(corner, model), view);
            halfWidth = MathF.Max(halfWidth, MathF.Abs(lightSpace.X));
            halfHeight = MathF.Max(halfHeight, MathF.Abs(lightSpace.Y));
            var depth = -lightSpace.Z;
            nearest = MathF.Min(nearest, depth);
            farthest = MathF.Max(farthest, depth);
        }

        const float xyMargin = 1.08f;
        var depthMargin = MathF.Max(radius * 0.08f, 0.02f);
        var projection = Matrix4x4.CreateOrthographic(
            halfWidth * 2f * xyMargin,
            halfHeight * 2f * xyMargin,
            MathF.Max(0.01f, nearest - depthMargin),
            farthest + depthMargin);
        return view * projection;
    }

    private static void FillBoundsCorners(ModelAsset asset, Span<Vector3> corners)
    {
        var min = asset.BoundsMin;
        var max = asset.BoundsMax;
        corners[0] = new Vector3(min.X, min.Y, min.Z);
        corners[1] = new Vector3(max.X, min.Y, min.Z);
        corners[2] = new Vector3(min.X, max.Y, min.Z);
        corners[3] = new Vector3(max.X, max.Y, min.Z);
        corners[4] = new Vector3(min.X, min.Y, max.Z);
        corners[5] = new Vector3(max.X, min.Y, max.Z);
        corners[6] = new Vector3(min.X, max.Y, max.Z);
        corners[7] = new Vector3(max.X, max.Y, max.Z);
    }

    private void BindNoPanels()
    {
        _program.Set("uPanelCount", 0);
        for (var index = 0; index < MaxPanels; index++)
        {
            _whitePixel.Bind((uint)(5 + index));
            _program.Set($"uPanelArt{index}", 5 + index);
        }
    }

    private void DrawHeroShadow(ModelAsset asset, float yaw, Matrix4x4 viewProjection)
    {
        var half = asset.Size * 0.5f;
        var cos = MathF.Abs(MathF.Cos(yaw));
        var sin = MathF.Abs(MathF.Sin(yaw));
        var radiusX = (half.X * cos) + (half.Z * sin);
        var radiusZ = (half.Z * cos) + (half.X * sin);
        DrawShadows(
            [new ShadowFootprint(Vector2.Zero, new Vector2(radiusX, MathF.Max(radiusZ, 0.06f)), 0.92f)],
            viewProjection,
            planeY: -half.Y - 0.018f);
    }

    private void DrawShelfShadows(
        IReadOnlyList<ShelfDrawItem> items,
        Matrix4x4 viewProjection)
    {
        _shadowFootprints.Clear();
        var count = Math.Min(items.Count, 7);
        for (var index = 0; index < count; index++)
        {
            var item = items[index].Item;
            var profile = item.Profile;
            var halfWidth = profile.WidthInShelfUnits * 0.5f;
            var halfDepth = profile.DepthInShelfUnits * 0.5f;
            var cos = MathF.Abs(MathF.Cos(item.Yaw));
            var sin = MathF.Abs(MathF.Sin(item.Yaw));
            var radiusX = (halfWidth * cos) + (halfDepth * sin);
            var radiusZ = (halfDepth * cos) + (halfWidth * sin);
            var focus = Math.Clamp(item.FocusAmount, 0f, 1f);
            var lift = profile.FloorClearanceInShelfUnits + (focus * FocusLift) + item.LaunchVerticalOffset;
            var positiveLift = Math.Clamp(lift, 0f, 0.14f);
            var shadowExpansion = 1f + (positiveLift * 2f);
            var insertionVisibility = Math.Clamp(1f + (item.LaunchVerticalOffset * 3f), 0f, 1f);
            _shadowFootprints.Add(new ShadowFootprint(
                // The plane's second axis is world Z, so this must follow the item's depth step.
                new Vector2(item.CentreX, (focus * FocusDepth) + item.LaunchDepthOffset),
                new Vector2(
                    MathF.Max(radiusX, 0.05f) * shadowExpansion * item.LaunchScale,
                    MathF.Max(radiusZ, 0.045f) * shadowExpansion * item.LaunchScale),
                (1f - (focus * 0.14f))
                * (1f - (positiveLift * 1.5f))
                * insertionVisibility));
        }

        DrawShadows(_shadowFootprints, viewProjection, planeY: ShelfPlaneY);
    }

    private void DrawShadows(
        IReadOnlyList<ShadowFootprint> footprints,
        Matrix4x4 viewProjection,
        float planeY)
    {
        if (footprints.Count == 0)
        {
            return;
        }

        var minX = footprints.Min(footprint => footprint.Centre.X - (footprint.Radius.X * 2.2f));
        var maxX = footprints.Max(footprint => footprint.Centre.X + (footprint.Radius.X * 2.2f));
        var planeCentre = new Vector2((minX + maxX) * 0.5f, -0.03f);
        var planeExtent = new Vector2(MathF.Max((maxX - minX) * 0.5f, 1f), 1.1f);

        _shadowProgram.Use();
        _shadowProgram.Set("uViewProjection", viewProjection);
        _shadowProgram.Set("uPlaneY", planeY);
        _shadowProgram.Set("uPlaneCentre", planeCentre);
        _shadowProgram.Set("uPlaneExtent", planeExtent);
        _shadowProgram.Set("uShadowCount", Math.Min(footprints.Count, 7));
        for (var index = 0; index < Math.Min(footprints.Count, 7); index++)
        {
            var footprint = footprints[index];
            _shadowProgram.Set(
                $"uShadowFootprint[{index}]",
                new Vector4(footprint.Centre, footprint.Radius.X, footprint.Radius.Y));
            _shadowProgram.Set($"uShadowOpacity[{index}]", footprint.Opacity);
        }

        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        _receivingPlane.Draw();
        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
    }

    private static MeshGeometry CreateReceivingPlane() => new()
    {
        Vertices =
        [
            -1f, 0f, -1f, 0f, 1f, 0f, 0f, 0f,
             1f, 0f, -1f, 0f, 1f, 0f, 1f, 0f,
             1f, 0f,  1f, 0f, 1f, 0f, 1f, 1f,
            -1f, 0f,  1f, 0f, 1f, 0f, 0f, 1f,
        ],
        Indices = [0, 1, 2, 0, 2, 3],
        MaterialIndex = -1,
    };

    private void BindPanels(ShellResources resources)
    {
        var definition = resources.Definition;
        if (_inspectionShellsWithoutPanels.Contains(definition.Shell))
        {
            BindNoPanels();
            return;
        }

        var panels = resources.Panels;

        _program.Set("uPanelCount", panels.Count);
        _program.Set("uPanelRoughness", definition.PanelRoughness);
        _program.Set("uPanelFlattenNormal", definition.FlattenPanelNormal ? 1f : 0f);

        // The unlit sides of the sleeve get the system's colour rather than the model's own printed
        // artwork, which belongs to whichever game the model was scanned from.
        var tint = new Vector4(_drawAccent * 0.85f, 1f);

        for (var i = 0; i < panels.Count; i++)
        {
            var panel = panels[i];
            var placement = panel.Placement;
            _program.Set($"uPanelOrigin[{i}]", placement.Origin);
            _program.Set($"uPanelUEdge[{i}]", placement.UEdge);
            _program.Set($"uPanelVEdge[{i}]", placement.VEdge);
            _program.Set($"uPanelNormal[{i}]", placement.Normal);
            _program.Set($"uPanelTint[{i}]", tint);
            _program.Set(
                $"uPanelAspect[{i}]",
                placement.UEdge.Length() / MathF.Max(placement.VEdge.Length(), 1e-6f));
            _program.Set($"uPanelCornerRadius[{i}]", panel.Panel.CornerRadius);
            _program.Set($"uPanelCutCorner[{i}]", panel.Panel.CutCorner);
            // Negative is the shader's "unbounded", which is what a panel with no depth limit —
            // a cartridge label sunk into moulding — needs.
            _program.Set($"uPanelMaxDepth[{i}]", panel.Panel.MaxSurfaceDepth ?? -1f);

            // Each face is independent: a case can wear a scraped front with no back yet, and
            // the missing one takes the platform tint instead of blanking the others.
            var art = _activePanelArt?.Get(i);
            _program.Set($"uPanelHasArt[{i}]", art is not null ? 1f : 0f);
            _program.Set($"uPanelArtScale[{i}]", ArtScale(
                panel.Panel.ArtFit, placement, art?.Aspect ?? 1f));
            (art?.Texture ?? _whitePixel).Bind((uint)(5 + i));
            _program.Set($"uPanelArt{i}", 5 + i);
        }

        // Keep every declared sampler pointing at a complete texture even when the shell uses fewer
        // panels than the shader declares.
        for (var i = panels.Count; i < MaxPanels; i++)
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
        _program.Set(
            "uRoughnessFactor",
            (material?.RoughnessFactor ?? 0.6f)
                * resources.Definition.BodyRoughnessScale
                * _activeMaterialAppearance.RoughnessScale);

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

        var cameraPosition = new Vector3(0f, distance * 0.065f, distance);
        var view = Matrix4x4.CreateLookAt(cameraPosition, new Vector3(0f, -0.04f, 0f), Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            fovY, aspect, MathF.Max(0.01f, distance - 2f), distance + 4f);

        return (view, projection, cameraPosition);
    }

    private bool TryResources(MediaShell shell, out ShellResources resources)
    {
        if (_shells.TryGetValue(shell, out var existing))
        {
            resources = existing;
            return true;
        }

        if (!MediaShellCatalog.TryGetPrepared(shell, out var asset))
        {
            resources = null!;
            return false;
        }

        var definition = MediaShellCatalog.Definition(shell);
        resources = UploadResources(definition, asset);
        _shells[shell] = resources;
        return true;
    }

    /// <summary>
    /// Replaces one catalogue shell for the headless sourcing preview. Kept internal and exposed
    /// only to the preview assembly so candidate files can pass through the exact shipping shader
    /// without becoming application assets before their license and geometry clear review.
    /// </summary>
    internal void SetInspectionShell(MediaShell shell, ModelAsset asset, bool suppressArtworkPanels = false)
    {
        if (_shells.Remove(shell, out var existing))
        {
            existing.Dispose();
        }

        _shells[shell] = UploadResources(MediaShellCatalog.Definition(shell), asset);
        if (suppressArtworkPanels)
        {
            _inspectionShellsWithoutPanels.Add(shell);
        }
        else
        {
            _inspectionShellsWithoutPanels.Remove(shell);
        }
    }

    private ShellResources UploadResources(MediaShellDefinition definition, ModelAsset asset)
    {

        var textures = asset.Textures
            .Select((image, index) => GlTexture.Upload(_gl, image, srgb: IsColourMap(asset, index)))
            .ToList();
        var meshes = asset.Meshes.Select(mesh => GlMesh.Upload(_gl, mesh)).ToList();

        var panels = new ArtPanelBinding[1 + definition.ExtraPanels.Count];
        panels[0] = new ArtPanelBinding(definition.CoverPanel, MediaShellCatalog.Place(definition.CoverPanel, asset));
        for (var index = 0; index < definition.ExtraPanels.Count; index++)
        {
            var panel = definition.ExtraPanels[index];
            panels[index + 1] = new ArtPanelBinding(panel, MediaShellCatalog.Place(panel, asset));
        }

        var resources = new ShellResources(definition, asset, meshes, textures, panels);
        return resources;
    }

    // Base-colour maps hold sRGB-encoded colour; normal and metallic-roughness maps hold linear
    // data that must not be gamma-decoded on the way in.
    private static bool IsColourMap(ModelAsset asset, int textureIndex) =>
        asset.Materials.Any(material => material.BaseColorTexture == textureIndex);

    private void EnsureSceneTarget(uint width, uint height)
    {
        _sceneWidth = width;
        _sceneHeight = height;

        var desiredWidth = RoundUp(width, SceneTargetBucket);
        var desiredHeight = RoundUp(height, SceneTargetBucket);
        var fits = _sceneFramebuffer != 0
            && width <= _sceneCapacityWidth
            && height <= _sceneCapacityHeight;
        var shouldShrink = fits
            && (IsExcessivelyOversized(_sceneCapacityWidth, desiredWidth)
                || IsExcessivelyOversized(_sceneCapacityHeight, desiredHeight));

        if (fits && !shouldShrink)
        {
            _sceneTargetUnderuseFrames = 0;
            return;
        }

        if (shouldShrink && ++_sceneTargetUnderuseFrames < SceneTargetShrinkDelayFrames)
        {
            return;
        }

        var capacityWidth = shouldShrink
            ? desiredWidth
            : Math.Max(_sceneCapacityWidth, desiredWidth);
        var capacityHeight = shouldShrink
            ? desiredHeight
            : Math.Max(_sceneCapacityHeight, desiredHeight);
        _sceneTargetUnderuseFrames = 0;
        DisposeSceneTarget();

        _sceneCapacityWidth = capacityWidth;
        _sceneCapacityHeight = capacityHeight;

        _sceneFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _sceneFramebuffer);

        _sceneColour = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _sceneColour);
        unsafe
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D, 0, InternalFormat.Rgba8, capacityWidth, capacityHeight, 0,
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
            RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, capacityWidth, capacityHeight);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, _sceneDepth);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Scene framebuffer incomplete: {status}.");
        }
    }

    /// <summary>
    /// Clears only the logical scene rectangle. The allocation is bucketed and may temporarily be
    /// larger after a resize, so clearing its entire capacity would waste fill bandwidth.
    /// </summary>
    private void ClearVisibleSceneTarget()
    {
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(0, 0, _sceneWidth, _sceneHeight);
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        _gl.Disable(EnableCap.ScissorTest);
    }

    internal static uint RoundUp(uint value, uint bucket)
    {
        if (value == 0 || bucket == 0)
        {
            return value;
        }

        return checked(((value + bucket - 1) / bucket) * bucket);
    }

    internal static bool IsExcessivelyOversized(uint capacity, uint desired) =>
        desired > 0 && capacity / (double)desired >= 1.5d;

    private void EnsureKeyShadowTarget()
    {
        if (_keyShadowFramebuffer != 0)
        {
            return;
        }

        _keyShadowFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _keyShadowFramebuffer);

        _keyShadowDepth = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _keyShadowDepth);
        unsafe
        {
            _gl.TexImage2D(
                TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
                KeyShadowSize, KeyShadowSize, 0,
                PixelFormat.DepthComponent, PixelType.UnsignedInt, (void*)0);
        }

        // Comparisons are done explicitly in GLSL so the same PCF path works on desktop GL and
        // ANGLE/GLES. Nearest source samples plus the shader's 3x3 kernel keep results deterministic.
        _gl.TexParameter(
            TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(
            TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(
            TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(
            TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.FramebufferTexture2D(
            FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, _keyShadowDepth, 0);

        // A colour renderbuffer costs no sampling bandwidth and avoids the depth-only framebuffer
        // draw/read-buffer differences between core OpenGL, macOS GL 3.2, and ANGLE's GLES path.
        _keyShadowColour = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _keyShadowColour);
        _gl.RenderbufferStorage(
            RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, KeyShadowSize, KeyShadowSize);
        _gl.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, _keyShadowColour);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Key shadow framebuffer incomplete: {status}.");
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
        _sceneCapacityWidth = 0;
        _sceneCapacityHeight = 0;
    }

    private void DisposeKeyShadowTarget()
    {
        if (_keyShadowFramebuffer == 0)
        {
            return;
        }

        _gl.DeleteFramebuffer(_keyShadowFramebuffer);
        _gl.DeleteTexture(_keyShadowDepth);
        _gl.DeleteRenderbuffer(_keyShadowColour);
        _keyShadowFramebuffer = 0;
        _keyShadowDepth = 0;
        _keyShadowColour = 0;
    }

    public void Dispose()
    {
        foreach (var resources in _shells.Values)
        {
            resources.Dispose();
        }

        _shells.Clear();
        _inspectionShellsWithoutPanels.Clear();
        DisposeSceneTarget();
        DisposeKeyShadowTarget();
        foreach (var cover in _coverArt.Values)
        {
            cover.Dispose();
        }
        _coverArt.Clear();
        _whitePixel.Dispose();
        _flatNormal.Dispose();
        _environment.Dispose();
        _receivingPlane.Dispose();
        _keyShadowProgram.Dispose();
        _shadowProgram.Dispose();
        _program.Dispose();
    }

    private sealed record ShellResources(
        MediaShellDefinition Definition,
        ModelAsset Asset,
        IReadOnlyList<GlMesh> Meshes,
        IReadOnlyList<GlTexture> Textures,
        IReadOnlyList<ArtPanelBinding> Panels) : IDisposable
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

    private sealed record CoverResource(GlTexture Texture, float Aspect) : IDisposable
    {
        public void Dispose() => Texture.Dispose();
    }

    /// <summary>
    /// Lightweight per-platform finish layered over shared geometry. Geometry remains cached once
    /// per shell while a PS2, PS3 and Wii case can still differ in colour, gloss and reflectance.
    /// Values are linear-space product-lighting corrections, not replacement artwork.
    /// </summary>
    internal readonly record struct MaterialVariantAppearance(
        Vector3 BodyTint,
        float BodyTintMix,
        float RoughnessScale,
        float ReflectanceScale)
    {
        public static MaterialVariantAppearance Default { get; } =
            new(Vector3.One, 0f, 1f, 1f);

        public static MaterialVariantAppearance For(string variant) => variant switch
        {
            "ps2-black" => new(new Vector3(0.018f, 0.020f, 0.025f), 0.82f, 1.06f, 1f),
            // A DS card's shell is black, and this model's is near-white — which only became
            // obvious once the label stopped covering the whole face and revealed the band along
            // the bottom that carries the release code.
            // Mixed far harder than the case finishes: those tint an already-dark model, while this
            // shell's plastic is near-white, and at 0.86 the surviving fraction of it still read as
            // mid grey rather than black.
            "ds-black" => new(new Vector3(0.021f, 0.022f, 0.026f), 0.965f, 1.02f, 1f),
            "gamecube-black" => new(new Vector3(0.022f, 0.024f, 0.030f), 0.80f, 1.04f, 1f),
            "ps3-clear" => new(new Vector3(0.38f, 0.46f, 0.58f), 0.28f, 0.76f, 1.35f),
            "wii-white" => new(new Vector3(0.86f, 0.88f, 0.92f), 0.78f, 0.92f, 1.08f),
            _ => Default,
        };
    }

    /// <summary>
    /// Panels the fragment shader declares; keep in step with MAX_PANELS in pbr.frag.
    /// </summary>
    /// <remarks>
    /// Public because a host has to size its own per-face bookkeeping to match, and a second
    /// literal three on the other side of the assembly boundary is exactly the kind of pair that
    /// drifts apart unnoticed.
    /// </remarks>
    public const int MaxPanels = 3;

    /// <summary>
    /// One game's uploaded faces. Held as a set rather than three dictionary entries so evicting a
    /// game that scrolled out of the window cannot leave a stray face behind on the GPU.
    /// </summary>
    private sealed class PanelArtSet : IDisposable
    {
        private readonly CoverResource?[] _panels = new CoverResource?[MaxPanels];

        public bool IsEmpty => _panels.All(panel => panel is null);

        public CoverResource? Get(int index) =>
            index >= 0 && index < _panels.Length ? _panels[index] : null;

        public void Replace(int index, CoverResource? resource)
        {
            _panels[index]?.Dispose();
            _panels[index] = resource;
        }

        public void Dispose()
        {
            for (var index = 0; index < _panels.Length; index++)
            {
                _panels[index]?.Dispose();
                _panels[index] = null;
            }
        }
    }

    private readonly record struct ArtPanelBinding(ArtPanel Panel, ArtPanelPlacement Placement);

    private readonly record struct ShelfDrawItem(
        MediaShelfRenderItem Item,
        ShellResources Resources,
        Matrix4x4 Model);

    private readonly record struct ShadowFootprint(Vector2 Centre, Vector2 Radius, float Opacity);
}
