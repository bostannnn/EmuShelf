using System.Numerics;
using EmuShelf.Rendering.Gl;
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
public sealed class LauncherCompositor : INativeScene
{
    private readonly GL _gl;
    private readonly GlProgram _sprites;
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

    private float _lastFrame = float.NaN;

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

    public float? NextFrameMilliseconds => null;

    private sealed class SpriteState(LauncherSprite sprite, uint texture, uint shadow)
    {
        public LauncherSprite Sprite { get; set; } = sprite;
        public FolderArtState2D? Folder { get; set; }
        public uint Texture { get; set; } = texture;
        public uint Shadow { get; set; } = shadow;
        public FolderArtState PendingState { get; set; } = FolderArtState.Closed;
        public float LiftFrom { get; set; } = 1f;
        public float LiftTo { get; set; } = 1f;
        public float LiftStart { get; set; } = float.NegativeInfinity;

        public float Lift(float now)
        {
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
                break;
            case LauncherFolderCommand folder:
                _folderOpen = folder.Open;
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
                        _live.Remove(state.Sprite.Id);
                }
                break;
            case LauncherFolderStateCommand folderState:
                if (_live.TryGetValue(folderState.Id, out var holder))
                {
                    holder.PendingState = folderState.State;
                    holder.Folder?.SetState(folderState.State, now, folderState.Animate);
                }
                break;
            case LauncherBumpCommand bump:
                _bump = new Vector2(bump.DirectionX, bump.DirectionY);
                _bumpStart = now;
                break;
        }
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
            return;
        }
        if (existing is null)
        {
            var state = new SpriteState(sprite, SpriteTextures.Upload(_gl, sprite.Image.Pixels, repeat: false),
                sprite.LiftShadow is { } shadow ? SpriteTextures.Upload(_gl, shadow.Pixels, repeat: false) : 0);
            if (sprite.Id == _focused && sprite.Liftable)
            {
                state.LiftFrom = state.LiftTo = LauncherMotion.LiftScale;
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
            _gl.DeleteTexture(existing.Texture);
            existing.Texture = SpriteTextures.Upload(_gl, sprite.Image.Pixels, repeat: false);
        }
        if (!ReferenceEquals(existing.Sprite.LiftShadow, sprite.LiftShadow))
        {
            if (existing.Shadow != 0)
                _gl.DeleteTexture(existing.Shadow);
            existing.Shadow = sprite.LiftShadow is { } shadow ? SpriteTextures.Upload(_gl, shadow.Pixels, repeat: false) : 0;
        }
        existing.Sprite = sprite;
    }

    private void ApplyFocus(LauncherFocusCommand focus, float now)
    {
        if (focus.SpriteId != _focused)
        {
            SetLift(_focused, 1f, now, focus.Instant);
            SetLift(focus.SpriteId, LauncherMotion.LiftScale, now, focus.Instant);
            _focused = focus.SpriteId;
            _focusTime = focus.Instant ? float.NegativeInfinity : now;
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

    private void SetLift(string? id, float to, float now, bool instant)
    {
        if (id is null || !_live.TryGetValue(id, out var state) || !state.Sprite.Liftable)
            return;
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
        var animating = _page.Step(dt) | _column.Step(dt);

        // The selector follows its target where the target's track has it now.
        if (_target is { } target && _selectorShown)
        {
            var at = TargetOnScreen(target);
            _x.Retarget(at.X);
            _y.Retarget(at.Y);
            _w.Retarget(target.Width);
            _h.Retarget(target.Height);
            _r.Retarget(target.Radius);
            animating |= _x.Step(dt) | _y.Step(dt) | _w.Step(dt) | _h.Step(dt) | _r.Step(dt);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)surfaceWidth, (uint)surfaceHeight);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.96f, 0.97f, 0.98f, 1f);
        _gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _sprites.Use();
        _sprites.Set("uTexture", 0);
        _sprites.Set("uCanvas", new Vector2(_layout.CanvasWidth, _layout.CanvasHeight));

        var sx = surfaceWidth / _layout.CanvasWidth;
        var sy = surfaceHeight / _layout.CanvasHeight;
        var zoom = Zoom(now);
        animating |= zoom.Animating;

        DrawGroup(LauncherGroup.Background, now, 1f, 1f, Vector2.Zero, null, sx, sy, surfaceHeight);
        DrawGroup(LauncherGroup.Home, now, zoom.HomeOpacity, 1f, Vector2.Zero, _layout.HomeClip, sx, sy, surfaceHeight, zoom);
        DrawGroup(LauncherGroup.Dots, now, zoom.HomeOpacity, 1f, Vector2.Zero, null, sx, sy, surfaceHeight);
        if (_closing.Count > 0)
        {
            if (zoom.Animating && !_zoomOpening)
                DrawSprites(_closing, now, zoom.SheetOpacity, zoom.SheetScale, _zoomOrigin, _layout.FolderClip, sx, sy, surfaceHeight);
            else
            {
                DeleteAll(_closing);
                _closing = [];
            }
        }
        if (_folderOpen || (zoom.Animating && _zoomOpening))
            DrawGroup(LauncherGroup.Folder, now, zoom.SheetOpacity, zoom.SheetScale, _zoomOrigin, _layout.FolderClip, sx, sy, surfaceHeight);
        DrawGroup(LauncherGroup.Dock, now, 1f, 1f, Vector2.Zero, null, sx, sy, surfaceHeight);

        animating |= DrawSelector(now);
        animating |= _live.Values.Any(s => now - s.LiftStart < LauncherMotion.LiftMilliseconds || s.Folder?.IsAnimating(now) == true);

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
            .OrderBy(s => s.Sprite.Id == _focused ? 1 : 0);
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
            _gl.Enable(EnableCap.ScissorTest);
            _gl.Scissor((int)MathF.Floor(x0 * sx), (int)MathF.Floor(surfaceHeight - ((y0 + h) * sy)),
                (uint)MathF.Ceiling(w * sx), (uint)MathF.Ceiling(h * sy));
        }
        else
        {
            _gl.Disable(EnableCap.ScissorTest);
        }
        foreach (var state in sprites)
        {
            var sprite = state.Sprite;
            var offset = new Vector2(TrackOffset(sprite.Track), 0);
            var lift = sprite.Liftable ? state.Lift(now) : 1f;
            var spriteOpacity = opacity;
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
            Quad(state.Texture, sprite.Image, spriteOpacity, lift, sprite.PivotX, sprite.PivotY, offset, groupScale, pivot);
            state.Folder?.Draw(now, (texture, image, affine) =>
                Quad(texture, image, spriteOpacity, lift, sprite.PivotX, sprite.PivotY, offset, groupScale, pivot, affine));
        }
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

    private bool DrawSelector(float now)
    {
        if (!_selectorShown || _target is null)
            return false;
        var animating = false;
        var hidden = now - _hiddenSince;
        var opacity = 1f;
        if (hidden < LauncherMotion.ZoomSelectorHiddenMilliseconds + LauncherMotion.ZoomSelectorFadeMilliseconds)
        {
            opacity = Math.Clamp((hidden - LauncherMotion.ZoomSelectorHiddenMilliseconds) / LauncherMotion.ZoomSelectorFadeMilliseconds, 0f, 1f);
            animating = true;
        }
        var bump = Vector2.Zero;
        var sinceBump = now - _bumpStart;
        if (sinceBump < LauncherMotion.BumpMilliseconds)
        {
            var amount = LauncherMotion.Bump(sinceBump / LauncherMotion.BumpMilliseconds) * LauncherMotion.BumpFraction;
            bump = new Vector2(_bump.X * amount * (float)_w.Value, _bump.Y * amount * (float)_h.Value);
            animating = true;
        }
        if (opacity <= 0f)
            return animating;
        var x = (float)_x.Value + bump.X;
        var y = (float)_y.Value + bump.Y;
        var w = (float)_w.Value;
        var h = (float)_h.Value;

        _gl.Disable(EnableCap.ScissorTest);
        var sinceFocus = now - _focusTime;
        if (_glowTexture != 0 && _glowImage is { } glow)
        {
            var breath = LauncherMotion.Breath(sinceFocus);
            animating |= sinceFocus < LauncherMotion.BreathHalfCycleMilliseconds * LauncherMotion.BreathHalfCycles;
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
        _ring.Set("uRadius", Math.Max(0f, (float)_r.Value));
        _ring.Set("uOpacity", opacity);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _sprites.Use();
        return animating;
    }

    private void Delete(SpriteState state)
    {
        state.Folder?.Dispose();
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
        DeleteAll(_live.Values);
        DeleteAll(_closing);
        _live.Clear();
        _closing.Clear();
        if (_glowTexture != 0)
            _gl.DeleteTexture(_glowTexture);
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
