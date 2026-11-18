using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Stage;

/// <summary>
/// The Handheld Launcher (bottom screen) as one GPU scene (M45 H4, option 5): every tile, chip, dock button
/// and the page dots are pictures drawn once by the Avalonia Launcher; this scene does all of the Launcher's
/// motion per frame on its own thread: the home page track and the folder's column track (springs that keep
/// their speed), the focus lift (1.13× with the deeper shadow fading in), the travelling selector (a spring
/// in position, size and radius, drawn as rounded-rect bands, over its breathing glow), the edge bump, and
/// the folder's Zoom through. Nothing is laid out or blurred here.
/// </summary>
public sealed partial class LauncherCompositor : INativeScene
{
    private readonly GL _gl;
    private readonly GlProgram _sprites;
    // Recently replaced tile faces already live on the GPU. The live sprites own their own textures.
    private readonly LinkedList<(StagePixels Pixels, uint Texture)> _spareFaces = [];
    private readonly GlProgram _ring;
    private readonly uint _vao;

    private readonly Dictionary<string, SpriteState> _live = new(StringComparer.Ordinal);
    private List<SpriteState> _closing = [];

    private LauncherLayoutCommand _layout = new(1240, 1080, new StageRect(0, 0, 1240, 1080), new StageRect(0, 0, 1240, 1080), 1240, 264, 30, -17);
    private readonly SceneSpring _page = new(LauncherMotion.TrackStiffness, LauncherMotion.TrackDamping);
    private readonly SceneSpring _column = new(LauncherMotion.TrackStiffness, LauncherMotion.TrackDamping);
    private bool _tracksPlaced;

    private readonly SceneSpring _x = new(LauncherMotion.SelectorStiffness, LauncherMotion.SelectorDamping);
    private readonly SceneSpring _y = new(LauncherMotion.SelectorStiffness, LauncherMotion.SelectorDamping);
    private readonly SceneSpring _w = new(LauncherMotion.SelectorStiffness, LauncherMotion.SelectorDamping);
    private readonly SceneSpring _h = new(LauncherMotion.SelectorStiffness, LauncherMotion.SelectorDamping);
    private readonly SceneSpring _r = new(LauncherMotion.SelectorStiffness, LauncherMotion.SelectorDamping);
    private LauncherSelectorTarget? _target;
    private bool _selectorShown;
    private string? _focused;
    private float _focusTime = float.NegativeInfinity;
    private uint _glowTexture;
    private StageImage? _glowImage;
    private float _hiddenSince = float.NegativeInfinity;
    private Vector2 _bump;
    private float _bumpStart = float.NegativeInfinity;

    private bool _folderOpen;
    private bool _zoomOpening;
    private float _zoomStart = float.NegativeInfinity;
    private Vector2 _zoomOrigin;
    private string? _zoomTile;

    // The Big Type look's ground (M47 B3): a flat colour; null clears to today's light grey, under the pictured gradient. A new
    // colour floods in (M47 B6) as a circle growing from the focused tile over the old: the waves still growing, oldest first, each
    // with where it grows from (found on the first frame it is drawn, once the focus that came with it has arrived).
    private Vector3? _groundTo;
    private readonly List<FloodWave> _waves = [];
    private uint _white;
    private string? _probeTag;

    private sealed class FloodWave(Vector3 colour, float start)
    {
        public Vector3 Colour { get; } = colour;
        public float Start { get; } = start;
        public Vector2? Origin { get; set; }
    }

    /// <inheritdoc />
    public string? ProbeTag => _probeTag;

    /// <summary>While set (the probe's "ring" run, M47 B11), each frame's ring and target are kept in <see cref="LastRing"/>.</summary>
    public bool ProbeRing { get; set; }

    /// <summary>The last frame's ring and target, for the probe; null until one is drawn with <see cref="ProbeRing"/> set.</summary>
    public string? LastRing { get; private set; }

    // The Big Type look's motion (M47 B5): off until LauncherMotionCommand says so, so today's look moves exactly as before.
    private bool _bigType;
    private readonly BigTypeLauncherMotion _bt = new();
    private float _lastNow;

    private float _lastFrame = float.NaN;
    private (int X, int Y, int Width, int Height)? _groupScissor;
    private StageRect? _progress;
    private float _progressStart;

    public LauncherCompositor(GL gl, GlslDialect dialect, int majorVersion, int minorVersion)
    {
        _gl = gl;
        _sprites = GlProgram.Create(
            gl,
            ShaderLibrary.Load("sprite.vert", dialect, majorVersion, minorVersion),
            ShaderLibrary.Load("stage.frag", dialect, majorVersion, minorVersion),
            "launcher sprite");
        _ring = GlProgram.Create(
            gl,
            ShaderLibrary.Load("sprite.vert", dialect, majorVersion, minorVersion),
            ShaderLibrary.Load("selector.frag", dialect, majorVersion, minorVersion),
            "launcher selector");
        _vao = gl.GenVertexArray();
    }

    /// <summary>
    /// While only the focus glow breathes (everything else settled), the next 30 Hz step of the breath: a slow
    /// change of opacity looks the same at 30 frames a second as at the panel's 120, at a quarter of the frames
    /// (each costs Android's compositor as much as it costs us). Null when nothing moves.
    /// </summary>
    /// <remarks>Big Type (M47 B5): the ring's breath and the focused tile's bob step at 30 Hz for a minute after the last press,
    /// then rest; after that nothing is drawn until something changes.</remarks>
    public float? NextFrameMilliseconds => _bigType
        ? _selectorShown ? _bt.NextFrameMilliseconds(_lastNow) : null
        : _breathing ? LauncherMotion.BreathStepMilliseconds : null;

    /// <summary>The framebuffer frames are drawn into: the surface's own (0), or a target of the desktop GL checks.</summary>
    public uint TargetFramebuffer { get; init; }

    /// <summary>The Launcher's retained commands rebuild it whole; nothing else carries over.</summary>
    public StageCommand? CaptureResume() => null;

    private bool _breathing;

    private sealed class SpriteState(LauncherSprite sprite, uint texture, uint shadow)
    {
        public LauncherSprite Sprite { get; set; } = sprite;
        public FolderArtState2D? Folder { get; set; }
        public uint Texture { get; set; } = texture;
        public uint Shadow { get; set; } = shadow;
        public FolderArtState PendingState { get; set; } = FolderArtState.Closed;
        public float LaunchStart { get; set; } = float.NegativeInfinity;
        public bool LaunchDown { get; set; }
        public bool LaunchBigType { get; set; }
        public float LiftFrom { get; set; } = 1f;
        public float LiftTo { get; set; } = 1f;
        public float LiftStart { get; set; } = float.NegativeInfinity;



        /// <summary>Big Type lifts on its landing curve over 380 ms (M47 B5); today's look on its own over 220.</summary>
        public bool BigType { get; set; }

        public float LiftMilliseconds => BigType ? BigTypeMotion.LiftMilliseconds : LauncherMotion.LiftMilliseconds;

        /// <summary>A text roll under way: the picture it leaves, where its characters were, and when it began.</summary>
        public (uint Texture, StageImage Image, StageText Text, float Start)? Roll { get; set; }

        public float Lift(float now)
        {
            if (BigType)
                return BigTypeMotion.Settle(LiftFrom, LiftTo, now - LiftStart);
            var t = (now - LiftStart) / LauncherMotion.LiftMilliseconds;
            return t >= 1 ? LiftTo : LiftFrom + ((LiftTo - LiftFrom) * LauncherMotion.Lift(t));
        }

        public float ShadowOpacity(float now)
        {
            var t = Math.Clamp((now - LiftStart) / LauncherMotion.LiftMilliseconds, 0f, 1f);
            var from = LiftFrom > 1f ? 1f : 0f;
            var to = LiftTo > 1f ? 1f : 0f;
            return from + ((to - from) * t);
        }
    }

    public void Apply(StageCommand command, float now)
    {
        switch (command)
        {
            case LauncherMotionCommand motion:
                _bigType = motion.BigType;
                _bt.HoldStill = motion.BigType && motion.HoldStill;
                // The Launcher arriving counts as the first press: the resting motion lives its minute and the hop waits.
                if (float.IsNaN(_bt.LastInput))
                    _bt.Input(now);
                foreach (var state in _live.Values)
                    state.BigType = _bigType;
                break;
            case LauncherInputCommand:
                _bt.Input(now);
                break;
            case LauncherLayoutCommand layout:
                _layout = layout;
                break;
            case LauncherSpriteCommand sprite:
                ApplySprite(sprite, now);
                break;
            case LauncherFocusCommand focus:
                ApplyFocus(focus, now);
                break;
            case LauncherTracksCommand tracks:
                var page = -tracks.Page * _layout.PageWidth;
                var column = -tracks.FirstColumn * _layout.ColumnPitch;
                if (tracks.SnapPage || !_tracksPlaced)
                    _page.Snap(page);
                else
                    _page.Retarget(page);
                if (tracks.SnapColumn || !_tracksPlaced)
                    _column.Snap(column);
                else
                {
                    _column.Retarget(column);
                    // Only one spare column is realized past each edge: never lag further than that.
                    _column.Limit(_layout.ColumnPitch);
                }
                _tracksPlaced = true;
                _bt.HopPage = tracks.Page;
                _bt.HopFirstColumn = tracks.FirstColumn;
                break;
            case LauncherFolderCommand folder:
                _folderOpen = folder.Open;
                _bt.HopGrid = folder.Open ? LauncherGroup.Folder : LauncherGroup.Home;
                break;
            case LauncherZoomCommand zoom when _bigType:
                ApplyBigTypeFolder(zoom, now);
                break;
            case LauncherPlayCommand play:
                ApplyPlay(play, now);
                break;
            case LauncherLettersCommand letters:
                ApplyLetters(letters, now);
                break;
            case LauncherZoomCommand zoom:
                _zoomOpening = zoom.Open;
                _zoomStart = now;
                _zoomOrigin = new Vector2(zoom.OriginX, zoom.OriginY);
                _zoomTile = zoom.TileId;
                _hiddenSince = now;
                if (!zoom.Open)
                {
                    // The folder's sprites are about to be taken away; keep drawing them while they shrink.
                    DeleteAll(_closing);
                    _closing = _live.Values.Where(s => s.Sprite.Group == LauncherGroup.Folder).ToList();
                    foreach (var state in _closing)
                    {
                        _live.Remove(state.Sprite.Id);
                        _bt.Place(state.Sprite.Id, null);
                    }
                }
                break;
            case LauncherFolderStateCommand folderState:
                if (_live.TryGetValue(folderState.Id, out var holder))
                {
                    holder.PendingState = folderState.State;
                    holder.Folder?.SetState(folderState.State, now, folderState.Animate);
                }
                break;
            case LauncherProgressCommand progress:
                if (progress.Track is not null && _progress is null)
                    _progressStart = now;
                _progress = progress.Track;
                break;
            case LauncherLaunchCommand launch:
                if (_live.TryGetValue(launch.SpriteId, out var launching))
                {
                    launching.LaunchStart = now;
                    launching.LaunchDown = launch.Downward;
                    launching.LaunchBigType = launch.BigType;
                }
                break;
            // Held still under Big Type (M47 B10, the mock's bump returns under reduced motion), the wall is not nudged.
            case LauncherBumpCommand when _bt.HoldStill:
                break;
            case LauncherBumpCommand bump:
                _bump = new Vector2(bump.DirectionX, bump.DirectionY);
                _bumpStart = now;
                break;
            case LauncherGroundCommand ground:
                var to = new Vector3(ground.R, ground.G, ground.B);
                if (!ground.Flood || _groundTo is null || _bt.HoldStill)
                {
                    // The first ground, a replay, or held still: it is simply there.
                    _groundTo = to;
                    _waves.Clear();
                }
                else if (to != (_waves.Count > 0 ? _waves[^1].Colour : _groundTo.Value))
                {
                    _waves.Add(new FloodWave(to, now)
                    {
                        Origin = ground.FromMiddle ? new Vector2(_layout.CanvasWidth / 2f, _layout.CanvasHeight / 2f) : null,
                    });
                }
                break;
        }
    }

    /// <summary>The ground the screen is cleared to: the last colour to have covered it.</summary>
    private Vector3 Ground() => _groundTo ?? new Vector3(0.96f, 0.97f, 0.98f);

    /// <summary>
    /// Big Type's flood (M47 B6, move 3): each new ground grows as a circle from the tile focused when it came, over the ground and
    /// the waves before it (the mock's <c>flood</c>); a wave that has covered the screen becomes the ground. Returns whether one grows.
    /// </summary>
    private bool DrawFlood(float now, float sx, float sy, int surfaceHeight)
    {
        if (_waves.Count == 0)
            return false;
        if (_white == 0)
            _white = SpriteTextures.Upload(_gl, new StagePixels(1, 1, [255, 255, 255, 255], IsBgra: false), repeat: false);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindTexture(TextureTarget.Texture2D, _white);
        _sprites.Set("uLinear", new Vector4(1, 0, 0, 1));
        _sprites.Set("uTranslate", Vector2.Zero);
        _sprites.Set("uScale", new Vector3(1, 0, 0));
        _sprites.Set("uOffset", Vector2.Zero);
        _sprites.Set("uGroup", new Vector3(1, 0, 0));
        _sprites.Set("uUv", new Vector4(0, 0, 1, 1));
        _sprites.Set("uOpacity", 1f);
        _sprites.Set("uTintAmount", 1f);
        foreach (var wave in _waves)
        {
            var origin = wave.Origin ??= FloodOrigin();
            var radius = BigTypeMotion.FloodRadiusAt(now - wave.Start, _bt.HoldStill);
            var extentX = radius + 1f / sx;
            var extentY = extentX * sx / sy;
            _sprites.Set("uRect", new Vector4(origin.X - extentX, origin.Y - extentY, 2f * extentX, 2f * extentY));
            _sprites.Set("uTint", wave.Colour);
            _sprites.Set("uClip0", new Vector4(origin.X * sx, surfaceHeight - (origin.Y * sy), radius * sx, 1f));
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        }
        // Everything after draws as it always has.
        _sprites.Set("uTintAmount", 0f);
        _sprites.Set("uClip0", Vector4.Zero);
        return true;
    }

    /// <summary>A wave that has covered the screen becomes the ground, with every wave before it (before the screen is cleared).</summary>
    private void SettleFlood(float now, float sx, float sy)
    {
        for (var i = _waves.Count - 1; i >= 0; i--)
        {
            var wave = _waves[i];
            var since = now - wave.Start;
            if (BigTypeMotion.Flooding(since, _bt.HoldStill))
            {
                var centre = (wave.Origin ??= FloodOrigin()) * new Vector2(sx, sy);
                if (!BigTypeMotion.CoversCanvas(centre, BigTypeMotion.FloodRadiusAt(since) * sx, _layout.CanvasWidth * sx, _layout.CanvasHeight * sy))
                    continue;
            }
            _groundTo = wave.Colour;
            _waves.RemoveRange(0, i + 1);
            break;
        }
    }

    /// <summary>Where a flood grows from: the middle of what the ring is going to (the focused tile), or the screen's middle.</summary>
    private Vector2 FloodOrigin()
    {
        if (_target is not { } target)
            return new Vector2(_layout.CanvasWidth / 2f, _layout.CanvasHeight / 2f);
        var at = TargetOnScreen(target);
        return new Vector2(at.X + (target.Width / 2f), at.Y + (target.Height / 2f));
    }

    private void ApplySprite(LauncherSpriteCommand command, float now)
    {
        _live.TryGetValue(command.Id, out var existing);
        if (command.Sprite is not { } sprite)
        {
            if (existing is not null)
            {
                Delete(existing);
                _live.Remove(command.Id);
            }
            _bt.Place(command.Id, null);
            return;
        }
        if (_bigType)
            _bt.Place(command.Id, sprite.Cell);
        if (existing is null)
        {
            var state = new SpriteState(sprite, SpriteTextures.Upload(_gl, sprite.Image.Pixels, repeat: false),
                sprite.LiftShadow is { } shadow ? SpriteTextures.Upload(_gl, shadow.Pixels, repeat: false) : 0)
            {
                BigType = _bigType,
            };
            // A folder opening (M47 B8): its count rolls in from nothing as the folder shows (the mock's roll(fcount, n)).
            if (FolderCountRollStart(sprite, now) is { } rollStart)
                state.Roll = (0, sprite.Image, new StageText([]), rollStart);
            if (sprite.Id == _focused && sprite.Liftable)
            {
                state.LiftFrom = state.LiftTo = sprite.LiftTo;
            }
            if (sprite.Folder is { } art)
                state.Folder = new FolderArtState2D(_gl, art, state.PendingState);
            _live[command.Id] = state;
            return;
        }
        if (!ReferenceEquals(existing.Sprite.Folder, sprite.Folder))
        {
            var current = existing.Folder?.State ?? existing.PendingState;
            existing.Folder?.Dispose();
            existing.Folder = sprite.Folder is { } art ? new FolderArtState2D(_gl, art, current) : null;
        }
        if (!ReferenceEquals(existing.Sprite.Image, sprite.Image))
        {
            // Big Type's counts roll (M47 B5): the old picture stays until the characters that changed have rolled over.
            if (existing.Roll is { } rolling)
                _gl.DeleteTexture(rolling.Texture);
            existing.Roll = null;
            // Within a run of presses (a held direction, quick taps; M47 B11) a count swaps in place rather than rolling again.
            if (_bigType && !_bt.InRun && existing.Sprite.Text is { } before && sprite.Text is { } after &&
                BigTypeMotion.Rolls(before.Text, after.Text, _bt.HoldStill))
                existing.Roll = (existing.Texture, existing.Sprite.Image, before, now);
            else if (existing.Sprite.CacheFace)
            {
                _spareFaces.AddFirst((existing.Sprite.Image.Pixels, existing.Texture));
                if (_spareFaces.Count > 8)
                {
                    _gl.DeleteTexture(_spareFaces.Last!.Value.Texture);
                    _spareFaces.RemoveLast();
                }
            }
            else
                _gl.DeleteTexture(existing.Texture);
            existing.Texture = TextureFor(sprite);
        }
        if (!ReferenceEquals(existing.Sprite.LiftShadow, sprite.LiftShadow))
        {
            if (existing.Shadow != 0)
                _gl.DeleteTexture(existing.Shadow);
            existing.Shadow = sprite.LiftShadow is { } shadow ? SpriteTextures.Upload(_gl, shadow.Pixels, repeat: false) : 0;
        }
        // Re-pictured while lifted (a Big Type tile turns white when focused): it keeps its lift, at its own scale.
        if (sprite.Id == _focused && sprite.Liftable && existing.LiftTo > 1f && existing.LiftTo != sprite.LiftTo)
            existing.LiftTo = sprite.LiftTo;
        existing.Sprite = sprite;
    }

    private uint TextureFor(LauncherSprite sprite)
    {
        if (sprite.CacheFace)
        {
            for (var node = _spareFaces.First; node is not null; node = node.Next)
            {
                if (!ReferenceEquals(node.Value.Pixels, sprite.Image.Pixels))
                    continue;
                _spareFaces.Remove(node);
                return node.Value.Texture;
            }
        }
        return SpriteTextures.Upload(_gl, sprite.Image.Pixels, repeat: false);
    }

    private void ApplyFocus(LauncherFocusCommand focus, float now)
    {
        // Big Type holding still: the lift lands at once too. While the selector is hidden for a folder's zoom it lands where it
        // is going instead of travelling unseen.
        var instant = focus.Instant || (_bigType && _bt.HoldStill);
        if (focus.SpriteId != _focused)
        {
            SetLift(_focused, null, now, instant);
            SetLift(focus.SpriteId, lifted: true, now, instant);
            _focused = focus.SpriteId;
            _focusTime = focus.Instant ? float.NegativeInfinity : now;
            if (_bigType)
                _bt.Focus(focus.SpriteId, now, instant);
        }
        if (_bigType && focus.Selector is not null && focus.Selector != _target)
        {
            var hidden = now - _hiddenSince < LauncherMotion.ZoomSelectorHiddenMilliseconds + LauncherMotion.ZoomSelectorFadeMilliseconds;
            _bt.MoveRing(now, instant || !_selectorShown || hidden, focus.Pop);
        }
        _target = focus.Selector;
        if (focus.Selector is not { } target)
        {
            _selectorShown = false;
            return;
        }
        if (!ReferenceEquals(target.Glow, _glowImage))
        {
            if (_glowTexture != 0)
                _gl.DeleteTexture(_glowTexture);
            _glowImage = target.Glow;
            _glowTexture = target.Glow is { } glow ? SpriteTextures.Upload(_gl, glow.Pixels, repeat: false) : 0;
        }
        if (!_selectorShown || focus.Instant)
        {
            var at = TargetOnScreen(target);
            _x.Snap(at.X);
            _y.Snap(at.Y);
            _w.Snap(target.Width);
            _h.Snap(target.Height);
            _r.Snap(target.Radius);
            _selectorShown = true;
        }
    }

    private void SetLift(string? id, bool? lifted, float now, bool instant)
    {
        if (id is null || !_live.TryGetValue(id, out var state) || !state.Sprite.Liftable)
            return;
        var to = lifted == true ? state.Sprite.LiftTo : 1f;
        state.LiftFrom = instant ? to : state.Lift(now);
        state.LiftTo = to;
        state.LiftStart = instant ? float.NegativeInfinity : now;
    }

    private float TrackOffset(LauncherTrack track) => track switch
    {
        LauncherTrack.Page => (float)_page.Value,
        LauncherTrack.Column => (float)_column.Value,
        LauncherTrack.Dots => (float)(-_page.Value / _layout.PageWidth * _layout.DotPitch) + _layout.DotsShift,
        _ => 0f,
    };

    private Vector2 TargetOnScreen(LauncherSelectorTarget target) => new(target.X + TrackOffset(target.Track), target.Y);

    // ---- A frame ----

    public bool Draw(float now, int surfaceWidth, int surfaceHeight)
    {
        var dt = float.IsNaN(_lastFrame) ? 1f / 60f : Math.Clamp((now - _lastFrame) / 1000f, 0f, 0.05f);
        _lastFrame = now;
        _lastNow = now;
        var animating = _page.Step(dt) | _column.Step(dt);

        // The selector follows its target where the target's track has it now (Big Type's ring takes its own path, M47 B5).
        if (_target is { } target && _selectorShown && !_bigType)
        {
            var at = TargetOnScreen(target);
            _x.Retarget(at.X);
            _y.Retarget(at.Y);
            _w.Retarget(target.Width);
            _h.Retarget(target.Height);
            _r.Retarget(target.Radius);
            animating |= _x.Step(dt) | _y.Step(dt) | _w.Step(dt) | _h.Step(dt) | _r.Step(dt);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, TargetFramebuffer);
        _gl.Viewport(0, 0, (uint)surfaceWidth, (uint)surfaceHeight);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        var sx = surfaceWidth / _layout.CanvasWidth;
        var sy = surfaceHeight / _layout.CanvasHeight;
        SettleFlood(now, sx, sy);
        var ground = Ground();
        _gl.ClearColor(ground.X, ground.Y, ground.Z, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _sprites.Use();
        _sprites.Set("uTexture", 0);
        _sprites.Set("uCanvas", new Vector2(_layout.CanvasWidth, _layout.CanvasHeight));

        var flooding = _waves.Count > 0 && DrawFlood(now, sx, sy, surfaceHeight);
        animating |= flooding;
        _probeTag = flooding ? "flood" : null;
        var zoom = _bigType ? BigTypeZoom(now) : Zoom(now);
        animating |= zoom.Animating;

        // A Title page covers everything under it with its own opaque background.
        var title = _live.Values.Any(state => state.Sprite.Group == LauncherGroup.Title);
        if (title)
        {
            DrawGroup(LauncherGroup.Title, now, 1f, 1f, Vector2.Zero, null, sx, sy, surfaceHeight);
            animating |= DrawProgress(now);
            animating |= _live.Values.Any(state => !float.IsNegativeInfinity(state.LaunchStart) && now - state.LaunchStart < LaunchMilliseconds);
            animating |= DrawSelector(now);
            animating |= _live.Values.Any(s => now - s.LiftStart < s.LiftMilliseconds || s.Roll is not null);
            _gl.Disable(EnableCap.ScissorTest);
            _gl.BindVertexArray(0);
            _gl.BindTexture(TextureTarget.Texture2D, 0);
            if (!animating)
                _lastFrame = float.NaN;
            return animating;
        }

        DrawGroup(LauncherGroup.Background, now, 1f, 1f, Vector2.Zero, null, sx, sy, surfaceHeight);
        DrawGroup(LauncherGroup.Home, now, zoom.HomeOpacity, 1f, Vector2.Zero, _layout.HomeClip, sx, sy, surfaceHeight, zoom);
        DrawGroup(LauncherGroup.Dots, now, zoom.HomeOpacity, 1f, Vector2.Zero, null, sx, sy, surfaceHeight);
        if (_bigType)
            DrawBigTypeFolder(now, zoom, sx, sy, surfaceHeight);
        else if (_closing.Count > 0)
        {
            if (zoom.Animating && !_zoomOpening)
                DrawSprites(_closing, now, zoom.SheetOpacity, zoom.SheetScale, _zoomOrigin, _layout.FolderClip, sx, sy, surfaceHeight);
            else
            {
                DeleteAll(_closing);
                _closing = [];
            }
        }
        if (!_bigType && (_folderOpen || (zoom.Animating && _zoomOpening)))
            DrawGroup(LauncherGroup.Folder, now, zoom.SheetOpacity, zoom.SheetScale, _zoomOrigin, _layout.FolderClip, sx, sy, surfaceHeight);
        DrawGroup(LauncherGroup.Dock, now, 1f, 1f, Vector2.Zero, null, sx, sy, surfaceHeight);

        animating |= DrawSelector(now);
        animating |= _live.Values.Any(s => now - s.LiftStart < s.LiftMilliseconds || s.Folder?.IsAnimating(now) == true || s.Roll is not null);
        if (_bigType)
            animating |= _bt.IsMoving(now, hops: !zoom.Animating) || LettersMoving(now);

        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindVertexArray(0);
        _gl.BindTexture(TextureTarget.Texture2D, 0);
        if (!animating)
            _lastFrame = float.NaN;
        return animating;
    }

    private readonly record struct ZoomState(bool Animating, float HomeOpacity, float SheetOpacity, float SheetScale, float TileScale, float TileOpacity);

    private ZoomState Zoom(float now)
    {
        var t = now - _zoomStart;
        if (_zoomOpening)
        {
            var done = t >= LauncherMotion.ZoomSheetInDelay + LauncherMotion.ZoomSheetInMilliseconds;
            var home = 1f - LauncherMotion.ZoomEase((t - LauncherMotion.ZoomHomeOutDelay) / LauncherMotion.ZoomHomeOutMilliseconds);
            var sheetT = (t - LauncherMotion.ZoomSheetInDelay) / LauncherMotion.ZoomSheetInMilliseconds;
            var sheetScale = LauncherMotion.ZoomSheetFromScale + ((1f - LauncherMotion.ZoomSheetFromScale) * LauncherMotion.Sheet(sheetT));
            var sheetOpacity = Math.Clamp(sheetT / LauncherMotion.ZoomSheetFadeInPortion, 0f, 1f);
            var tileT = t / LauncherMotion.ZoomTileOutMilliseconds;
            var ease = LauncherMotion.ZoomEase(tileT);
            if (done)
                return new ZoomState(false, _folderOpen ? 0f : 1f, 1f, 1f, 1f, 1f);
            return new ZoomState(true, home, sheetOpacity, sheetScale,
                tileT >= 1 ? 1f : 1f + ((LauncherMotion.ZoomTileScale - 1f) * ease), tileT >= 1 ? 0f : 1f - ease);
        }
        else
        {
            var done = t >= Math.Max(LauncherMotion.ZoomTileInDelay + LauncherMotion.ZoomTileInMilliseconds,
                Math.Max(LauncherMotion.ZoomSheetOutMilliseconds, LauncherMotion.ZoomHomeInDelay + LauncherMotion.ZoomHomeInMilliseconds));
            if (done)
                return new ZoomState(false, _folderOpen ? 0f : 1f, 1f, 1f, 1f, 1f);
            var sheetT = t / LauncherMotion.ZoomSheetOutMilliseconds;
            var sheetScale = 1f - ((1f - LauncherMotion.ZoomSheetFromScale) * LauncherMotion.ZoomEase(sheetT));
            var sheetOpacity = sheetT < LauncherMotion.ZoomSheetFadeOutBy
                ? 1f - LauncherMotion.ZoomEase(sheetT / LauncherMotion.ZoomSheetFadeOutBy)
                : 0f;
            var home = LauncherMotion.ZoomEase((t - LauncherMotion.ZoomHomeInDelay) / LauncherMotion.ZoomHomeInMilliseconds);
            var tileT = (t - LauncherMotion.ZoomTileInDelay) / LauncherMotion.ZoomTileInMilliseconds;
            var tileScale = LauncherMotion.ZoomTileScale + ((1f - LauncherMotion.ZoomTileScale) * LauncherMotion.TileReturn(tileT));
            return new ZoomState(true, home, sheetOpacity, sheetScale, tileScale, LauncherMotion.CubicOut(tileT));
        }
    }

    private void DrawGroup(LauncherGroup group, float now, float opacity, float groupScale, Vector2 pivot, StageRect? clip,
        float sx, float sy, int surfaceHeight, ZoomState zoom = default)
    {
        var sprites = _live.Values.Where(s => s.Sprite.Group == group)
            .OrderBy(s => s.Sprite.Z).ThenBy(s => s.Sprite.Id == _focused ? 1 : 0);
        DrawSprites(sprites, now, opacity, groupScale, pivot, clip, sx, sy, surfaceHeight, zoom);
    }

    private void DrawSprites(IEnumerable<SpriteState> sprites, float now, float opacity, float groupScale, Vector2 pivot,
        StageRect? clip, float sx, float sy, int surfaceHeight, ZoomState zoom = default)
    {
        if (opacity <= 0f)
            return;
        if (clip is { } c)
        {
            // The clip zooms with its group (the Avalonia page clipped inside the scaled surface).
            var x0 = pivot.X + ((c.X - pivot.X) * groupScale);
            var y0 = pivot.Y + ((c.Y - pivot.Y) * groupScale);
            var w = c.Width * groupScale;
            var h = c.Height * groupScale;
            _groupScissor = ((int)MathF.Floor(x0 * sx), (int)MathF.Floor(surfaceHeight - ((y0 + h) * sy)),
                (int)MathF.Ceiling(w * sx), (int)MathF.Ceiling(h * sy));
            _gl.Enable(EnableCap.ScissorTest);
            _gl.Scissor(_groupScissor.Value.X, _groupScissor.Value.Y, (uint)_groupScissor.Value.Width, (uint)_groupScissor.Value.Height);
        }
        else
        {
            _groupScissor = null;
            _gl.Disable(EnableCap.ScissorTest);
        }
        foreach (var state in sprites)
        {
            var sprite = state.Sprite;
            var offset = new Vector2(TrackOffset(sprite.Track), 0);
            // Big Type's grid tiles make room, bob and hop (M47 B5); a zooming page holds its tiles still.
            if (_bigType && sprite.Cell is not null)
                offset += _bt.Offset(sprite.Id, now, hops: !zoom.Animating && sprite.Group != LauncherGroup.Title);
            var lift = sprite.Liftable ? state.Lift(now) : 1f;
            var spriteOpacity = opacity;
            var sinceLaunch = float.IsNegativeInfinity(state.LaunchStart) ? float.NaN : now - state.LaunchStart;
            if (_bigType)
            {
                // M47 B8: a folder opening or closing, the launch's flight and the shelf coming back pose each tile.
                if (!BigTypePose(state, now, sinceLaunch, ref offset, ref lift, ref spriteOpacity))
                    continue;
                if (DrawLetters(state, now, spriteOpacity))
                    continue;
            }
            else if (sinceLaunch >= 0 && sinceLaunch < LaunchMilliseconds)
            {
                var (launchScale, launchY, launchOpacity) = Launch(sinceLaunch / LaunchMilliseconds);
                lift *= launchScale;
                offset.Y += state.LaunchDown ? -launchY : launchY;
                spriteOpacity *= launchOpacity;
            }
            else if (sinceLaunch >= LaunchMilliseconds)
            {
                continue; // lifted off: gone until the page closes
            }
            if (sprite.Id == _zoomTile && zoom.Animating)
            {
                lift *= zoom.TileScale;
                spriteOpacity *= zoom.TileOpacity;
            }
            if (state.Shadow != 0 && sprite.LiftShadow is { } shadow)
            {
                var shadowOpacity = state.ShadowOpacity(now) * spriteOpacity;
                Quad(state.Shadow, shadow, shadowOpacity, lift, sprite.PivotX, sprite.PivotY, offset, groupScale, pivot);
            }
            if (state.Roll is { } roll)
            {
                if (DrawRoll(state, roll, now, spriteOpacity, offset, sx, sy, surfaceHeight))
                    continue;
                _gl.DeleteTexture(roll.Texture);
                state.Roll = null;
            }
            Quad(state.Texture, sprite.Image, spriteOpacity, lift, sprite.PivotX, sprite.PivotY, offset, groupScale, pivot);
            state.Folder?.Draw(now, (texture, image, affine) =>
                Quad(texture, image, spriteOpacity, lift, sprite.PivotX, sprite.PivotY, offset, groupScale, pivot, affine));
        }
    }

    /// <summary>
    /// A rolling text (M47 B5): the new picture's unchanged parts as they are, each changed character's old glyph leaving upward and
    /// the new one rising in, each under a scissor of its own cell. False once the roll has finished.
    /// </summary>
    private bool DrawRoll(SpriteState state, (uint Texture, StageImage Image, StageText Text, float Start) roll, float now, float opacity,
        Vector2 offset, float sx, float sy, int surfaceHeight)
    {
        var image = state.Sprite.Image;
        if (state.Sprite.Text is not { } text ||
            BigTypeRollDraw.Pieces(roll.Text, text, image.X, image.Y, image.Width, image.Height, now - roll.Start) is not { } pieces)
            return false;
        foreach (var piece in pieces)
        {
            // The cell's window on the surface, inside the group's own clip.
            var left = (int)MathF.Floor((piece.ClipX + offset.X) * sx);
            var bottom = (int)MathF.Floor(surfaceHeight - ((piece.ClipY + offset.Y + piece.ClipHeight) * sy));
            var right = (int)MathF.Ceiling((piece.ClipX + offset.X + piece.ClipWidth) * sx);
            var top = (int)MathF.Ceiling(surfaceHeight - ((piece.ClipY + offset.Y) * sy));
            if (_groupScissor is { } group)
            {
                left = Math.Max(left, group.X);
                bottom = Math.Max(bottom, group.Y);
                right = Math.Min(right, group.X + group.Width);
                top = Math.Min(top, group.Y + group.Height);
            }
            if (right <= left || top <= bottom)
                continue;
            _gl.Enable(EnableCap.ScissorTest);
            _gl.Scissor(left, bottom, (uint)(right - left), (uint)(top - bottom));
            var (texture, picture) = piece.Source == BigTypeRollSource.Old ? (roll.Texture, roll.Image) : (state.Texture, image);
            Quad(texture, picture, opacity * piece.Opacity, 1f, 0f, 0f, offset + new Vector2(0f, piece.OffsetY), 1f, Vector2.Zero);
        }
        // Put back the group's clip for the sprites after this one.
        if (_groupScissor is { } back)
            _gl.Scissor(back.X, back.Y, (uint)back.Width, (uint)back.Height);
        else
        {
            _gl.Disable(EnableCap.ScissorTest);
        }
        return true;
    }

    private void Quad(uint texture, StageImage image, float opacity, float scale, float pivotX, float pivotY, Vector2 offset,
        float groupScale, Vector2 groupPivot, StageAffine? part = null)
    {
        if (opacity <= 0f)
            return;
        var local = part ?? StageAffine.Identity;
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _sprites.Set("uLinear", new Vector4(local.M11, local.M12, local.M21, local.M22));
        _sprites.Set("uTranslate", new Vector2(local.M31, local.M32));
        _sprites.Set("uRect", new Vector4(image.X, image.Y, image.Width, image.Height));
        _sprites.Set("uScale", new Vector3(scale, pivotX, pivotY));
        _sprites.Set("uOffset", offset);
        _sprites.Set("uGroup", new Vector3(groupScale, groupPivot.X, groupPivot.Y));
        _sprites.Set("uUv", new Vector4(0, 0, 1, 1));
        _sprites.Set("uOpacity", opacity);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
    }

    // The launch, as the Avalonia Launcher played it: press to 0.94 and pop to 1.1 in 260 ms, then lift off
    // upward (-1100 px) shrinking to 0.7 and fading over 420 ms.
    private const float LaunchPress = 260f;
    private const float LaunchLift = 420f;
    private const float LaunchMilliseconds = LaunchPress + LaunchLift;

    private static (float Scale, float OffsetY, float Opacity) Launch(float t)
    {
        var press = LaunchPress * 0.4f / LaunchMilliseconds;
        var pop = LaunchPress / LaunchMilliseconds;
        static float Sine(float x) => 0.5f - (0.5f * MathF.Cos(MathF.PI * Math.Clamp(x, 0f, 1f)));
        float scale;
        if (t < press)
            scale = 1f + ((0.94f - 1f) * Sine(t / press));
        else if (t < pop)
            scale = 0.94f + ((1.1f - 0.94f) * Sine((t - press) / (pop - press)));
        else
            scale = 1.1f + ((0.7f - 1.1f) * Sine((t - pop) / (1f - pop)));
        var lift = t < pop ? 0f : (t - pop) / (1f - pop);
        return (scale, -1100f * lift * lift * lift, t < pop ? 1f : 1f - lift);
    }

    /// <summary>The launching panel's indeterminate bar: a 40% segment sweeping across the track every 1.5 s.</summary>
    private bool DrawProgress(float now)
    {
        if (_progress is not { } track)
            return false;
        _gl.Disable(EnableCap.ScissorTest);
        _ring.Use();
        _ring.Set("uCanvas", new Vector2(_layout.CanvasWidth, _layout.CanvasHeight));
        _ring.Set("uLinear", new Vector4(1, 0, 0, 1));
        _ring.Set("uTranslate", Vector2.Zero);
        _ring.Set("uScale", new Vector3(1, 0, 0));
        _ring.Set("uOffset", Vector2.Zero);
        _ring.Set("uGroup", new Vector3(1, 0, 0));
        _ring.Set("uUv", new Vector4(0, 0, 1, 1));
        _ring.Set("uOpacity", 1f);
        void Bar(float x, float width, Vector4 colour)
        {
            _ring.Set("uRect", new Vector4(x, track.Y, width, track.Height));
            _ring.Set("uSize", new Vector2(width, track.Height));
            _ring.Set("uRadius", track.Height / 2);
            _ring.Set("uFill", colour);
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        }
        // #29465088 and #5B58D9, premultiplied.
        var trackAlpha = 0x29 / 255f;
        Bar(track.X, track.Width, new Vector4(0x46 / 255f * trackAlpha, 0x50 / 255f * trackAlpha, 0x88 / 255f * trackAlpha, trackAlpha));
        var segment = track.Width * 0.4f;
        var phase = ((now - _progressStart) % 1500f) / 1500f;
        var x = track.X - segment + ((track.Width + segment) * phase);
        var left = Math.Max(track.X, x);
        var right = Math.Min(track.X + track.Width, x + segment);
        if (right > left)
            Bar(left, right - left, new Vector4(0x5B / 255f, 0x58 / 255f, 0xD9 / 255f, 1f));
        _ring.Set("uFill", Vector4.Zero);
        _sprites.Use();
        return true;
    }

    private bool DrawSelector(float now)
    {
        _breathing = false;
        if (!_selectorShown || _target is null)
            return false;
        // Big Type's moves (M47 B8): the ring waits while the grid moves, then pops in or simply lands.
        if (_bigType && BigTypeRingWaits(now))
            return true;
        var animating = false;
        var hidden = now - _hiddenSince;
        var opacity = 1f;
        if (hidden < LauncherMotion.ZoomSelectorHiddenMilliseconds + LauncherMotion.ZoomSelectorFadeMilliseconds)
        {
            opacity = Math.Clamp((hidden - LauncherMotion.ZoomSelectorHiddenMilliseconds) / LauncherMotion.ZoomSelectorFadeMilliseconds, 0f, 1f);
            animating = true;
        }
        float x, y, w, h, radius;
        if (_bigType)
        {
            // Big Type's ring (M47 B5): one timed path from where it was drawn to where its target is now, then a breath at rest.
            var at = TargetOnScreen(_target);
            var ring = _bt.Ring(new BigTypeRing(at.X, at.Y, _target.Width, _target.Height, _target.Radius), now, out var ringOpacity, out var moving);
            // The probe's ring trace (M47 B11, "ring"): where the ring is drawn and where its target is, each frame.
            if (ProbeRing)
                LastRing = FormattableString.Invariant($"ring t={now:0.0} at={ring.X:0.0},{ring.Y:0.0},{ring.Width:0.0},{ring.Height:0.0} target={at.X:0.0},{at.Y:0.0},{_target.Width:0.0},{_target.Height:0.0} moving={moving}");
            animating |= moving;
            opacity *= ringOpacity;
            (x, y, w, h, radius) = (ring.X, ring.Y, ring.Width, ring.Height, ring.Radius);
        }
        else
        {
            (x, y, w, h, radius) = ((float)_x.Value, (float)_y.Value, (float)_w.Value, (float)_h.Value, (float)_r.Value);
        }
        var sinceBump = now - _bumpStart;
        if (_bigType)
        {
            // Big Type's bump is the mock's (M47 B11): the ring goes 18 px toward the wall and back in 220 ms.
            if (sinceBump < BigTypeMotion.BumpMilliseconds)
            {
                var by = BigTypeMotion.Bump(sinceBump);
                x += _bump.X * by;
                y += _bump.Y * by;
                animating = true;
            }
        }
        else if (sinceBump < LauncherMotion.BumpMilliseconds)
        {
            var amount = LauncherMotion.Bump(sinceBump / LauncherMotion.BumpMilliseconds) * LauncherMotion.BumpFraction;
            x += _bump.X * amount * w;
            y += _bump.Y * amount * h;
            animating = true;
        }
        if (opacity <= 0f)
            return animating;

        _gl.Disable(EnableCap.ScissorTest);
        var sinceFocus = now - _focusTime;
        if (_glowTexture != 0 && _glowImage is { } glow)
        {
            // Stepped on the 30 Hz grid from the focus change, so every frame of the breath is one the clock chose.
            var stepped = sinceFocus < 0f ? sinceFocus
                : MathF.Floor(sinceFocus / LauncherMotion.BreathStepMilliseconds) * LauncherMotion.BreathStepMilliseconds;
            var breath = LauncherMotion.Breath(stepped);
            _breathing = sinceFocus < LauncherMotion.BreathHalfCycleMilliseconds * LauncherMotion.BreathHalfCycles;
            _sprites.Use();
            var gx = x + (w / 2) - (glow.Width / 2);
            var gy = y + (h / 2) - (glow.Height / 2);
            Quad(_glowTexture, glow with { X = gx, Y = gy }, opacity * breath, 1f, 0, 0, Vector2.Zero, 1f, Vector2.Zero);
        }
        _ring.Use();
        _ring.Set("uCanvas", new Vector2(_layout.CanvasWidth, _layout.CanvasHeight));
        _ring.Set("uRect", new Vector4(x, y, w, h));
        _ring.Set("uLinear", new Vector4(1, 0, 0, 1));
        _ring.Set("uTranslate", Vector2.Zero);
        _ring.Set("uScale", new Vector3(1, 0, 0));
        _ring.Set("uOffset", Vector2.Zero);
        _ring.Set("uGroup", new Vector3(1, 0, 0));
        _ring.Set("uUv", new Vector4(0, 0, 1, 1));
        _ring.Set("uSize", new Vector2(w, h));
        _ring.Set("uRadius", Math.Max(0f, radius));
        _ring.Set("uFill", Vector4.Zero);
        var bands = _target.Bands ?? LauncherRingBands.Classic;
        _ring.Set("uInsets", new Vector3(bands.Inset1, bands.Inset2, bands.Inset3));
        _ring.Set("uBand1", bands.Band1);
        _ring.Set("uBand2", bands.Band2);
        _ring.Set("uBand3", bands.Band3);
        _ring.Set("uOpacity", opacity);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _sprites.Use();
        return animating;
    }

    private void Delete(SpriteState state)
    {
        state.Folder?.Dispose();
        if (state.Roll is { } roll)
            _gl.DeleteTexture(roll.Texture);
        _gl.DeleteTexture(state.Texture);
        if (state.Shadow != 0)
            _gl.DeleteTexture(state.Shadow);
    }

    private void DeleteAll(IEnumerable<SpriteState> states)
    {
        foreach (var state in states)
            Delete(state);
    }

    public void Dispose()
    {
        foreach (var face in _spareFaces)
            _gl.DeleteTexture(face.Texture);
        _spareFaces.Clear();
        DeleteAll(_live.Values);
        DeleteAll(_closing);
        DisposeLetters();
        _live.Clear();
        _closing.Clear();
        if (_glowTexture != 0)
            _gl.DeleteTexture(_glowTexture);
        if (_white != 0)
            _gl.DeleteTexture(_white);
        _gl.DeleteVertexArray(_vao);
        _sprites.Dispose();
        _ring.Dispose();
    }
}

/// <summary>Uploading a picture as a texture, shared by the native scenes.</summary>
internal static class SpriteTextures
{
    private const int SwizzleR = 0x8E42;
    private const int SwizzleB = 0x8E44;
    private const int GlRed = 0x1903;
    private const int GlBlue = 0x1905;

    public static unsafe uint Upload(GL gl, StagePixels pixels, bool repeat)
    {
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        fixed (byte* data = pixels.Data)
        {
            gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)pixels.Width, (uint)pixels.Height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }
        if (pixels.IsBgra)
        {
            gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)SwizzleR, GlBlue);
            gl.TexParameter(TextureTarget.Texture2D, (TextureParameterName)SwizzleB, GlRed);
        }
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        var wrap = (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, wrap);
        return texture;
    }
}
