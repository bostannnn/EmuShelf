using System.Numerics;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Stage;

// The Big Type grid's big moves on the native Launcher (M47 B8), from the maths the Avalonia Launcher shares (BigTypeFolderMotion,
// BigTypeLaunchMotion): a folder opening (home's tiles fall away from it, the games deal in, its name's letters land and its count
// rolls in, the ring pops in on the first game), a folder closing (the games drop out, home's tiles pop back in around it), the launch
// (the tile flies up through the hinge, the grid goes as the takeover starts) and the shelf coming back (the tiles pop in around the
// focused one). Each sprite's pose is worked out where it is drawn; nothing new is pictured.
public sealed partial class LauncherCompositor
{
    private enum BigTypeMoveKind
    {
        Open,
        Close,
        Return,
    }

    private sealed record BigTypeMove(BigTypeMoveKind Kind, float Start, int CentreColumn, int CentreRow, string? Pressed, int FirstColumn)
    {
        public bool RingShown { get; set; }
    }

    private BigTypeMove? _btMove;
    private bool _playing;
    private float _playStart = float.NegativeInfinity;

    private sealed class LettersState(BigTypeLetters letters, uint texture, float start, float fromEm)
    {
        public BigTypeLetters Letters { get; } = letters;
        public uint Texture { get; } = texture;
        public float Start { get; } = start;
        public float FromEm { get; } = fromEm;
    }

    private readonly Dictionary<string, LettersState> _letters = new(StringComparer.Ordinal);

    private float SinceMove(BigTypeMove move, float now) => now - move.Start;

    private void ApplyBigTypeFolder(LauncherZoomCommand zoom, float now)
    {
        var cell = zoom.TileId is { } id && _live.TryGetValue(id, out var tile) ? tile.Sprite.Cell : null;
        var (column, row) = cell is { } c ? (c.Column, c.Row) : (0, 0);
        if (zoom.Open)
        {
            _btMove = new BigTypeMove(BigTypeMoveKind.Open, now, column, row, zoom.TileId, 0);
            return;
        }
        // The folder's sprites are about to be taken away; keep drawing them while they drop.
        DeleteAll(_closing);
        _closing = _live.Values.Where(s => s.Sprite.Group == LauncherGroup.Folder).ToList();
        foreach (var state in _closing)
        {
            _live.Remove(state.Sprite.Id);
            _bt.Place(state.Sprite.Id, null);
        }
        _btMove = new BigTypeMove(BigTypeMoveKind.Close, now, column, row, zoom.TileId, _bt.HopFirstColumn);
    }

    private void ApplyPlay(LauncherPlayCommand play, float now)
    {
        if (play.Playing)
        {
            _playing = true;
            _playStart = play.Animate && !_bt.HoldStill ? now : float.NegativeInfinity;
            _btMove = null;
            return;
        }
        _playing = false;
        foreach (var state in _live.Values)
        {
            state.LaunchStart = float.NegativeInfinity;
            state.LaunchBigType = false;
        }
        if (!play.Animate || _bt.HoldStill)
        {
            _btMove = null;
            return;
        }
        var cell = _focused is { } id && _live.TryGetValue(id, out var focused) ? focused.Sprite.Cell : null;
        var column = cell is { Grid: LauncherGroup.Folder } f ? f.Column - _bt.HopFirstColumn : cell?.Column ?? 0;
        _btMove = new BigTypeMove(BigTypeMoveKind.Return, now, column, cell?.Row ?? 0, null, _bt.HopFirstColumn);
    }

    private void ApplyLetters(LauncherLettersCommand command, float now)
    {
        if (_letters.Remove(command.SpriteId, out var old))
            _gl.DeleteTexture(old.Texture);
        if (command.Letters is not { } letters)
            return;
        _letters[command.SpriteId] = new LettersState(letters, SpriteTextures.Upload(_gl, letters.Atlas, repeat: false),
            command.Animate ? now : float.NegativeInfinity, command.FromEm);
    }

    /// <summary>A folder's count arriving as its folder opens rolls in from nothing when the folder shows.</summary>
    private float? FolderCountRollStart(LauncherSprite sprite, float now) =>
        _bigType && !_bt.HoldStill && _btMove is { Kind: BigTypeMoveKind.Open } move && sprite.Group == LauncherGroup.Folder &&
        sprite.Text is not null && sprite.Cell is null && SinceMove(move, now) < BigTypeFolderMotion.OutHoldMilliseconds + BigTypeFolderMotion.DealTotal
            ? Math.Max(now, move.Start + BigTypeFolderMotion.OutHoldMilliseconds)
            : null;

    /// <summary>The groups' state under Big Type, in the shape today's zoom gives it: home's and the folder's opacity, and whether a move runs.</summary>
    private ZoomState BigTypeZoom(float now)
    {
        var still = _bt.HoldStill;
        var grid = _playing ? BigTypeLaunchMotion.GridOpacity(now - _playStart, still) : 1f;
        var home = _folderOpen ? 0f : 1f;
        var folder = _folderOpen ? 1f : 0f;
        var animating = _playing && now - _playStart < BigTypeLaunchMotion.TakeoverAt + BigTypeLaunchMotion.GridFadeMilliseconds;
        if (_btMove is { } move)
        {
            var since = SinceMove(move, now);
            switch (move.Kind)
            {
                case BigTypeMoveKind.Open:
                    var homeGone = BigTypeFolderMotion.HomeGone(since, still);
                    home = homeGone ? 0f : 1f;
                    folder = homeGone ? 1f : 0f;
                    animating |= since < BigTypeFolderMotion.OpenTotal(still);
                    break;
                case BigTypeMoveKind.Close:
                    home = BigTypeFolderMotion.FolderGone(since, still) ? 1f : 0f;
                    folder = 0f;
                    animating |= since < BigTypeFolderMotion.CloseTotal(still);
                    break;
                default:
                    animating |= since < (still ? 0f : BigTypeFolderMotion.PopInTotal);
                    break;
            }
            if (!animating)
                _btMove = null;
        }
        return new ZoomState(animating, home * grid, folder * grid, 1f, 1f, 1f);
    }

    /// <summary>The closing folder's sprites while they drop, then the open folder's (Big Type).</summary>
    private void DrawBigTypeFolder(float now, ZoomState zoom, float sx, float sy, int surfaceHeight)
    {
        if (_closing.Count > 0)
        {
            if (_btMove is { Kind: BigTypeMoveKind.Close } move && !BigTypeFolderMotion.FolderGone(SinceMove(move, now), _bt.HoldStill))
                DrawSprites(_closing, now, 1f, 1f, Vector2.Zero, _layout.FolderClip, sx, sy, surfaceHeight, zoom);
            else
            {
                DeleteAll(_closing);
                _closing = [];
            }
        }
        if (zoom.SheetOpacity > 0f)
            DrawGroup(LauncherGroup.Folder, now, zoom.SheetOpacity, 1f, Vector2.Zero, _layout.FolderClip, sx, sy, surfaceHeight, zoom);
    }

    /// <summary>
    /// A sprite's pose under Big Type's moves: the opened folder's tile and home's falling away, a folder's games dealing in or dropping
    /// out, the launched tile's flight, the tiles popping in. False when it is not drawn this frame.
    /// </summary>
    private bool BigTypePose(SpriteState state, float now, float sinceLaunch, ref Vector2 offset, ref float lift, ref float opacity)
    {
        var sprite = state.Sprite;
        var still = _bt.HoldStill;
        if (state.LaunchBigType && sinceLaunch >= 0f)
        {
            if (BigTypeLaunchMotion.Flown(sinceLaunch, still))
                return false;
            var (rise, scale) = BigTypeLaunchMotion.Flight(sinceLaunch, sprite.Image.Y, still);
            offset.Y += state.LaunchDown ? -rise : rise;
            lift *= scale;
            return true;
        }
        if (_btMove is not { } move || (sprite.Cell is null && sprite.Group != LauncherGroup.Folder))
            return true;
        var since = SinceMove(move, now);
        var rest = new Vector2(TrackOffset(sprite.Track), 0f);
        switch (move.Kind)
        {
            case BigTypeMoveKind.Open when sprite.Group == LauncherGroup.Home && sprite.Cell is { } home:
            {
                var opened = sprite.Id == move.Pressed;
                var leave = BigTypeFolderMotion.FallAway(home.Column - move.CentreColumn, home.Row - move.CentreRow, opened, since, still);
                var target = rest + new Vector2(leave.ToX, leave.ToY);
                offset += (target - offset) * leave.Progress;
                lift += (leave.ToScale - lift) * leave.Progress;
                opacity *= leave.Opacity;
                return opacity > 0f;
            }
            case BigTypeMoveKind.Open when sprite.Group == LauncherGroup.Folder && sprite.Cell is { } game:
            {
                var pose = BigTypeFolderMotion.Deal(game.Column - _bt.HopFirstColumn, game.Row, since - BigTypeFolderMotion.OutHoldMilliseconds, still);
                offset += new Vector2(pose.X, pose.Y);
                lift *= pose.Scale;
                opacity *= pose.Opacity;
                return opacity > 0f;
            }
            case BigTypeMoveKind.Close when sprite.Group == LauncherGroup.Folder && sprite.Cell is { } game:
            {
                var leave = BigTypeFolderMotion.Drop(game.Column - move.FirstColumn, game.Row, since, still);
                var target = rest + new Vector2(leave.ToX, leave.ToY);
                offset += (target - offset) * leave.Progress;
                lift += (leave.ToScale - lift) * leave.Progress;
                opacity *= leave.Opacity;
                return opacity > 0f;
            }
            case BigTypeMoveKind.Close when sprite.Group == LauncherGroup.Home && sprite.Cell is { } home:
            {
                var pose = BigTypeFolderMotion.PopIn(home.Column - move.CentreColumn, home.Row - move.CentreRow,
                    since - BigTypeFolderMotion.CloseHoldMilliseconds, still);
                lift *= pose.Scale;
                opacity *= pose.Opacity;
                return opacity > 0f;
            }
            case BigTypeMoveKind.Return when sprite.Cell is { } tile:
            {
                var column = tile.Grid == LauncherGroup.Folder ? tile.Column - move.FirstColumn : tile.Column;
                var pose = BigTypeFolderMotion.PopIn(column - move.CentreColumn, tile.Row - move.CentreRow, since, still);
                lift *= pose.Scale;
                opacity *= pose.Opacity;
                return opacity > 0f;
            }
        }
        return true;
    }

    /// <summary>A sprite whose letters are landing draws them in its place (M47 B8, a folder's name); true when it did.</summary>
    private bool DrawLetters(SpriteState state, float now, float opacity)
    {
        if (_letters.Count == 0 || !_letters.TryGetValue(state.Sprite.Id, out var letters) || float.IsNegativeInfinity(letters.Start))
            return false;
        var start = _btMove is { Kind: BigTypeMoveKind.Open } move ? Math.Max(letters.Start, move.Start + BigTypeFolderMotion.OutHoldMilliseconds)
            : letters.Start;
        var since = now - start;
        var count = letters.Letters.Letters.Count;
        if (_bt.HoldStill || since >= BigTypeListMotion.HeadingInTotal(count))
            return false;
        var atlas = letters.Letters.Atlas;
        var size = letters.Letters.Size;
        foreach (var letter in letters.Letters.Letters)
        {
            var (em, degrees, letterOpacity) = BigTypeListMotion.HeadingLetter(letter.Order, since, letters.FromEm);
            if (letterOpacity <= 0f)
                continue;
            var uv = new Vector4(letter.AtlasX / atlas.Width, letter.AtlasY / atlas.Height,
                (letter.AtlasX + letter.Width) / atlas.Width, (letter.AtlasY + letter.Height) / atlas.Height);
            var affine = BigTypeStageLayer.Pose(letter.X + (letter.Width / 2f), letter.Y + (letter.Height / 2f), 0f, em * size, degrees);
            QuadUv(letters.Texture, letter.X, letter.Y, letter.Width, letter.Height, opacity * letterOpacity, uv, affine);
        }
        return true;
    }

    /// <summary>Whether a folder's letters are still landing (they keep frames coming).</summary>
    private bool LettersMoving(float now) => _letters.Values.Any(letters => !float.IsNegativeInfinity(letters.Start) &&
        now - letters.Start < BigTypeFolderMotion.OutHoldMilliseconds + BigTypeListMotion.HeadingInTotal(letters.Letters.Letters.Count));

    /// <summary>
    /// The ring under a move: hidden while the grid moves (a folder opening until its first game has dealt, closing until home is back,
    /// the whole of a game, the shelf's return a moment), then popping in on a folder's first game or simply there.
    /// </summary>
    private bool BigTypeRingWaits(float now)
    {
        if (_playing)
            return true;
        if (_btMove is not { } move)
            return false;
        var since = SinceMove(move, now);
        var still = _bt.HoldStill;
        var waits = move.Kind switch
        {
            BigTypeMoveKind.Open => since < BigTypeFolderMotion.OpenRingAt(still),
            BigTypeMoveKind.Close => since < BigTypeFolderMotion.CloseRingAt(still),
            _ => since < (still ? 0f : BigTypeLaunchMotion.ReturnRingAfter),
        };
        if (waits)
            return true;
        if (!move.RingShown)
        {
            move.RingShown = true;
            if (move.Kind == BigTypeMoveKind.Open)
                _bt.PopIn(now, BigTypeFolderMotion.RingPopFromScale);
            else
                _bt.MoveRing(now, instant: true, pop: false);
        }
        return false;
    }

    private void QuadUv(uint texture, float x, float y, float width, float height, float opacity, Vector4 uv, StageAffine affine)
    {
        if (opacity <= 0f)
            return;
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _sprites.Set("uLinear", new Vector4(affine.M11, affine.M12, affine.M21, affine.M22));
        _sprites.Set("uTranslate", new Vector2(affine.M31, affine.M32));
        _sprites.Set("uRect", new Vector4(x, y, width, height));
        _sprites.Set("uScale", new Vector3(1f, 0f, 0f));
        _sprites.Set("uOffset", Vector2.Zero);
        _sprites.Set("uGroup", new Vector3(1f, 0f, 0f));
        _sprites.Set("uUv", uv);
        _sprites.Set("uOpacity", opacity);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        _sprites.Set("uUv", new Vector4(0, 0, 1, 1));
    }

    private void DisposeLetters()
    {
        foreach (var letters in _letters.Values)
            _gl.DeleteTexture(letters.Texture);
        _letters.Clear();
    }
}
