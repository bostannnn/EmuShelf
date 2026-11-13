using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;
using EmuShelf.Rendering.Stage.Backdrops;
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
    /// <summary>The Stage's canvas on the 16:9 screen; the backdrops are drawn at this size.</summary>
    public const float WideCanvasWidth = 1920f;
    public const float CanvasHeight = 1080f;

    // 1920 on the 16:9 screen, 1240 once the screens are swapped (M45 H5, StageCanvasCommand).
    private float _canvasWidth = WideCanvasWidth;

    /// <summary>The canvas width the pictures are placed on.</summary>
    public float CanvasWidth => _canvasWidth;

    /// <summary>
    /// The framebuffer a frame is composed into: the surface's default one (0) on the device; the preview tool, whose
    /// surfaceless context has none, gives one of its own.
    /// </summary>
    public uint TargetFramebuffer { get; set; }

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

    private BackdropRenderer? _animator;

    /// <summary>
    /// Raised on the render thread with a backdrop's id when it starts: a system's first pick or the next in its
    /// rotation (not a resume, which the host already reports). The host logs it, so a device measurement can tell
    /// which backdrop was on screen.
    /// </summary>
    public event Action<string>? BackdropStarted;
    private IReadOnlyList<string>? _animatedIds;
    private readonly Random _random = new();
    private BackdropState? _backdrop;
    private BackdropState? _leavingBackdrop;
    private float _backdropFadeStart;

    // The backdrop's own clock (StageRest): it runs with the Stage's while someone navigates, then eases to a
    // stop a minute after the last navigation, and the Stage stops drawing until the next one.
    private float _backdropClock;
    private float _backdropClockReal = float.NaN;
    private float _backdropSpeed = 1f;
    private float _lastNavigation;

    // Battery saver or reduced motion (StageMotionCommand): the backdrop rests whatever the navigation, and starts at rest.
    private bool _holdStill;

    private Layer? _art;
    private List<Layer> _hero = [];
    private float _fadeStart = float.NegativeInfinity;
    private Layer? _fly;
    private bool _flyFromAbove;
    private float _flyStart;
    private FolderArtState2D? _folder;
    private FolderArtState _folderState;
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
            ShaderLibrary.Load("sprite.vert", dialect, majorVersion, minorVersion),
            ShaderLibrary.Load("stage.frag", dialect, majorVersion, minorVersion),
            "stage");
        _vao = gl.GenVertexArray();
    }

    /// <summary>
    /// The backdrop's step interval while it has something moving (an animated backdrop's 30 Hz step, or the era
    /// motif's drift); null when it is still.
    /// </summary>
    public float? NextFrameMilliseconds => IsBackdropResting
        ? null
        : _backdrop?.Animated is not null
            ? BackdropCatalog.StepMilliseconds
            : _backdrop?.Backdrop is { Tile: not null } or { Quad: not null }
                ? _backdrop.Backdrop.StepMilliseconds
                : null;

    /// <summary>The backdrop has eased to a stop (StageRest): nothing moves until the next navigation.</summary>
    public bool IsBackdropResting => _backdropSpeed <= 0f;

    /// <summary>Advances the backdrop's clock to the Stage's <paramref name="now"/> at the backdrop's current speed.</summary>
    private void AdvanceBackdropClock(float now)
    {
        // A long gap (resting, a paused activity) moves the clock no more than a frame or so.
        var delta = float.IsNaN(_backdropClockReal) ? 0f : Math.Clamp(now - _backdropClockReal, 0f, 100f);
        _backdropClockReal = now;
        _backdropSpeed = StageRest.Next(_backdropSpeed, delta, _holdStill || now - _lastNavigation >= StageRest.AfterMilliseconds);
        _backdropClock += delta * StageRest.Eased(_backdropSpeed);
    }

    /// <summary>
    /// The running backdrop and the rest state, for a rebuilt context (<see cref="StageBackdropResumeCommand"/>); null
    /// before any backdrop was set. Reads managed state only: the context this was drawn with may already be gone.
    /// </summary>
    public StageCommand? CaptureResume()
    {
        if (_backdrop is not { } state)
            return null;
        var showing = state.Animated;
        return new StageBackdropResumeCommand(showing?.Id, showing?.StartMilliseconds ?? 0f, showing?.ClockOffsetSeconds ?? 0f,
            _backdropClock, _backdropSpeed, _lastNavigation);
    }

    private void Resume(StageBackdropResumeCommand resume)
    {
        _backdropClock = resume.Clock;
        _backdropClockReal = float.NaN;
        _backdropSpeed = resume.Speed;
        _lastNavigation = resume.LastNavigation;
        if (resume.Id is not { } id || _backdrop is not { } state || _animatedIds is not { } ids || !ids.Contains(id, StringComparer.Ordinal)
            || _animator is null)
            return;
        // The replayed backdrop command opened on a pick of its own; the one that was showing takes its place, at the
        // same point of its motion and of its turn. (Its targets were lost with the context: a trail starts afresh.)
        if (state.Animated is { } showing && showing.Id == id && showing.StartMilliseconds == resume.StartMilliseconds)
            return;
        if (_animator.Start(id, resume.StartMilliseconds, resume.ClockOffsetSeconds) is not { } resumed)
            return;
        _animator.Stop(state.Animated);
        _animator.Stop(state.Previous);
        state.Previous = null;
        state.Animated = resumed;
    }

    public void Apply(StageCommand command, float now)
    {
        // Navigation wakes a resting backdrop; the medium's idle sway, the corner pills, the idle drift, the device's say on
        // motion and a rebuilt context carrying on do not.
        if (command is not (StageMediumPoseCommand or StageChromeCommand or StageCentreCommand { Drift: true } or
            StageBackdropCommand { Drift: true } or StageMotionCommand or StageBackdropResumeCommand))
            _lastNavigation = now;
        switch (command)
        {
            case StageMotionCommand motion:
                _holdStill = motion.HoldStill;
                // Told before the first frame (a replay, or the mode opening under battery saver): still from the start.
                if (_holdStill && float.IsNaN(_backdropClockReal))
                    _backdropSpeed = 0f;
                break;
            case StageBackdropResumeCommand resume:
                Resume(resume);
                break;
            case StageCanvasCommand canvas:
                _canvasWidth = canvas.Width;
                _mediumDirty = true;
                break;
            case StageBackdropCommand backdrop:
            {
                var ids = backdrop.Animated is { Count: > 0 } list ? list : null;
                if (_backdrop?.Animated is not null && ids is not null && _animatedIds is not null
                    && ids.SequenceEqual(_animatedIds) && backdrop.Backdrop is { } same)
                {
                    // A move within the same system: its backdrops keep running, only the pictured fallback changes.
                    var refreshed = Upload(same);
                    (refreshed.Animated, refreshed.Previous, refreshed.RotationStart) = (_backdrop.Animated, _backdrop.Previous, _backdrop.RotationStart);
                    _backdrop.Animated = _backdrop.Previous = null;
                    DeleteBackdrop(_backdrop);
                    _backdrop = refreshed;
                    break;
                }
                DeleteBackdrop(_leavingBackdrop);
                _leavingBackdrop = _backdrop;
                _backdrop = backdrop.Backdrop is { } next ? Upload(next) : null;
                _animatedIds = ids;
                if (_backdrop is not null && ids is not null)
                {
                    // A system opens on one of its backdrops at random, then takes turns (BackdropRotation).
                    _animator ??= new BackdropRenderer(_gl, _dialect, _major, _minor);
                    _backdrop.Animated = _animator.Start(BackdropRotation.Pick(ids, null, _random), _backdropClock);
                    if (_backdrop.Animated is { } started)
                        BackdropStarted?.Invoke(started.Id);
                }
                _backdropFadeStart = now;
                break;
            }
            case StageCentreCommand centre:
                Replace(ref _art, centre.Art, repeat: false);
                // The drift crossfades: the hero on screen stays under the arriving one while it fades in (or, when the next
                // step has no fan art, fades out under its centre).
                ReplaceHero(centre.Hero, keepLeaving: centre.Drift && centre.FadeIn);
                if (centre.FadeIn)
                {
                    _fadeStart = now;
                    _fadeMilliseconds = Math.Max(1f, centre.FadeMilliseconds);
                }
                if (!ReferenceEquals(_folder?.Art, centre.Folder))
                {
                    _folder?.Dispose();
                    _folderState = centre.FolderState;
                    _folder = centre.Folder is { } art ? new FolderArtState2D(_gl, art, centre.FolderState) : null;
                }
                Replace(ref _plate, centre.Plate, repeat: false);
                SetMedium(centre.Medium);
                if (centre.Pop)
                    _popStart = now;
                break;
            case StageLaunchCommand launch:
                Replace(ref _fly, launch.Cover, repeat: false);
                _flyStart = now;
                _flyFromAbove = launch.FromAbove;
                break;
            case StageFolderStateCommand folderState:
                _folderState = folderState.State;
                _folder?.SetState(folderState.State, now, folderState.Animate);
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
        // Animated backdrops draw their next 30 Hz step into their own small frames before the Stage is composed.
        AdvanceBackdropClock(now);
        var clock = _backdropClock;
        if (_backdrop is { } current)
        {
            Rotate(current, clock);
            UpdateAnimated(current, clock);
        }
        if (_leavingBackdrop is { } leaving)
            UpdateAnimated(leaving, clock);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, TargetFramebuffer);
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
                DrawBackdrop(_leavingBackdrop, clock, 1f);
                animating = true;
            }
        }
        if (_backdrop is not null)
            DrawBackdrop(_backdrop, clock, _leavingBackdrop is null ? 1f : fade);

        var sincePop = now - _popStart;
        var centreScale = StageCurves.PopScale(sincePop);
        var plateScale = StageCurves.PlateScale(sincePop);
        animating |= sincePop < Math.Max(StageCurves.PopMilliseconds, StageCurves.PlatePopMilliseconds);

        // A Title page fades in (340 ms) rather than popping: the hero, the centre and the plate together. The idle drift
        // fades slower, over the hero it replaces.
        var sinceFade = now - _fadeStart;
        var content = sinceFade >= _fadeMilliseconds ? 1f : EaseInOutCubic(Math.Max(0f, sinceFade) / _fadeMilliseconds);
        animating |= sinceFade < _fadeMilliseconds;
        if (_leavingHero.Count > 0)
        {
            if (content >= 1f)
            {
                DeleteLayers(_leavingHero);
            }
            else
            {
                // Under an arriving hero (opaque fan art) the old one stays whole; under anything else it fades out.
                var outgoing = _hero.Count > 0 ? 1f : 1f - content;
                foreach (var layer in _leavingHero)
                    DrawLayer(layer, outgoing, 1f, 0, 0);
            }
        }
        foreach (var layer in _hero)
            DrawLayer(layer, content, 1f, 0, 0);

        if (_medium is { } medium && _mediumTexture != 0)
        {
            // The FBO's first row is the bottom of the picture.
            DrawQuad(_mediumTexture, medium.X, medium.Y, medium.Width, medium.Height, content,
                centreScale, _canvasWidth / 2f, StageCurves.CentrePivotY, new Vector4(0, 1, 1, 0));
        }
        else if (_folder is { } folder)
        {
            folder.Draw(now, (texture, image, affine) => DrawQuad(texture, image.X, image.Y, image.Width, image.Height, content,
                centreScale, _canvasWidth / 2f, StageCurves.CentrePivotY, new Vector4(0, 0, 1, 1), affine));
            animating |= folder.IsAnimating(now);
        }
        else if (_art is { } art)
        {
            DrawLayer(art, content, centreScale, _canvasWidth / 2f, StageCurves.CentrePivotY);
        }
        if (_plate is { } plate)
            DrawLayer(plate, content, plateScale, _canvasWidth / 2f, StageCurves.PlatePivotY);
        if (_fly is { } fly)
            animating |= DrawLaunch(fly, now);
        foreach (var chrome in _chrome.Values)
            DrawLayer(chrome, 1f, 1f, 0, 0);

        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return animating;
    }

    private float _fadeMilliseconds = StageCentreCommand.HeroFadeMilliseconds;

    // The idle drift's outgoing hero, drawn under the arriving one until its fade ends.
    private List<Layer> _leavingHero = [];

    private static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - (MathF.Pow((-2f * t) + 2f, 3f) / 2f);

    private void ReplaceHero(IReadOnlyList<StageImage>? hero, bool keepLeaving = false)
    {
        if (hero is not null && hero.Count == _hero.Count && hero.Select((image, i) => ReferenceEquals(image, _hero[i].Image)).All(same => same))
            return;
        // At most one hero leaves at a time; anything that is not a drift step lets the old one go at once.
        DeleteLayers(_leavingHero);
        if (keepLeaving)
            _leavingHero = _hero;
        else
            DeleteLayers(_hero);
        _hero = hero?.Select(image => new Layer(image, SpriteTextures.Upload(_gl, image.Pixels, repeat: false))).ToList() ?? [];
    }

    private void DeleteLayers(List<Layer> layers)
    {
        foreach (var layer in layers)
            _gl.DeleteTexture(layer.Texture);
        layers.Clear();
    }

    // The launch, as the Avalonia Stage played it: after 140 ms the cover lands from 760 px below, growing
    // from 0.45× on the pop curve (520 ms) and fading in over its first quarter; once it has landed (660 ms
    // after the launch) it zooms to 1.7× and fades (460 ms).
    private bool DrawLaunch(Layer fly, float now)
    {
        var t = now - _flyStart;
        const float delay = 140f, land = 520f, zoom = 460f;
        float scale, opacity, y;
        if (t < delay)
            return true;
        if (t < delay + land)
        {
            var p = (t - delay) / land;
            var e = StageCurves.CubicBezier(0.22f, 1.1f, 0.36f, 1f, p);
            scale = 0.45f + (0.55f * e);
            y = (_flyFromAbove ? -760f : 760f) * (1f - e);
            opacity = Math.Clamp(p / 0.25f, 0f, 1f);
        }
        else if (t < delay + land + zoom)
        {
            var p = (t - delay - land) / zoom;
            var e = p * p * p;
            scale = 1f + (0.7f * e);
            y = 0f;
            opacity = 1f - e;
        }
        else
        {
            return false;
        }
        var image = fly.Image;
        DrawQuad(fly.Texture, image.X, image.Y + y, image.Width, image.Height, opacity, scale,
            image.X + (image.Width / 2), image.Y + y + (image.Height / 2), new Vector4(0, 0, 1, 1));
        return true;
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
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, TargetFramebuffer);
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

        /// <summary>The animated backdrop drawn instead of the layers, when the system has one that compiled.</summary>
        public BackdropInstance? Animated { get; set; }

        /// <summary>The system's previous backdrop while the next one fades in over it.</summary>
        public BackdropInstance? Previous { get; set; }

        public float RotationStart { get; set; }
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
        _animator?.Stop(state.Animated);
        _animator?.Stop(state.Previous);
        state.Animated = state.Previous = null;
        _gl.DeleteTexture(state.Under.Texture);
        if (state.Over is { } over)
            _gl.DeleteTexture(over.Texture);
        if (state.Tile != 0)
            _gl.DeleteTexture(state.Tile);
        if (state.Quad is { } quad)
            _gl.DeleteTexture(quad.Texture);
    }

    // The system's backdrops take turns: after its dwell the showing one hands over to another at random, which
    // fades in over it; once the fade is done the old one's targets are freed. These run on the backdrop's own
    // clock, so a resting backdrop also pauses its turn.
    private void Rotate(BackdropState state, float clock)
    {
        if (state.Animated is not { } showing || _animatedIds is not { } ids)
            return;
        if (state.Previous is { } previous)
        {
            if (clock - state.RotationStart >= BackdropRotation.FadeMilliseconds)
            {
                _animator!.Stop(previous);
                state.Previous = null;
            }
            return;
        }
        if (clock - showing.StartMilliseconds < BackdropRotation.DwellMilliseconds(ids.Count))
            return;
        if (_animator!.Start(BackdropRotation.Pick(ids, showing.Id, _random), clock) is not { } next)
            return;
        state.Previous = showing;
        state.Animated = next;
        state.RotationStart = clock;
        BackdropStarted?.Invoke(next.Id);
    }

    private void UpdateAnimated(BackdropState state, float clock)
    {
        if (state.Previous is { } previous)
            _animator!.Update(previous, clock);
        if (state.Animated is { } animated)
            _animator!.Update(animated, clock);
    }

    /// <summary>Where the mover is: the clock's place in its loop, held to the last step, in whole pixels.</summary>
    public static Vector2 DriftOffset(StageBackdrop backdrop, float clock)
    {
        if (backdrop.LoopMilliseconds <= 0)
            return Vector2.Zero;
        var stepped = backdrop.StepMilliseconds > 0 ? MathF.Floor(clock / backdrop.StepMilliseconds) * backdrop.StepMilliseconds : clock;
        var progress = stepped / backdrop.LoopMilliseconds % 1f;
        return new Vector2(MathF.Round(backdrop.Travel.X * progress), MathF.Round(backdrop.Travel.Y * progress));
    }

    private void DrawBackdrop(BackdropState state, float clock, float opacity)
    {
        if (state.Animated is { } animated)
        {
            if (state.Previous is { } previous)
            {
                // Within a system the next backdrop fades in over the last one (BackdropRotation.FadeMilliseconds).
                _animator!.Present(previous, opacity, BackdropCrop);
                _animator.Present(animated, opacity * StageCurves.EaseInOut((clock - state.RotationStart) / BackdropRotation.FadeMilliseconds), BackdropCrop);
            }
            else
            {
                _animator!.Present(animated, opacity, BackdropCrop);
            }
            // Back to the sprite program for the layers above.
            _program.Use();
            _gl.BindVertexArray(_vao);
            _gl.ActiveTexture(TextureUnit.Texture0);
            return;
        }
        // The pictured layers are 1920 wide: a narrower canvas shows their middle.
        var shift = BackdropShift;
        DrawLayer(state.Under, opacity, 1f, 0, 0, shift);
        var offset = DriftOffset(state.Backdrop, clock);
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
            DrawQuad(quad.Texture, image.X + offset.X + shift, image.Y + offset.Y, image.Width, image.Height, opacity, 1f, 0, 0,
                new Vector4(0, 0, 1, 1));
        }
        if (state.Over is { } over)
            DrawLayer(over, opacity, 1f, 0, 0, shift);
    }

    // On the narrow screen the 16:9 backdrops are centre-cropped: the pictured ones shifted left, the animated
    // ones cut in the output shader.
    private float BackdropShift => (_canvasWidth - WideCanvasWidth) / 2f;

    private float BackdropCrop => Math.Max(0f, 1f - (_canvasWidth / WideCanvasWidth));

    private void Replace(ref Layer? layer, StageImage? image, bool repeat)
    {
        if (layer is not null && ReferenceEquals(layer.Image, image))
            return;
        if (layer is not null)
            _gl.DeleteTexture(layer.Texture);
        layer = image is null ? null : new Layer(image, Upload(image.Pixels, repeat));
    }

    private void DrawLayer(Layer layer, float opacity, float scale, float pivotX, float pivotY, float shiftX = 0f)
    {
        var image = layer.Image;
        DrawQuad(layer.Texture, image.X + shiftX, image.Y, image.Width, image.Height, opacity, scale, pivotX, pivotY, new Vector4(0, 0, 1, 1));
    }

    private void DrawQuad(uint texture, float x, float y, float width, float height, float opacity, float scale,
        float pivotX, float pivotY, Vector4 uv, StageAffine? part = null)
    {
        if (opacity <= 0f)
            return;
        var local = part ?? StageAffine.Identity;
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _program.Set("uLinear", new Vector4(local.M11, local.M12, local.M21, local.M22));
        _program.Set("uTranslate", new Vector2(local.M31, local.M32));
        _program.Set("uOffset", Vector2.Zero);
        _program.Set("uGroup", new Vector3(1, 0, 0));
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
        _animator?.Dispose();
        _animator = null;
        Replace(ref _art, null, false);
        Replace(ref _plate, null, false);
        _folder?.Dispose();
        _folder = null;
        ReplaceHero(null);
        DeleteLayers(_leavingHero);
        Replace(ref _fly, null, false);
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
