using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;
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
public sealed partial class StageCompositor : INativeScene, IProbedScene
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
    private Random _random = new();

    /// <summary>
    /// Makes the backdrops' picks repeatable (the frame probe's A/B runs, M47 B6): the same presses then show the same backdrops, so two
    /// runs compare. Off the probe the picks stay random.
    /// </summary>
    public void SeedBackdrops(int seed) => _random = new Random(seed);
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

    // The Big Type look (M47 B4): the ground and dark ink, each picture's zone, and the veils (white masks drawn in a zone's
    // veil colour under the centre). Null ink: today's look, where nothing is tinted and the screen clears to its grey.
    private StageInkCommand? _ink;
    private readonly Dictionary<string, StageInkZone> _chromeZones = new(StringComparer.Ordinal);

    // Big Type's rolling text (M47 B5): each piece's characters, and a roll under way (the picture it leaves and when it began).
    private readonly Dictionary<string, StageText> _chromeTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (Layer Old, StageText Text, float Start)> _rolls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Layer> _veils = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StageInkZone> _veilZones = new(StringComparer.Ordinal);

    // Big Type's motion (M47 B6), on with StageMotionCommand.BigType: the name's letters; the last press (the letters' hop); the
    // backdrops still showing beneath the one wiping in, oldest first; the circles the next quads are cut to (surface pixels, zero is
    // off); this frame's canvas on the surface; and the dark ink the top backdrop was last drawn with.
    private bool _bigType;
    private BigTypeStageLayer? _letters;
    private float _lastInput = float.NaN;
    private readonly List<BackdropState> _beneath = [];
    private Vector4 _clip0;
    private Vector4 _clip1;
    private Vector4 _clip2;
    private (int X, int Y, int Width, int Height, float Scale) _fit;
    private Vector3? _drawnDeep;
    private string? _probeTag;

    /// <summary>At most this many backdrops stay beneath a wipe; a further system change lets the oldest go.</summary>
    internal const int MaxBeneath = 2;

    /// <inheritdoc />
    public string? ProbeTag => _probeTag;

    // The probe's breakdown of a frame (IProbedScene): the time each part took, only while the host asks for it.
    private System.Text.StringBuilder? _sections;
    private long _sectionMark;
    private int _sectionQuads;

    /// <inheritdoc />
    public bool ProbeSections { get; set; }

    /// <inheritdoc />
    public string? LastSections { get; private set; }

    private void Section(string name)
    {
        if (_sections is null)
            return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        _sections.Append(name).Append('=')
            .Append(((now - _sectionMark) * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        if (_sectionQuads > 0)
            _sections.Append('/').Append(_sectionQuads).Append('q');
        _sections.Append(' ');
        _sectionMark = now;
        _sectionQuads = 0;
    }

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
    public float? NextFrameMilliseconds => Earliest(IsBackdropResting
        ? null
        : _backdrop?.Animated is not null
            ? BackdropCatalog.StepMilliseconds
            : _backdrop?.Backdrop is { Tile: not null } or { Quad: not null }
                ? _backdrop.Backdrop.StepMilliseconds
                : null, UntilLettersHop);

    private static float? Earliest(float? a, float? b) => a is { } x && b is { } y ? Math.Min(x, y) : a ?? b;

    // Big Type (M47 B6): the name's letters hop with the Launcher's tiles, so a Stage with nothing else moving still wakes for a hop.
    private float? UntilLettersHop => _bigType && _letters?.Name is not null && !float.IsNaN(_lastInput)
        ? BigTypeMotion.UntilNextHop(_backdropClockReal - _lastInput, _holdStill)
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
        if (_animator.Start(id, resume.StartMilliseconds, resume.ClockOffsetSeconds, state.Look) is not { } resumed)
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
        // Big Type's name and presses (M47 B6) are text and moments, as the corner pieces are: they do not wake it either.
        if (command is not (StageMediumPoseCommand or StageChromeCommand or StageCentreCommand { Drift: true } or
            StageBackdropCommand { Drift: true } or StageMotionCommand or StageBackdropResumeCommand or StageNameCommand or
            StageInputCommand))
            _lastNavigation = now;
        switch (command)
        {
            case StageMotionCommand motion:
                _holdStill = motion.HoldStill;
                _bigType = motion.BigType;
                // Told before the first frame (a replay, or the mode opening under battery saver): still from the start.
                if (_holdStill && float.IsNaN(_backdropClockReal))
                    _backdropSpeed = 0f;
                break;
            case StageNameCommand name:
                (_letters ??= new BigTypeStageLayer(_gl)).Apply(name, now);
                break;
            case StageInputCommand:
                _lastInput = now;
                break;
            case StageBackdropCommand backdrop when _bigType:
                ApplyBigTypeBackdrop(backdrop, now);
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
            {
                var layers = chrome.Veil ? _veils : _chrome;
                var zones = chrome.Veil ? _veilZones : _chromeZones;
                layers.TryGetValue(chrome.Key, out var old);
                var layer = old;
                if (_rolls.Remove(chrome.Key, out var unfinished))
                    _gl.DeleteTexture(unfinished.Old.Texture);
                // Big Type's text rolls the characters that changed (M47 B5): the old picture stays until they have rolled over. Within a
                // run of presses (quick taps; M47 B11) it swaps in place, so a roll never restarts on a roll.
                if (_ink is not null && old is not null && chrome.Image is { } next && !ReferenceEquals(old.Image, next) && !chrome.Quick &&
                    _chromeTexts.TryGetValue(chrome.Key, out var before) && chrome.Text is { } after && BigTypeMotion.Rolls(before.Text, after.Text, _holdStill))
                {
                    _rolls[chrome.Key] = (old, before, now);
                    layer = new Layer(next, Upload(next.Pixels, repeat: false));
                }
                else
                {
                    Replace(ref layer, chrome.Image, repeat: false);
                }
                if (chrome.Text is { } text && layer is not null)
                    _chromeTexts[chrome.Key] = text;
                else
                    _chromeTexts.Remove(chrome.Key);
                if (layer is null)
                {
                    layers.Remove(chrome.Key);
                    zones.Remove(chrome.Key);
                }
                else
                {
                    layers[chrome.Key] = layer;
                    zones[chrome.Key] = chrome.Zone;
                }
                break;
            }
            case StageTakeoverCommand takeover:
                ApplyTakeover(takeover, now);
                break;
            case StageTakeoverStatusCommand status:
                ApplyTakeoverStatus(status, now);
                break;
            case StageShakeCommand shake:
                _shakes[shake.Key] = now;
                break;
            case StageInkCommand ink:
                _ink = ink;
                // The dark ink belongs to what is on top; a backdrop that has gone beneath a wipe keeps the one it was drawn with.
                if (_bigType && _backdrop is { } top)
                    top.Deep = ink.Deep;
                break;
        }
    }

    // ---- Big Type's backdrops (M47 B6): the wipe, and the start-up cost kept off the frame that shows it ----

    /// <summary>
    /// A system change under Big Type: the new backdrop goes on top and wipes in through a growing circle once it is ready, with the
    /// ones it covers running beneath until it has covered them (the mock's <c>setBackdrop</c>). It is prepared over the frames after
    /// the command (started, then its first step drawn; its pictured layers go up only if they are ever drawn), never on the frame
    /// the press lands on, and its circle starts growing <see cref="BigTypeMotion.WipeDelay"/> after the command or once it is ready,
    /// whichever is later. The first backdrop, with nothing beneath it, is prepared at once and simply shows.
    /// </summary>
    private void ApplyBigTypeBackdrop(StageBackdropCommand command, float now)
    {
        var ids = command.Animated is { Count: > 0 } list ? list : null;
        if (_backdrop is { } top && top.Ids is not null && ids is not null && ids.SequenceEqual(top.Ids) && command.Backdrop is { } same)
        {
            // A move within the same system: its backdrops keep running; only the pictured fallback changes, and goes up if drawn.
            DeleteLayers(top);
            top.Backdrop = same;
            return;
        }
        if (command.Backdrop is not { } next)
        {
            foreach (var state in _beneath)
                DeleteBackdrop(state);
            _beneath.Clear();
            DeleteBackdrop(_backdrop);
            _backdrop = null;
            _animatedIds = null;
            return;
        }
        var arriving = new BackdropState(next)
        {
            Step = 0,
            Ids = ids,
            Queued = now,
            Deep = _ink?.Deep ?? Vector3.One,
            // The middle of the canvas this Stage draws on (the narrow one's own when the screens are swapped, M47 B11).
            WipeOriginX = command.WipeOriginX ?? (_canvasWidth / 2f),
            // M47 B13: Big Type's own animations, sizes and recolour toward the colour the command carries.
            Look = new BackdropLook(true, command.Ground ?? Vector3.One),
        };
        if (_backdrop is { } showing && showing.Ready)
        {
            // What was on screen keeps the ink it was drawn with (an ink command earlier in this batch belongs to the arrival).
            if (_drawnDeep is { } drawn)
                showing.Deep = drawn;
            _beneath.Add(showing);
            while (_beneath.Count > MaxBeneath)
            {
                DeleteBackdrop(_beneath[0]);
                _beneath.RemoveAt(0);
            }
        }
        else
        {
            // One replaced before it showed is let go; with nothing beneath (the first backdrop, a replay) the arrival simply shows.
            DeleteBackdrop(_backdrop);
            if (_beneath.Count == 0)
                arriving.WipeStart = float.NegativeInfinity;
        }
        _backdrop = arriving;
        _animatedIds = ids;
        if (_beneath.Count == 0)
        {
            // Nobody is watching a wipe: prepare it now, so a rebuilt context's resume and the first frame find it running.
            Prepare(arriving);
            Prepare(arriving);
        }
    }

    /// <summary>One step of getting a backdrop ready: started (or its first layer uploaded), then its first step drawn (or the rest).</summary>
    private void Prepare(BackdropState state)
    {
        if (state.Ready)
            return;
        if (state.Step == 0)
        {
            state.Step = 1;
            if (state.Ids is { } ids)
            {
                _animator ??= new BackdropRenderer(_gl, _dialect, _major, _minor);
                state.Animated = _animator.Start(BackdropRotation.Pick(ids, null, _random), _backdropClock, look: state.Look);
                if (state.Animated is { } started)
                {
                    BackdropStarted?.Invoke(started.Id);
                    return;
                }
            }
            // No animated backdrop (Recent, your folders, one that did not compile): its pictures are what shows.
            state.Under ??= new Layer(state.Backdrop.Under, Upload(state.Backdrop.Under.Pixels, repeat: false));
            return;
        }
        if (state.Animated is { } animated)
            _animator!.Update(animated, _backdropClock);
        else
            UploadLayers(state);
        state.Step = 2;
    }

    /// <summary>Puts a backdrop's pictured layers on the GPU (Big Type does this only when they are to be drawn).</summary>
    private void UploadLayers(BackdropState state)
    {
        var backdrop = state.Backdrop;
        state.Under ??= new Layer(backdrop.Under, Upload(backdrop.Under.Pixels, repeat: false));
        if (state.Over is null && backdrop.Over is { } over)
            state.Over = new Layer(over, Upload(over.Pixels, repeat: false));
        if (state.Tile == 0 && backdrop.Tile is { } tile)
            state.Tile = Upload(tile, repeat: true);
        if (state.Quad is null && backdrop.Quad is { } quad)
            state.Quad = new Layer(quad, Upload(quad.Pixels, repeat: false));
    }

    /// <summary>Takes a backdrop's pictured layers off the GPU (they go up again if they are drawn).</summary>
    private void DeleteLayers(BackdropState state)
    {
        if (state.Under is { } under)
            _gl.DeleteTexture(under.Texture);
        if (state.Over is { } over)
            _gl.DeleteTexture(over.Texture);
        if (state.Tile != 0)
            _gl.DeleteTexture(state.Tile);
        if (state.Quad is { } quad)
            _gl.DeleteTexture(quad.Texture);
        state.Under = state.Over = state.Quad = null;
        state.Tile = 0;
    }

    /// <summary>The Big Type backdrops from the bottom up: those beneath a wipe, then the one on top.</summary>
    private IEnumerable<BackdropState> Stack()
    {
        foreach (var state in _beneath)
            yield return state;
        if (_backdrop is { } top)
            yield return top;
    }

    /// <summary>
    /// The wipe's circle for a backdrop on the canvas (radius 0 before it grows); null for one that covers the screen (the bottom of
    /// the stack, or a wipe that has finished).
    /// </summary>
    private float? WipeRadius(BackdropState state, float now)
    {
        if (float.IsNegativeInfinity(state.WipeStart))
            return null;
        if (float.IsNaN(state.WipeStart) || now < state.WipeStart)
            return 0f;
        var radius = BigTypeMotion.WipeRadiusAt(now - state.WipeStart);
        return radius >= BigTypeMotion.WipeRadius ? null : radius;
    }

    /// <summary>A circle on the canvas as the shader takes it: in the surface's pixels from the bottom-left, keeping what is inside (or outside).</summary>
    private Vector4 Circle(float x, float y, float radius, bool inside) =>
        new(_fit.X + (x * _fit.Scale), _fit.Y + ((CanvasHeight - y) * _fit.Scale), radius * _fit.Scale, inside ? 1f : -1f);

    private Vector4 WipeCircle(BackdropState state, float radius, bool inside) =>
        Circle(state.WipeOriginX, CanvasHeight + BigTypeMotion.WipeBelow, radius, inside);

    /// <summary>
    /// Advances the Big Type backdrops for this frame: one preparation step for the one on top (none on the frame its command came on),
    /// its wipe's start once it is ready, and the ones beneath let go once a wipe above them has covered the screen. Returns whether a
    /// wipe (or a preparation) is under way.
    /// </summary>
    private bool AdvanceBigTypeBackdrops(float now)
    {
        var busy = false;
        if (_backdrop is { Ready: false } top)
        {
            busy = true;
            if (top.Queued < now || top.Step > 0)
                Prepare(top);
            if (top.Ready && float.IsNaN(top.WipeStart))
            {
                // Held still the new backdrop cuts in once it is ready (the mock's reduced motion); otherwise its circle grows from
                // the wipe's delay after the press, or now if getting it ready took longer.
                top.WipeStart = _holdStill ? float.NegativeInfinity : Math.Max(now, top.Queued + BigTypeMotion.WipeDelay);
            }
        }
        // The highest backdrop that covers the screen hides everything beneath it.
        var stack = Stack().ToList();
        for (var i = stack.Count - 1; i > 0; i--)
        {
            if (!stack[i].Ready || WipeRadius(stack[i], now) is not null)
                continue;
            for (var j = 0; j < i; j++)
                DeleteBackdrop(stack[j]);
            _beneath.RemoveRange(0, Math.Min(i, _beneath.Count));
            break;
        }
        return busy || _beneath.Count > 0;
    }

    /// <summary>Draws one frame into the bound default framebuffer. Returns whether something is still animating.</summary>
    public bool Draw(float now, int surfaceWidth, int surfaceHeight)
    {
        if (ProbeSections)
        {
            (_sections ??= new System.Text.StringBuilder()).Clear();
            _sectionMark = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        else
        {
            _sections = null;
        }
        var fit = FitCanvas(surfaceWidth, surfaceHeight);
        _fit = fit;
        if (_medium is not null && (_mediumDirty || _coverDirty))
            RenderMedium(fit.Scale);
        Section("medium");
        // Animated backdrops draw their next 30 Hz step into their own small frames before the Stage is composed.
        AdvanceBackdropClock(now);
        var clock = _backdropClock;
        var wiping = false;
        if (_bigType)
        {
            // Big Type (M47 B6): the backdrops beneath a wipe keep running until it has covered them.
            wiping = AdvanceBigTypeBackdrops(now);
            if (_backdrop is { Ready: true } top)
                Rotate(top, clock);
            foreach (var state in Stack())
            {
                if (state.Ready)
                    UpdateAnimated(state, clock);
            }
        }
        else
        {
            if (_backdrop is { } current)
            {
                Rotate(current, clock);
                UpdateAnimated(current, clock);
            }
            if (_leavingBackdrop is { } leaving)
                UpdateAnimated(leaving, clock);
        }

        Section("backdrops");
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, TargetFramebuffer);
        _gl.Viewport(0, 0, (uint)surfaceWidth, (uint)surfaceHeight);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        // Big Type clears to the focused item's colour (seen where no backdrop draws); today's look to its light grey.
        if (_ink is { } ground)
            _gl.ClearColor(ground.Ground.X, ground.Ground.Y, ground.Ground.Z, 1f);
        else
            _gl.ClearColor(0.94f, 0.95f, 0.96f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        // The canvas fills the surface, or (Big Type's 16:9 canvas on the narrow screen, M47 B4) sits in it letterboxed on the
        // ground, as the mock scales its whole top screen down when the screens swap.
        _gl.Viewport(fit.X, fit.Y, (uint)fit.Width, (uint)fit.Height);

        _program.Use();
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _program.Set("uTexture", 0);
        _program.Set("uCanvas", new Vector2(CanvasWidth, CanvasHeight));

        var animating = wiping;
        var fade = 1f;
        IReadOnlyList<InkPass> passes;
        if (_bigType)
        {
            passes = DrawBigTypeBackdrops(now, clock);
        }
        else
        {
            fade = StageCurves.FadeIn(now - _backdropFadeStart);
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
            var showing = ShowingBackdrops(clock, fade);
            var deep = _ink?.Deep ?? Vector3.One;
            passes = [new InkPass(default, default, default, zone => ZoneInk(zone, showing, deep, IsNarrowCanvas))];
        }

        Section("present");
        // The veils lie on the backdrop, under everything else (the mock's .shade comes before the case). During a wipe each backdrop's
        // veils are drawn only where it shows.
        if (_ink is not null)
        {
            foreach (var (key, veil) in _veils)
            {
                var zone = _veilZones.GetValueOrDefault(key);
                foreach (var pass in PassesFor(zone, passes))
                {
                    Clip(pass);
                    var colours = pass.Ink(zone);
                    DrawLayer(veil, colours.VeilStrength, 1f, 0, 0, tint: colours.Veil);
                }
            }
            Clip(null);
        }

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
            // The FBO's first row is the bottom of the picture. It pops about its own middle: the canvas's middle in today's
            // look, where the medium's box is centred on it; its corner's middle in Big Type's.
            // Big Type's launch bumps the case as the tile arrives (M47 B8).
            var (bump, rise) = CaseBump(now, ref animating);
            DrawQuad(_mediumTexture, medium.X, medium.Y - rise, medium.Width, medium.Height, content,
                centreScale * bump, medium.X + (medium.Width / 2f), medium.Y - rise + (medium.Height / 2f), new Vector4(0, 1, 1, 0));
        }
        else if (_folder is { } folder)
        {
            folder.Draw(now, (texture, image, affine) => DrawQuad(texture, image.X, image.Y, image.Width, image.Height, content,
                centreScale, _canvasWidth / 2f, StageCurves.CentrePivotY, new Vector4(0, 0, 1, 1), affine));
            animating |= folder.IsAnimating(now);
        }
        else if (_art is { } art)
        {
            // Big Type's centre is a flat cover in its corner (or a card): it pops about its own middle.
            if (_ink is not null)
            {
                var (bump, rise) = CaseBump(now, ref animating);
                DrawQuad(art.Texture, art.Image.X, art.Image.Y - rise, art.Image.Width, art.Image.Height, content, centreScale * bump,
                    art.Image.X + (art.Image.Width / 2f), art.Image.Y - rise + (art.Image.Height / 2f), new Vector4(0, 0, 1, 1));
            }
            else
                DrawLayer(art, content, centreScale, _canvasWidth / 2f, StageCurves.CentrePivotY);
        }
        if (_plate is { } plate)
            DrawLayer(plate, content, plateScale, _canvasWidth / 2f, StageCurves.PlatePivotY);
        if (_fly is { } fly)
            animating |= DrawLaunch(fly, now);

        Section("centre");
        // Big Type's name (M47 B6): its letters arrive, leave and hop, in the left zone's ink.
        var lettersMoving = false;
        if (_letters is { } letters)
        {
            lettersMoving = letters.Advance(now, _holdStill);
            var hop = LettersHop(now, letters);
            animating |= lettersMoving || hop is not null;
            if (!letters.IsEmpty)
            {
                foreach (var pass in PassesFor(StageInkZone.Left, passes))
                {
                    Clip(pass);
                    Vector3? tint = _ink is not null ? pass.Ink(StageInkZone.Left).Ink : null;
                    if (UseBatch)
                    {
                        letters.Draw(now, _holdStill, hop, (texture, x, y, width, height, opacity, uv, affine) =>
                            BatchQuad(texture, x, y, width, height, opacity, uv, affine, tint, default));
                        FlushBatch();
                    }
                    else
                    {
                        letters.Draw(now, _holdStill, hop, (texture, x, y, width, height, opacity, uv, affine) =>
                            DrawQuad(texture, x, y, width, height, opacity, 1f, 0f, 0f, uv, affine, tint));
                    }
                }
                Clip(null);
            }
        }

        Section("letters");
        // The rolls under way this frame (M47 B5); a finished one lets its old picture go.
        Dictionary<string, IReadOnlyList<BigTypeRollPiece>>? rolling = null;
        foreach (var (key, roll) in _rolls.Count == 0 ? [] : _rolls.ToList())
        {
            if (_chrome.TryGetValue(key, out var layer) && _chromeTexts.TryGetValue(key, out var text) &&
                BigTypeRollDraw.Pieces(roll.Text, text, layer.Image.X, layer.Image.Y, layer.Image.Width, layer.Image.Height, now - roll.Start)
                    is { } pieces)
            {
                (rolling ??= new(StringComparer.Ordinal))[key] = pieces;
                animating = true;
                continue;
            }
            _gl.DeleteTexture(roll.Old.Texture);
            _rolls.Remove(key);
        }
        foreach (var (key, chrome) in _chrome)
        {
            var zone = _ink is not null ? _chromeZones.GetValueOrDefault(key) : StageInkZone.None;
            foreach (var pass in PassesFor(zone, passes))
            {
                Clip(pass);
                Vector3? tint = zone == StageInkZone.None ? null : pass.Ink(zone).Ink;
                if (rolling is not null && rolling.TryGetValue(key, out var pieces))
                    DrawRoll(chrome, _rolls[key].Old, pieces, tint, fit);
                else
                    DrawLayer(chrome, 1f, 1f, 0, 0, ShakeOf(key, now, ref animating), tint: tint);
            }
        }
        Clip(null);

        Section("chrome");
        // Big Type's launch (M47 B8): the takeover over everything.
        animating |= DrawTakeover(now, fit);
        LastSections = _sections?.ToString();
        _drawnDeep = _backdrop?.Deep;
        _probeTag = _bigType ? ProbeTagOf(lettersMoving, wiping && _beneath.Count > 0) : null;
        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        return animating;
    }

    /// <summary>The motions the frame probe files a frame under: "letters", "wipe", joined by "+"; null when neither ran.</summary>
    internal static string? ProbeTagOf(bool letters, bool wipe) => (letters, wipe) switch
    {
        (true, true) => "letters+wipe",
        (true, false) => "letters",
        (false, true) => "wipe",
        _ => null,
    };

    /// <summary>
    /// How far into an idle hop's wave the name's letters are (M47 B6, the mock's <c>hop</c>: the same schedule as the Launcher's
    /// tiles, from the last press); null when no wave is running over them.
    /// </summary>
    private float? LettersHop(float now, BigTypeStageLayer letters)
    {
        if (!_bigType || letters.Name is not { } name || float.IsNaN(_lastInput) || letters.Arriving(now, _holdStill))
            return null;
        var sinceInput = now - _lastInput;
        if (BigTypeMotion.HopStart(sinceInput, _holdStill) is not { } start)
            return null;
        var since = sinceInput - start;
        return since < BigTypeMotion.LetterHopWaveMilliseconds(name.Letters.Count) ? since : null;
    }

    /// <summary>
    /// One pass of the text and veils over the backdrops (M47 B6): the circles that keep it to where one backdrop shows, and that
    /// backdrop's ink, veil colour and strength for each zone. Outside a wipe there is one pass, unclipped, with the ink mixed over
    /// whatever is on screen (B4).
    /// </summary>
    internal sealed record InkPass(Vector4 Clip0, Vector4 Clip1, Vector4 Clip2, Func<StageInkZone, ZoneColours> Ink);

    /// <summary>
    /// The passes a zone's text or veil is drawn in (M47 B6). During a wipe, a zone whose ink, veil colour and veil strength are the same
    /// for every backdrop showing is drawn once with no circle: the circles share the screen out between the passes, so every pixel gets
    /// the same ink either way, and the text costs one draw instead of one per backdrop. A zone whose answers differ keeps one pass per
    /// backdrop, each in its own circle, so no pixel of text is in an ink chosen for another backdrop. Text with no zone (today's pills, the
    /// message toast) is drawn once, whatever is behind it.
    /// </summary>
    internal static IReadOnlyList<InkPass> PassesFor(StageInkZone zone, IReadOnlyList<InkPass> passes)
    {
        if (passes.Count <= 1)
            return passes;
        if (zone != StageInkZone.None)
        {
            var first = passes[0].Ink(zone);
            for (var i = 1; i < passes.Count; i++)
            {
                if (passes[i].Ink(zone) != first)
                    return passes;
            }
        }
        return [passes[^1] with { Clip0 = default, Clip1 = default, Clip2 = default }];
    }

    private void Clip(InkPass? pass)
    {
        _clip0 = pass?.Clip0 ?? default;
        _clip1 = pass?.Clip1 ?? default;
        _clip2 = pass?.Clip2 ?? default;
    }

    /// <summary>
    /// Draws the Big Type backdrops bottom up, each wiping in through its circle over the ones beneath, and returns the passes the
    /// text is drawn in: one per backdrop that shows, kept inside its own circle and outside every circle above it, in that backdrop's
    /// ink. So every pixel of text is in the ink chosen for the backdrop actually behind it, through the whole wipe.
    /// </summary>
    private IReadOnlyList<InkPass> DrawBigTypeBackdrops(float now, float clock)
    {
        var visible = new List<(BackdropState State, float? Radius)>(3);
        foreach (var state in Stack())
        {
            if (!state.Ready)
                continue;
            // The bottom one covers the screen, even one whose own wipe was cut short when an older one was let go.
            var radius = visible.Count == 0 ? null : WipeRadius(state, now);
            if (radius is 0f)
                continue;
            visible.Add((state, radius));
        }
        if (visible.Count == 0)
        {
            var deep = _ink?.Deep ?? Vector3.One;
            return [new InkPass(default, default, default, zone => ZoneInk(zone, [(null, 1f)], deep, IsNarrowCanvas, bigType: true))];
        }
        var passes = new List<InkPass>(visible.Count);
        for (var i = 0; i < visible.Count; i++)
        {
            var (state, radius) = visible[i];
            var inside = radius is { } r ? WipeCircle(state, r, inside: true) : default;
            DrawBackdrop(state, clock, 1f, inside);
            var above = visible.Skip(i + 1).Select(v => WipeCircle(v.State, v.Radius ?? 0f, inside: false)).ToArray();
            var showing = EntryShowing(state, clock);
            var deep = state.Deep;
            passes.Add(new InkPass(inside, above.Length > 0 ? above[0] : default, above.Length > 1 ? above[1] : default,
                zone => ZoneInk(zone, showing, deep, IsNarrowCanvas, bigType: true)));
        }
        return passes;
    }

    /// <summary>One Big Type backdrop's share of what shows where it shows: its running backdrop, mixed with the one it is rotating from.</summary>
    private static List<(string? Id, float Weight)> EntryShowing(BackdropState state, float clock)
    {
        if (state.Animated is not { } animated)
            return [(null, 1f)];
        if (state.Previous is { } previous)
        {
            var p = StageCurves.EaseInOut((clock - state.RotationStart) / BackdropRotation.FadeMilliseconds);
            return [(previous.Id, 1f - p), (animated.Id, p)];
        }
        return [(animated.Id, 1f)];
    }

    /// <summary>
    /// A rolling text (M47 B5): the new picture's unchanged parts as they are, each changed character's old glyph leaving upward and
    /// the new one rising in, each under a scissor of its own cell.
    /// </summary>
    private void DrawRoll(Layer layer, Layer old, IReadOnlyList<BigTypeRollPiece> pieces, Vector3? tint,
        (int X, int Y, int Width, int Height, float Scale) fit)
    {
        if (UseBatch)
        {
            // Batched (M47 B6): each piece cut to the same window by the fragment shader instead of the scissor.
            foreach (var piece in pieces)
            {
                var left = fit.X + (int)MathF.Floor(piece.ClipX * fit.Scale);
                var right = fit.X + (int)MathF.Ceiling((piece.ClipX + piece.ClipWidth) * fit.Scale);
                var bottom = fit.Y + (int)MathF.Floor((CanvasHeight - piece.ClipY - piece.ClipHeight) * fit.Scale);
                var top = fit.Y + (int)MathF.Ceiling((CanvasHeight - piece.ClipY) * fit.Scale);
                if (right <= left || top <= bottom)
                    continue;
                var source = piece.Source == BigTypeRollSource.Old ? old : layer;
                var picture = source.Image;
                BatchQuad(source.Texture, picture.X, picture.Y + piece.OffsetY, picture.Width, picture.Height, piece.Opacity,
                    new Vector4(0, 0, 1, 1), StageAffine.Identity, tint, new Vector4(left, bottom, right, top));
            }
            FlushBatch();
            return;
        }
        _gl.Enable(EnableCap.ScissorTest);
        foreach (var piece in pieces)
        {
            var left = fit.X + (int)MathF.Floor(piece.ClipX * fit.Scale);
            var right = fit.X + (int)MathF.Ceiling((piece.ClipX + piece.ClipWidth) * fit.Scale);
            var bottom = fit.Y + (int)MathF.Floor((CanvasHeight - piece.ClipY - piece.ClipHeight) * fit.Scale);
            var top = fit.Y + (int)MathF.Ceiling((CanvasHeight - piece.ClipY) * fit.Scale);
            if (right <= left || top <= bottom)
                continue;
            _gl.Scissor(left, bottom, (uint)(right - left), (uint)(top - bottom));
            var source = piece.Source == BigTypeRollSource.Old ? old : layer;
            var picture = source.Image;
            DrawQuad(source.Texture, picture.X, picture.Y + piece.OffsetY, picture.Width, picture.Height, piece.Opacity, 1f, 0f, 0f,
                new Vector4(0, 0, 1, 1), tint: tint);
        }
        _gl.Disable(EnableCap.ScissorTest);
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

    /// <summary>
    /// Where the canvas goes on a surface: as large as fits, centred. Today's look always has a canvas of the surface's shape
    /// (1920 or 1240 by 1080), so it fills the surface exactly as before.
    /// </summary>
    internal (int X, int Y, int Width, int Height, float Scale) FitCanvas(int surfaceWidth, int surfaceHeight)
    {
        var scale = Math.Min(surfaceWidth / CanvasWidth, surfaceHeight / CanvasHeight);
        var width = (int)MathF.Round(CanvasWidth * scale);
        var height = (int)MathF.Round(CanvasHeight * scale);
        if (Math.Abs(width - surfaceWidth) <= 1 && Math.Abs(height - surfaceHeight) <= 1)
            return (0, 0, surfaceWidth, surfaceHeight, surfaceWidth / CanvasWidth);
        return ((surfaceWidth - width) / 2, (surfaceHeight - height) / 2, width, height, scale);
    }

    // ---- Big Type's ink (M47 B4) ----

    /// <summary>A zone's ink, its veil's colour and the veil's strength, for what is on screen now.</summary>
    internal readonly record struct ZoneColours(Vector3 Ink, Vector3 Veil, float VeilStrength);

    /// <summary>
    /// The backdrops on screen with their share of it: the system's backdrop (and the one it is taking over from during a
    /// rotation), and while a console change crossfades, the leaving system's too. A pictured fallback counts as null.
    /// </summary>
    private List<(string? Id, float Weight)> ShowingBackdrops(float clock, float fade)
    {
        var showing = new List<(string? Id, float Weight)>(4);
        void Add(BackdropState? state, float weight)
        {
            if (state is null || weight <= 0f)
                return;
            if (state.Animated is not { } animated)
            {
                showing.Add((null, weight));
                return;
            }
            if (state.Previous is { } previous)
            {
                var p = StageCurves.EaseInOut((clock - state.RotationStart) / BackdropRotation.FadeMilliseconds);
                showing.Add((previous.Id, weight * (1f - p)));
                showing.Add((animated.Id, weight * p));
                return;
            }
            showing.Add((animated.Id, weight));
        }
        var current = _leavingBackdrop is null ? 1f : fade;
        Add(_backdrop, current);
        Add(_leavingBackdrop, 1f - current);
        if (showing.Count == 0)
            showing.Add((null, 1f));
        return showing;
    }

    private ZoneColours ZoneInk(StageInkZone zone, float clock, float fade) =>
        ZoneInk(zone, ShowingBackdrops(clock, fade), _ink?.Deep ?? Vector3.One, IsNarrowCanvas);

    // The narrow screen's canvas (the screens swapped): the legibility table's narrow answer (M47 B11).
    private bool IsNarrowCanvas => _canvasWidth < WideCanvasWidth;

    /// <summary>
    /// Mixes the zone's answer (<see cref="BackdropLegibility"/>) over the backdrops on screen by their share: dark ink is
    /// <paramref name="deep"/> with a white veil, white ink has a veil of <paramref name="deep"/>. The veil's colour is mixed by
    /// strength, so a strong veil handing over to a light one keeps its colour while it fades.
    /// </summary>
    /// <param name="narrow">The narrow Stage's own left veils where they are stronger (M47 B11).</param>
    /// <param name="bigType">Big Type's answers (M47 B13: its recoloured and resized animations), falling back to today's.</param>
    internal static ZoneColours ZoneInk(StageInkZone zone, IReadOnlyList<(string? Id, float Weight)> showing, Vector3 deep, bool narrow = false,
        bool bigType = false)
    {
        var ink = Vector3.Zero;
        var veil = Vector3.Zero;
        float strength = 0f, total = 0f;
        foreach (var (id, weight) in showing)
        {
            // The narrow Stage's own veil where the table's falls short over its crop (M47 B11); Big Type's own answers (M47 B13).
            var answer = (bigType ? BackdropLegibility.ForBigType(id, narrow) : BackdropLegibility.For(id, narrow))[zone];
            ink += weight * (answer.Dark ? deep : Vector3.One);
            veil += weight * answer.Veil * (answer.Dark ? Vector3.One : deep);
            strength += weight * answer.Veil;
            total += weight;
        }
        if (total <= 0f)
            return new ZoneColours(Vector3.One, deep, 0f);
        return new ZoneColours(ink / total, strength > 0f ? veil / strength : deep, strength / total);
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
        Section("mediumTarget");
        if (_coverDirty)
        {
            _renderer.SetCoverArt(_cover);
            _coverDirty = false;
        }
        Section("mediumCover");
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

    private sealed class BackdropState(StageBackdrop backdrop)
    {
        public StageBackdrop Backdrop { get; set; } = backdrop;

        /// <summary>The animated backdrop drawn instead of the layers, when the system has one that compiled.</summary>
        public BackdropInstance? Animated { get; set; }

        /// <summary>The system's previous backdrop while the next one fades in over it.</summary>
        public BackdropInstance? Previous { get; set; }

        public float RotationStart { get; set; }

        /// <summary>The pictured layers on the GPU; under Big Type (M47 B6) uploaded only when they are to be drawn.</summary>
        public Layer? Under { get; set; }
        public Layer? Over { get; set; }
        public uint Tile { get; set; }
        public Layer? Quad { get; set; }

        // ---- Big Type (M47 B6) ----

        /// <summary>The system's animated backdrops (null: none, its pictures show).</summary>
        public IReadOnlyList<string>? Ids { get; init; }

        /// <summary>When its command came (the Stage's clock).</summary>
        public float Queued { get; init; }

        /// <summary>How far it has been prepared: 0 not yet, 1 started (or its first layer uploaded), 2 ready to draw.</summary>
        public int Step { get; set; } = 2;

        public bool Ready => Step >= 2;

        /// <summary>When its wipe's circle began to grow: NaN not yet, negative infinity for one that simply covers the screen.</summary>
        public float WipeStart { get; set; } = float.NaN;

        /// <summary>Where its wipe grows from across the Stage canvas.</summary>
        public float WipeOriginX { get; init; }

        /// <summary>The dark ink its text is drawn in (the console's colour taken toward near-black).</summary>
        public Vector3 Deep { get; set; } = Vector3.One;

        /// <summary>The look its animations are drawn for (M47 B13): Big Type's, with the colour they are recoloured toward.</summary>
        public BackdropLook Look { get; init; }
    }

    private BackdropState Upload(StageBackdrop backdrop) => new(backdrop)
    {
        Under = new Layer(backdrop.Under, Upload(backdrop.Under.Pixels, repeat: false)),
        Over = backdrop.Over is { } over ? new Layer(over, Upload(over.Pixels, repeat: false)) : null,
        Tile = backdrop.Tile is { } tile ? Upload(tile, repeat: true) : 0,
        Quad = backdrop.Quad is { } quad ? new Layer(quad, Upload(quad.Pixels, repeat: false)) : null,
    };

    private void DeleteBackdrop(BackdropState? state)
    {
        if (state is null)
            return;
        _animator?.Stop(state.Animated);
        _animator?.Stop(state.Previous);
        state.Animated = state.Previous = null;
        DeleteLayers(state);
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
        if (_animator!.Start(BackdropRotation.Pick(ids, showing.Id, _random), clock, look: state.Look) is not { } next)
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

    private void DrawBackdrop(BackdropState state, float clock, float opacity, Vector4 clip = default)
    {
        if (state.Animated is { } animated)
        {
            if (state.Previous is { } previous)
            {
                // Within a system the next backdrop fades in over the last one (BackdropRotation.FadeMilliseconds).
                _animator!.Present(previous, opacity, BackdropCrop, clip);
                _animator.Present(animated, opacity * StageCurves.EaseInOut((clock - state.RotationStart) / BackdropRotation.FadeMilliseconds), BackdropCrop, clip);
            }
            else
            {
                _animator!.Present(animated, opacity, BackdropCrop, clip);
            }
            // Back to the sprite program for the layers above.
            _program.Use();
            _gl.BindVertexArray(_vao);
            _gl.ActiveTexture(TextureUnit.Texture0);
            return;
        }
        // Big Type uploads the pictured layers only when they are drawn (M47 B6).
        if (state.Under is null)
            UploadLayers(state);
        // A wipe's circle cuts the pictured layers as it cuts an animated backdrop.
        _clip0 = clip;
        // The pictured layers are 1920 wide: a narrower canvas shows their middle.
        var shift = BackdropShift;
        DrawLayer(state.Under!, opacity, 1f, 0, 0, shift);
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
        _clip0 = default;
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

    private void DrawLayer(Layer layer, float opacity, float scale, float pivotX, float pivotY, float shiftX = 0f, Vector3? tint = null)
    {
        var image = layer.Image;
        DrawQuad(layer.Texture, image.X + shiftX, image.Y, image.Width, image.Height, opacity, scale, pivotX, pivotY, new Vector4(0, 0, 1, 1),
            tint: tint);
    }

    private void DrawQuad(uint texture, float x, float y, float width, float height, float opacity, float scale,
        float pivotX, float pivotY, Vector4 uv, StageAffine? part = null, Vector3? tint = null)
    {
        if (opacity <= 0f)
            return;
        var local = part ?? StageAffine.Identity;
        _sectionQuads++;
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        // A tint (M47 B4) draws a white picture in a zone's ink; without one the picture is drawn as it is.
        var tintAmount = tint is null ? 0f : 1f;
        var linear = new Vector4(local.M11, local.M12, local.M21, local.M22);
        var translate = new Vector2(local.M31, local.M32);
        var rect = new Vector4(x, y, width, height);
        var pivot = new Vector3(scale, pivotX, pivotY);
        if (!_bigType)
        {
            // Today's look sets every uniform for every quad, as it always has.
            _quadUniforms = null;
            _program.Set("uTintAmount", tintAmount);
            if (tint is { } colour)
                _program.Set("uTint", colour);
            _program.Set("uLinear", linear);
            _program.Set("uTranslate", translate);
            _program.Set("uOffset", Vector2.Zero);
            _program.Set("uGroup", new Vector3(1, 0, 0));
            _program.Set("uRect", rect);
            _program.Set("uScale", pivot);
            _program.Set("uUv", uv);
            _program.Set("uOpacity", opacity);
            // Big Type's circles (M47 B6); all zero, as today's look always has them, cut nothing.
            _program.Set("uClip0", _clip0);
            _program.Set("uClip1", _clip1);
            _program.Set("uClip2", _clip2);
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            return;
        }
        // Big Type (M47 B6) draws many small quads a frame (a name's letters, twice during a wipe; a rolling line's pieces). Each GL
        // call costs the render thread a few microseconds on the Thor, so a uniform is set only when it differs from the value the
        // program already holds: a run of letters or pieces then sets little more than its rectangle and its texture coordinates.
        var last = _quadUniforms ??= new QuadUniforms();
        if (!last.Valid || last.TintAmount != tintAmount)
            _program.Set("uTintAmount", last.TintAmount = tintAmount);
        if (tint is { } ink && (!last.Valid || last.Tint != ink))
            _program.Set("uTint", last.Tint = ink);
        if (!last.Valid || last.Linear != linear)
            _program.Set("uLinear", last.Linear = linear);
        if (!last.Valid || last.Translate != translate)
            _program.Set("uTranslate", last.Translate = translate);
        if (!last.Valid)
        {
            _program.Set("uOffset", Vector2.Zero);
            _program.Set("uGroup", new Vector3(1, 0, 0));
        }
        if (!last.Valid || last.Rect != rect)
            _program.Set("uRect", last.Rect = rect);
        if (!last.Valid || last.Pivot != pivot)
            _program.Set("uScale", last.Pivot = pivot);
        if (!last.Valid || last.Uv != uv)
            _program.Set("uUv", last.Uv = uv);
        if (!last.Valid || last.Opacity != opacity)
            _program.Set("uOpacity", last.Opacity = opacity);
        if (!last.Valid || last.Clip0 != _clip0)
            _program.Set("uClip0", last.Clip0 = _clip0);
        if (!last.Valid || last.Clip1 != _clip1)
            _program.Set("uClip1", last.Clip1 = _clip1);
        if (!last.Valid || last.Clip2 != _clip2)
            _program.Set("uClip2", last.Clip2 = _clip2);
        last.Valid = true;
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    // ---- Big Type's batched quads (M47 B6) ----

    /// <summary>How many quads one batched draw holds (sprite-batch.vert's uQuads: five vec4s a quad).</summary>
    internal const int BatchQuads = 48;
    private const int BatchStride = 20;

    private GlProgram? _batchProgram;
    private readonly float[] _batch = new float[BatchQuads * BatchStride];
    private int _batchCount;
    private uint _batchTexture0;
    private uint _batchTexture1;
    private Vector3? _batchTint;

    /// <summary>
    /// Whether Big Type draws a name's letters and a rolling line's pieces as batches, one instanced draw for up to
    /// <see cref="BatchQuads"/> of them, instead of a draw per quad (on by default; the desktop GL check turns it off to compare the
    /// two pixel for pixel). Today's look never batches.
    /// </summary>
    public bool Batching { get; set; } = true;

    private bool UseBatch => _bigType && Batching;

    /// <summary>
    /// Adds a quad to the batch, as <see cref="DrawQuad"/> would draw it at scale 1 (the letters and the roll's pieces): its place, its
    /// texture coordinates, its own transform, its opacity, and the window it is cut to (x0, y0, x1, y1 in the surface's pixels; zero is
    /// none). A batch holds one ink and up to two textures in the order the quads came, so the drawing order is kept.
    /// </summary>
    private void BatchQuad(uint texture, float x, float y, float width, float height, float opacity, Vector4 uv, StageAffine affine,
        Vector3? tint, Vector4 window)
    {
        if (opacity <= 0f)
            return;
        if (_batchCount > 0 && tint != _batchTint)
            FlushBatch();
        float source;
        if (_batchCount == 0)
        {
            (_batchTexture0, _batchTexture1, _batchTint, source) = (texture, 0u, tint, 0f);
        }
        else if (texture == _batchTexture0)
        {
            source = 0f;
        }
        else if (_batchTexture1 == 0 || texture == _batchTexture1)
        {
            _batchTexture1 = texture;
            source = 1f;
        }
        else
        {
            FlushBatch();
            (_batchTexture0, _batchTexture1, _batchTint, source) = (texture, 0u, tint, 0f);
        }
        var at = _batchCount * BatchStride;
        _batch[at] = x;
        _batch[at + 1] = y;
        _batch[at + 2] = width;
        _batch[at + 3] = height;
        _batch[at + 4] = uv.X;
        _batch[at + 5] = uv.Y;
        _batch[at + 6] = uv.Z;
        _batch[at + 7] = uv.W;
        _batch[at + 8] = affine.M11;
        _batch[at + 9] = affine.M12;
        _batch[at + 10] = affine.M21;
        _batch[at + 11] = affine.M22;
        _batch[at + 12] = affine.M31;
        _batch[at + 13] = affine.M32;
        _batch[at + 14] = opacity;
        _batch[at + 15] = source;
        _batch[at + 16] = window.X;
        _batch[at + 17] = window.Y;
        _batch[at + 18] = window.Z;
        _batch[at + 19] = window.W;
        _batchCount++;
        _sectionQuads++;
        if (_batchCount == BatchQuads)
            FlushBatch();
    }

    /// <summary>Draws the quads batched so far in one instanced draw, under the circles set now, then hands back the sprite program.</summary>
    private void FlushBatch()
    {
        if (_batchCount == 0)
            return;
        _batchProgram ??= GlProgram.Create(
            _gl,
            ShaderLibrary.Load("sprite-batch.vert", _dialect, _major, _minor),
            ShaderLibrary.Load("stage-batch.frag", _dialect, _major, _minor),
            "stage batch");
        var program = _batchProgram;
        program.Use();
        program.Set("uTexture", 0);
        program.Set("uTexture1", 1);
        program.Set("uCanvas", new Vector2(CanvasWidth, CanvasHeight));
        program.Set("uScale", new Vector3(1f, 0f, 0f));
        program.Set("uOffset", Vector2.Zero);
        program.Set("uGroup", new Vector3(1f, 0f, 0f));
        program.Set("uFit", new Vector3(_fit.X, _fit.Y, _fit.Scale));
        program.Set("uTintAmount", _batchTint is null ? 0f : 1f);
        if (_batchTint is { } ink)
            program.Set("uTint", ink);
        program.Set("uClip0", _clip0);
        program.Set("uClip1", _clip1);
        program.Set("uClip2", _clip2);
        _gl.ActiveTexture(TextureUnit.Texture1);
        _gl.BindTexture(TextureTarget.Texture2D, _batchTexture1 != 0 ? _batchTexture1 : _batchTexture0);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _batchTexture0);
        program.Set("uQuads", _batch.AsSpan(0, _batchCount * BatchStride));
        _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)_batchCount);
        _batchCount = 0;
        _program.Use();
    }

    // The values the sprite program holds (Big Type only); every one of its per-quad uniforms is set through DrawQuad.
    private QuadUniforms? _quadUniforms;

    private sealed class QuadUniforms
    {
        public bool Valid;
        public float TintAmount;
        public Vector3 Tint;
        public Vector4 Linear;
        public Vector2 Translate;
        public Vector4 Rect;
        public Vector3 Pivot;
        public Vector4 Uv;
        public float Opacity;
        public Vector4 Clip0;
        public Vector4 Clip1;
        public Vector4 Clip2;
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
        DisposeTakeover();
        foreach (var state in _beneath)
            DeleteBackdrop(state);
        _beneath.Clear();
        _letters?.Dispose();
        _letters = null;
        foreach (var layer in _chrome.Values)
            _gl.DeleteTexture(layer.Texture);
        _chrome.Clear();
        foreach (var roll in _rolls.Values)
            _gl.DeleteTexture(roll.Old.Texture);
        _rolls.Clear();
        foreach (var layer in _veils.Values)
            _gl.DeleteTexture(layer.Texture);
        _veils.Clear();
        DeleteMediumTarget();
        _renderer?.Dispose();
        _renderer = null;
        _gl.DeleteVertexArray(_vao);
        _batchProgram?.Dispose();
        _program.Dispose();
    }
}
