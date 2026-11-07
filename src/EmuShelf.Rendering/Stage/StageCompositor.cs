using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Stage;

/// <summary>
/// The Handheld Stage as one GPU scene (M45 H4, option 5): textured quads for every 2D layer, the game's
/// medium rendered into its own texture, and all of the Stage's motion (the centre and plate pops, the
/// backdrop crossfade and drift) done here per frame. It runs on its own render thread with its own context,
/// so a frame costs a handful of draw calls and no UI-thread work; and during a pop the medium is only
/// scaled as a texture, never re-rendered. Pictures arrive pre-drawn (<see cref="StageImage"/>); nothing is
/// laid out or blurred here.
/// </summary>
/// <remarks>
/// Not thread-safe: the host applies commands and draws on its render thread. <paramref name="requestRender"/>
/// may be called from any thread (a shell finished loading).
/// </remarks>
public sealed class StageCompositor : INativeScene
{
    public const float CanvasWidth = 1920f;
    public const float CanvasHeight = 1080f;

    private const int SwizzleR = 0x8E42;
    private const int SwizzleB = 0x8E44;
    private const int GlRed = 0x1903;
    private const int GlBlue = 0x1905;

    private readonly GL _gl;
    private readonly GlslDialect _dialect;
    private readonly int _major;
    private readonly int _minor;
    private readonly Action _requestRender;
    private readonly GlProgram _program;
    private readonly uint _vao;

    private BackdropState? _backdrop;
    private BackdropState? _leavingBackdrop;
    private float _backdropFadeStart;

    private Layer? _art;
    private Layer? _plate;
    private float _popStart = float.NegativeInfinity;
    private readonly Dictionary<string, Layer> _chrome = new(StringComparer.Ordinal);

    private StageMedium? _medium;
    private MediaShellRenderer? _renderer;
    private Vector3 _uploadedAccent;
    private TextureImage? _cover;
    private bool _coverDirty;
    private float _yaw;
    private float _pitch;
    private volatile bool _mediumDirty;
    private uint _mediumFramebuffer;
    private uint _mediumTexture;
    private uint _mediumWidth;
    private uint _mediumHeight;

    public StageCompositor(GL gl, GlslDialect dialect, int majorVersion, int minorVersion, Action requestRender)
    {
        _gl = gl;
        _dialect = dialect;
        _major = majorVersion;
        _minor = minorVersion;
        _requestRender = requestRender;
        _program = GlProgram.Create(
            gl,
            ShaderLibrary.Load("stage.vert", dialect, majorVersion, minorVersion),
            ShaderLibrary.Load("stage.frag", dialect, majorVersion, minorVersion),
            "stage");
        _vao = gl.GenVertexArray();
    }

    /// <summary>The backdrop's step interval while it has something moving; null when it is still.</summary>
    public float? NextFrameMilliseconds => _backdrop?.Backdrop is { Tile: not null } or { Quad: not null }
        ? _backdrop.Backdrop.StepMilliseconds
        : null;

    public void Apply(StageCommand command, float now)
    {
        switch (command)
        {
            case StageBackdropCommand backdrop:
                DeleteBackdrop(_leavingBackdrop);
                _leavingBackdrop = _backdrop;
                _backdrop = backdrop.Backdrop is { } next ? Upload(next) : null;
                _backdropFadeStart = now;
                break;
            case StageCentreCommand centre:
                Replace(ref _art, centre.Art, repeat: false);
                Replace(ref _plate, centre.Plate, repeat: false);
                SetMedium(centre.Medium);
                if (centre.Pop)
                    _popStart = now;
                break;
            case StageMediumPoseCommand pose:
                if (pose.Yaw != _yaw || pose.Pitch != _pitch)
                {
                    _yaw = pose.Yaw;
                    _pitch = pose.Pitch;
                    _mediumDirty = true;
                }
                break;
            case StageMediumCoverCommand cover:
                if (!ReferenceEquals(cover.Cover, _cover))
                {
                    _cover = cover.Cover;
                    _coverDirty = true;
                    _mediumDirty = true;
                }
                break;
            case StageChromeCommand chrome:
                _chrome.TryGetValue(chrome.Key, out var old);
                var layer = old;
                Replace(ref layer, chrome.Image, repeat: false);
                if (layer is null)
                    _chrome.Remove(chrome.Key);
                else
                    _chrome[chrome.Key] = layer;
                break;
        }
    }

    /// <summary>Draws one frame into the bound default framebuffer. Returns whether something is still animating.</summary>
    public bool Draw(float now, int surfaceWidth, int surfaceHeight)
    {
        if (_medium is not null && (_mediumDirty || _coverDirty))
            RenderMedium(surfaceWidth / CanvasWidth);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)surfaceWidth, (uint)surfaceHeight);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.94f, 0.95f, 0.96f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);

        _program.Use();
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _program.Set("uTexture", 0);
        _program.Set("uCanvas", new Vector2(CanvasWidth, CanvasHeight));

        var animating = false;
        var fade = StageCurves.FadeIn(now - _backdropFadeStart);
        if (_leavingBackdrop is not null)
        {
            if (fade >= 1f)
            {
                DeleteBackdrop(_leavingBackdrop);
                _leavingBackdrop = null;
            }
            else
            {
                DrawBackdrop(_leavingBackdrop, now, 1f);
                animating = true;
            }
        }
        if (_backdrop is not null)
            DrawBackdrop(_backdrop, now, _leavingBackdrop is null ? 1f : fade);

        var sincePop = now - _popStart;
        var centreScale = StageCurves.PopScale(sincePop);
        var plateScale = StageCurves.PlateScale(sincePop);
        animating |= sincePop < Math.Max(StageCurves.PopMilliseconds, StageCurves.PlatePopMilliseconds);

        if (_medium is { } medium && _mediumTexture != 0)
        {
            // The FBO's first row is the bottom of the picture.
            DrawQuad(_mediumTexture, medium.X, medium.Y, medium.Width, medium.Height, 1f,
                centreScale, StageCurves.CentrePivotX, StageCurves.CentrePivotY, new Vector4(0, 1, 1, 0));
        }
        else if (_art is { } art)
        {
            DrawLayer(art, 1f, centreScale, StageCurves.CentrePivotX, StageCurves.CentrePivotY);
        }
        if (_plate is { } plate)
            DrawLayer(plate, 1f, plateScale, StageCurves.PlatePivotX, StageCurves.PlatePivotY);
        foreach (var chrome in _chrome.Values)
            DrawLayer(chrome, 1f, 1f, 0, 0);

        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return animating;
    }

    // ---- The medium ----

    private void SetMedium(StageMedium? medium)
    {
        if (medium == _medium)
            return;
        var shellChanged = medium?.Shell != _medium?.Shell;
        _medium = medium;
        _mediumDirty = true;
        if (medium is not null && shellChanged)
        {
            // Loading a shell is asynchronous; draw it once it is ready.
            _ = MediaShellCatalog.PrepareAsync(medium.Shell).ContinueWith(_ =>
            {
                _mediumDirty = true;
                _requestRender();
            }, TaskScheduler.Default);
        }
    }

    private void RenderMedium(float pixelsPerCanvas)
    {
        if (_medium is not { } medium)
            return;
        var width = (uint)Math.Max(1, Math.Round(medium.Width * pixelsPerCanvas));
        var height = (uint)Math.Max(1, Math.Round(medium.Height * pixelsPerCanvas));
        EnsureMediumTarget(width, height);
        _renderer ??= MediaShellRenderer.Create(_gl, _dialect, _major, _minor, medium.LinearAccent);
        if (medium.LinearAccent != _uploadedAccent)
        {
            _renderer.SetAccent(medium.LinearAccent);
            _uploadedAccent = medium.LinearAccent;
        }
        if (_coverDirty)
        {
            _renderer.SetCoverArt(_cover);
            _coverDirty = false;
        }
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _mediumFramebuffer);
        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        _renderer.Render(medium.Shell, _mediumFramebuffer, width, height, _yaw, _pitch);
        _mediumDirty = false;
    }

    private unsafe void EnsureMediumTarget(uint width, uint height)
    {
        if (_mediumFramebuffer != 0 && width == _mediumWidth && height == _mediumHeight)
            return;
        DeleteMediumTarget();
        _mediumTexture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _mediumTexture);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, width, height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _mediumFramebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _mediumFramebuffer);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _mediumTexture, 0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _mediumWidth = width;
        _mediumHeight = height;
    }

    private void DeleteMediumTarget()
    {
        if (_mediumFramebuffer != 0)
            _gl.DeleteFramebuffer(_mediumFramebuffer);
        if (_mediumTexture != 0)
            _gl.DeleteTexture(_mediumTexture);
        _mediumFramebuffer = _mediumTexture = 0;
    }

    // ---- Layers ----

    private sealed class Layer(StageImage image, uint texture)
    {
        public StageImage Image { get; } = image;
        public uint Texture { get; } = texture;
    }

    private sealed class BackdropState(StageBackdrop backdrop, Layer under, Layer? over, uint tile, Layer? quad)
    {
        public StageBackdrop Backdrop { get; } = backdrop;
        public Layer Under { get; } = under;
        public Layer? Over { get; } = over;
        public uint Tile { get; } = tile;
        public Layer? Quad { get; } = quad;
    }

    private BackdropState Upload(StageBackdrop backdrop) => new(
        backdrop,
        new Layer(backdrop.Under, Upload(backdrop.Under.Pixels, repeat: false)),
        backdrop.Over is { } over ? new Layer(over, Upload(over.Pixels, repeat: false)) : null,
        backdrop.Tile is { } tile ? Upload(tile, repeat: true) : 0,
        backdrop.Quad is { } quad ? new Layer(quad, Upload(quad.Pixels, repeat: false)) : null);

    private void DeleteBackdrop(BackdropState? state)
    {
        if (state is null)
            return;
        _gl.DeleteTexture(state.Under.Texture);
        if (state.Over is { } over)
            _gl.DeleteTexture(over.Texture);
        if (state.Tile != 0)
            _gl.DeleteTexture(state.Tile);
        if (state.Quad is { } quad)
            _gl.DeleteTexture(quad.Texture);
    }

    /// <summary>Where the mover is: the clock's place in its loop, held to the last step, in whole pixels.</summary>
    public static Vector2 DriftOffset(StageBackdrop backdrop, float now)
    {
        if (backdrop.LoopMilliseconds <= 0)
            return Vector2.Zero;
        var stepped = backdrop.StepMilliseconds > 0 ? MathF.Floor(now / backdrop.StepMilliseconds) * backdrop.StepMilliseconds : now;
        var progress = stepped / backdrop.LoopMilliseconds % 1f;
        return new Vector2(MathF.Round(backdrop.Travel.X * progress), MathF.Round(backdrop.Travel.Y * progress));
    }

    private void DrawBackdrop(BackdropState state, float now, float opacity)
    {
        DrawLayer(state.Under, opacity, 1f, 0, 0);
        var offset = DriftOffset(state.Backdrop, now);
        if (state.Tile != 0 && state.Backdrop.Tile is { } tile)
        {
            // The tile layer slides by the offset: canvas x shows the tile at x - offset.
            var uv = new Vector4(
                -offset.X / tile.Width, -offset.Y / tile.Height,
                (CanvasWidth - offset.X) / tile.Width, (CanvasHeight - offset.Y) / tile.Height);
            DrawQuad(state.Tile, 0, 0, CanvasWidth, CanvasHeight, opacity, 1f, 0, 0, uv);
        }
        if (state.Quad is { } quad)
        {
            var image = quad.Image;
            DrawQuad(quad.Texture, image.X + offset.X, image.Y + offset.Y, image.Width, image.Height, opacity, 1f, 0, 0,
                new Vector4(0, 0, 1, 1));
        }
        if (state.Over is { } over)
            DrawLayer(over, opacity, 1f, 0, 0);
    }

    private void Replace(ref Layer? layer, StageImage? image, bool repeat)
    {
        if (layer is not null && ReferenceEquals(layer.Image, image))
            return;
        if (layer is not null)
            _gl.DeleteTexture(layer.Texture);
        layer = image is null ? null : new Layer(image, Upload(image.Pixels, repeat));
    }

    private void DrawLayer(Layer layer, float opacity, float scale, float pivotX, float pivotY)
    {
        var image = layer.Image;
        DrawQuad(layer.Texture, image.X, image.Y, image.Width, image.Height, opacity, scale, pivotX, pivotY, new Vector4(0, 0, 1, 1));
    }

    private void DrawQuad(uint texture, float x, float y, float width, float height, float opacity, float scale,
        float pivotX, float pivotY, Vector4 uv)
    {
        if (opacity <= 0f)
            return;
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _program.Set("uRect", new Vector4(x, y, width, height));
        _program.Set("uScale", new Vector3(scale, pivotX, pivotY));
        _program.Set("uUv", uv);
        _program.Set("uOpacity", opacity);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    private unsafe uint Upload(StagePixels pixels, bool repeat)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        fixed (byte* data = pixels.Data)
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)pixels.Width, (uint)pixels.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
        if (pixels.IsBgra)
        {
            // Skia's BGRA bytes go up as they are; the texture reads them back in the right order.
            _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)SwizzleR, GlBlue);
            _gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)SwizzleB, GlRed);
        }
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        var wrap = (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        return texture;
    }

    public void Dispose()
    {
        DeleteBackdrop(_backdrop);
        DeleteBackdrop(_leavingBackdrop);
        _backdrop = _leavingBackdrop = null;
        Replace(ref _art, null, false);
        Replace(ref _plate, null, false);
        foreach (var layer in _chrome.Values)
            _gl.DeleteTexture(layer.Texture);
        _chrome.Clear();
        DeleteMediumTarget();
        _renderer?.Dispose();
        _renderer = null;
        _gl.DeleteVertexArray(_vao);
        _program.Dispose();
    }
}
