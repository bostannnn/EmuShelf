using System.Numerics;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The Stage's name under the Big Type look (M47 B6, from R1's spike): one quad per letter, cut from the name's atlas, each with its
/// own offset, tilt and opacity from <see cref="BigTypeMotion"/>. The old name's letters leave while the new one's arrive; at rest the
/// letters hop with the Launcher's tiles. Lives on the Stage's render thread; the compositor sets the ink (and, during a wipe, the
/// circle) before each <see cref="Draw"/>, so one frame may draw the letters more than once.
/// </summary>
internal sealed class BigTypeStageLayer : IDisposable
{
    /// <summary>Draws a textured quad on the canvas: texture, rect, opacity, uv, and the letter's own transform.</summary>
    public delegate void QuadDrawer(uint texture, float x, float y, float width, float height, float opacity, Vector4 uv, StageAffine affine);

    private readonly GL _gl;
    private NameState? _name;
    private readonly List<NameState> _leaving = [];

    public BigTypeStageLayer(GL gl) => _gl = gl;

    /// <summary>Whether there is a name to draw (arriving, at rest or leaving).</summary>
    public bool IsEmpty => _name is null && _leaving.Count == 0;

    /// <summary>The name showing (null when there is none), for the letters' hop.</summary>
    public BigTypeLetters? Name => _name?.Name;

    public void Apply(StageNameCommand command, float now)
    {
        if (_name is { } current)
        {
            if (command.Animate)
            {
                current.Start = now;
                current.Exit = command.Entrance;
                _leaving.Add(current);
            }
            else
            {
                _gl.DeleteTexture(current.Texture);
            }
        }
        if (!command.Animate)
        {
            foreach (var leaving in _leaving)
                _gl.DeleteTexture(leaving.Texture);
            _leaving.Clear();
        }
        _name = command.Name is { } name
            ? new NameState(name, SpriteTextures.Upload(_gl, name.Atlas, repeat: false), command.Entrance,
                command.Animate ? now : float.NegativeInfinity)
            : null;
    }

    /// <summary>Lets go of the leaving names that have gone. Returns whether any letter still moves (arriving or leaving).</summary>
    public bool Advance(float now, bool holdStill)
    {
        for (var i = _leaving.Count - 1; i >= 0; i--)
        {
            var leaving = _leaving[i];
            if (now - leaving.Start >= BigTypeMotion.DepartureMilliseconds(leaving.Name.Letters.Count, holdStill))
            {
                _gl.DeleteTexture(leaving.Texture);
                _leaving.RemoveAt(i);
            }
        }
        return _leaving.Count > 0 || Arriving(now, holdStill);
    }

    /// <summary>Whether the showing name's letters are still landing.</summary>
    public bool Arriving(float now, bool holdStill) =>
        _name is { } name && now - name.Start < BigTypeMotion.ArrivalMilliseconds(name.Name.Letters.Count, holdStill);

    /// <summary>
    /// Draws the leaving letters and the arriving (or resting) name. <paramref name="sinceHop"/> is how far into an idle hop's wave the
    /// letters are (null: none); a name still landing does not hop.
    /// </summary>
    public void Draw(float now, bool holdStill, float? sinceHop, QuadDrawer quad)
    {
        foreach (var leaving in _leaving)
        {
            var name = leaving.Name;
            var count = name.Letters.Count;
            foreach (var letter in name.Letters)
            {
                if (BigTypeMotion.Depart(leaving.Exit, letter.Order, count, now - leaving.Start, holdStill) is { } pose)
                    DrawLetter(leaving, letter, pose, quad);
            }
        }
        if (_name is not { } state)
            return;
        var arriving = Arriving(now, holdStill);
        foreach (var letter in state.Name.Letters)
        {
            var pose = arriving
                ? BigTypeMotion.Arrive(state.Entrance, letter.Order, state.Name.Letters.Count, now - state.Start, holdStill)
                : BigTypeLetterPose.Rest;
            if (!arriving && sinceHop is { } hop)
                pose = pose with { Y = BigTypeMotion.LetterHop(letter.Order, hop) };
            DrawLetter(state, letter, pose, quad);
        }
    }

    private static void DrawLetter(NameState state, BigTypeLetter letter, BigTypeLetterPose pose, QuadDrawer quad)
    {
        var opacity = pose.Opacity * state.Name.Opacity;
        if (opacity <= 0f)
            return;
        var atlas = state.Name.Atlas;
        var uv = new Vector4(letter.AtlasX / atlas.Width, letter.AtlasY / atlas.Height,
            (letter.AtlasX + letter.Width) / atlas.Width, (letter.AtlasY + letter.Height) / atlas.Height);
        var size = state.Name.Size;
        quad(state.Texture, letter.X, letter.Y, letter.Width, letter.Height, opacity, uv,
            Pose(letter.X + (letter.Width / 2f), letter.Y + (letter.Height / 2f), pose.X * size, pose.Y * size, pose.Degrees));
    }

    /// <summary>
    /// A letter's transform on the canvas: turned <paramref name="degrees"/> about its cell's middle (y down, so a positive angle turns
    /// clockwise as CSS does), then moved by (<paramref name="dx"/>, <paramref name="dy"/>).
    /// </summary>
    internal static StageAffine Pose(float cx, float cy, float dx, float dy, float degrees)
    {
        if (degrees == 0f)
            return new StageAffine(1f, 0f, 0f, 1f, dx, dy);
        var (sin, cos) = MathF.SinCos(degrees * MathF.PI / 180f);
        return new StageAffine(cos, sin, -sin, cos,
            cx - ((cx * cos) - (cy * sin)) + dx,
            cy - ((cx * sin) + (cy * cos)) + dy);
    }

    public void Dispose()
    {
        if (_name is { } name)
            _gl.DeleteTexture(name.Texture);
        foreach (var leaving in _leaving)
            _gl.DeleteTexture(leaving.Texture);
        _leaving.Clear();
        _name = null;
    }

    private sealed class NameState(BigTypeLetters name, uint texture, BigTypeEntrance entrance, float start)
    {
        public BigTypeLetters Name { get; } = name;
        public uint Texture { get; } = texture;
        public BigTypeEntrance Entrance { get; } = entrance;

        /// <summary>When it arrived; once it leaves, when it started leaving.</summary>
        public float Start { get; set; } = start;

        /// <summary>The arrival that sent it away (which way it leaves).</summary>
        public BigTypeEntrance Exit { get; set; }
    }
}
