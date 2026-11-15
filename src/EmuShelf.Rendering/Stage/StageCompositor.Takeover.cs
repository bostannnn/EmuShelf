using System.Numerics;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Stage;

// The launch on the native Stage under the Big Type look (M47 B8, the mock's launch and endPlay), from the maths the Avalonia Stage
// shares (BigTypeLaunchMotion): the case bumps as the tile arrives, the console's colour takes the Stage over through a circle from the
// case, the title's letters land in it and the status line rolls under it; when the game ends it all fades. An error on the line
// shakes it. Drawn over everything, white on the colour, cut to the circle.
public sealed partial class StageCompositor
{
    private sealed class TakeoverState(Vector3 colour, BigTypeLetters? title, uint titleTexture, float start, Vector2 origin)
    {
        public Vector2 Origin { get; } = origin;
        public Vector3 Colour { get; } = colour;
        public BigTypeLetters? Title { get; } = title;
        public uint TitleTexture { get; } = titleTexture;
        public float Start { get; } = start;
        public float EndStart { get; set; } = float.NaN;
    }

    private TakeoverState? _takeover;
    private Layer? _takeoverStatus;
    private StageText? _takeoverStatusText;
    private (Layer Old, StageText Text, float Start)? _takeoverRoll;
    private uint _takeoverWhite;
    private readonly Dictionary<string, float> _shakes = new(StringComparer.Ordinal);

    private void ApplyTakeover(StageTakeoverCommand command, float now)
    {
        if (command.Colour is not { } colour)
        {
            DeleteTakeover();
            return;
        }
        var start = command.Animate && !_holdStill ? now - command.Elapsed : float.NegativeInfinity;
        if (_takeover is { } current && ReferenceEquals(current.Title, command.Title))
        {
            // The same launch: it ends (or is told again).
            if (command.Ending && float.IsNaN(current.EndStart))
                current.EndStart = command.Animate && !_holdStill ? now : float.NegativeInfinity;
            return;
        }
        if (_takeover is { } old && old.TitleTexture != 0)
            _gl.DeleteTexture(old.TitleTexture);
        var texture = command.Title is { } title ? Upload(title.Atlas, repeat: false) : 0u;
        _takeover = new TakeoverState(colour, command.Title, texture, start,
            command.Origin ?? new Vector2(BigTypeLaunchMotion.TakeoverX, BigTypeLaunchMotion.TakeoverY));
        if (command.Ending)
            _takeover.EndStart = float.NegativeInfinity;
    }

    private void ApplyTakeoverStatus(StageTakeoverStatusCommand command, float now)
    {
        if (_takeoverRoll is { } unfinished)
        {
            _gl.DeleteTexture(unfinished.Old.Texture);
            _takeoverRoll = null;
        }
        var old = _takeoverStatus;
        if (command.Image is not { } image)
        {
            if (old is not null)
                _gl.DeleteTexture(old.Texture);
            _takeoverStatus = null;
            _takeoverStatusText = null;
            return;
        }
        var next = new Layer(image, Upload(image.Pixels, repeat: false));
        if (old is not null && _takeoverStatusText is { } before && command.Text is { } after && BigTypeMotion.Rolls(before.Text, after.Text, _holdStill))
            _takeoverRoll = (old, before, now);
        else if (old is not null)
            _gl.DeleteTexture(old.Texture);
        _takeoverStatus = next;
        _takeoverStatusText = command.Text;
    }

    /// <summary>The case's bump as the launched tile arrives: its scale and how far it has risen.</summary>
    private (float Scale, float Rise) CaseBump(float now, ref bool animating)
    {
        if (_takeover is not { } takeover || float.IsNegativeInfinity(takeover.Start) || !float.IsNaN(takeover.EndStart))
            return (1f, 0f);
        var since = now - takeover.Start;
        if (since < BigTypeLaunchMotion.BumpAt + BigTypeLaunchMotion.BumpMilliseconds)
            animating = true;
        return BigTypeLaunchMotion.CaseBump(since, _holdStill);
    }

    /// <summary>A chrome text's shake (an error on the line): how far across it is now.</summary>
    private float ShakeOf(string key, float now, ref bool animating)
    {
        if (_shakes.Count == 0 || !_shakes.TryGetValue(key, out var start))
            return 0f;
        var since = now - start;
        if (_holdStill || since >= BigTypeLaunchMotion.ShakeMilliseconds)
        {
            _shakes.Remove(key);
            return 0f;
        }
        animating = true;
        return BigTypeLaunchMotion.ShakeAt(since);
    }

    /// <summary>Draws the takeover over everything (colour, title, status), cut to its circle; true while it moves.</summary>
    private bool DrawTakeover(float now, (int X, int Y, int Width, int Height, float Scale) fit)
    {
        if (_takeover is not { } takeover)
            return false;
        var still = _holdStill || float.IsNegativeInfinity(takeover.Start);
        var since = still ? float.PositiveInfinity : now - takeover.Start;
        var fade = float.IsNaN(takeover.EndStart) ? 1f
            : float.IsNegativeInfinity(takeover.EndStart) ? 0f
            : BigTypeLaunchMotion.LaunchFade(now - takeover.EndStart, _holdStill);
        var animating = !still && since < BigTypeLaunchMotion.StageTotal(takeover.Title?.Letters.Count ?? 0) || (!float.IsNaN(takeover.EndStart) && fade > 0f);
        if (fade <= 0f)
            return animating;
        var radius = BigTypeLaunchMotion.TakeoverRadiusAt(since, still);
        if (radius <= 0f)
            return animating;
        _clip0 = radius >= BigTypeLaunchMotion.TakeoverRadius ? default
            : Circle(takeover.Origin.X, takeover.Origin.Y, radius, inside: true);
        _clip1 = _clip2 = default;
        if (_takeoverWhite == 0)
            _takeoverWhite = Upload(new StagePixels(1, 1, [255, 255, 255, 255], IsBgra: false), repeat: false);
        DrawQuad(_takeoverWhite, 0f, 0f, _canvasWidth, CanvasHeight, fade, 1f, 0f, 0f, new Vector4(0, 0, 1, 1), tint: takeover.Colour);
        if (takeover.Title is { } title && takeover.TitleTexture != 0)
        {
            foreach (var letter in title.Letters)
            {
                var (em, degrees, opacity) = BigTypeLaunchMotion.TitleLetter(letter.Order, since, still);
                if (opacity <= 0f)
                    continue;
                var uv = new Vector4(letter.AtlasX / title.Atlas.Width, letter.AtlasY / title.Atlas.Height,
                    (letter.AtlasX + letter.Width) / title.Atlas.Width, (letter.AtlasY + letter.Height) / title.Atlas.Height);
                var affine = BigTypeStageLayer.Pose(letter.X + (letter.Width / 2f), letter.Y + (letter.Height / 2f), 0f, em * title.Size, degrees);
                DrawQuad(takeover.TitleTexture, letter.X, letter.Y, letter.Width, letter.Height, opacity * fade, 1f, 0f, 0f, uv, affine);
            }
        }
        if (_takeoverStatus is { } status)
        {
            if (_takeoverRoll is { } roll && _takeoverStatusText is { } text &&
                BigTypeRollDraw.Pieces(roll.Text, text, status.Image.X, status.Image.Y, status.Image.Width, status.Image.Height, now - roll.Start)
                    is { } pieces)
            {
                // The roll's pieces are cut by the scissor, the circle by the shader: the circle has covered the Stage by then.
                DrawRoll(status, roll.Old, pieces, null, fit);
                animating = true;
            }
            else
            {
                if (_takeoverRoll is { } finished)
                {
                    _gl.DeleteTexture(finished.Old.Texture);
                    _takeoverRoll = null;
                }
                DrawLayer(status, fade, 1f, 0, 0);
            }
        }
        _clip0 = default;
        return animating;
    }

    private void DeleteTakeover()
    {
        if (_takeover is { TitleTexture: not 0 } takeover)
            _gl.DeleteTexture(takeover.TitleTexture);
        _takeover = null;
        if (_takeoverStatus is { } status)
            _gl.DeleteTexture(status.Texture);
        _takeoverStatus = null;
        _takeoverStatusText = null;
        if (_takeoverRoll is { } roll)
            _gl.DeleteTexture(roll.Old.Texture);
        _takeoverRoll = null;
    }

    private void DisposeTakeover()
    {
        DeleteTakeover();
        if (_takeoverWhite != 0)
            _gl.DeleteTexture(_takeoverWhite);
        _takeoverWhite = 0;
    }
}
