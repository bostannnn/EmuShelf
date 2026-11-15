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

    /// <summary>
    /// A new name. A quick one (<see cref="StageNameCommand.Quick"/>: its press came within a run of the one before; M47 B11) follows in
    /// the short form and the names already leaving go at once.
    /// </summary>
    public void Apply(StageNameCommand command, float now)
    {
        // M47 B9: a typed word stays one layer. Its next state replaces it in place (its own letters move); typing beginning sends the
        // name there upward.
        if (command.Typed is { } typed && command.Animate && command.Name is { } word)
        {
            if (_name is { } shown)
            {
                if (shown.Typed is not null && !typed.Begins)
                {
                    _gl.DeleteTexture(shown.Texture);
                }
                else
                {
                    shown.Start = now;
                    shown.UpExit = typed.Begins;
                    shown.Exit = command.Entrance;
                    _leaving.Add(shown);
                }
            }
            _name = new NameState(word, SpriteTextures.Upload(_gl, word.Atlas, repeat: false), command.Entrance, now) { Typed = typed };
            return;
        }
        // M47 B11: a name that comes within a run of the last one (a held direction, quick taps) follows in a short form, and the
        // names already leaving go at once, so a run never queues a landing per tile or stacks names on the Stage.
        var run = command.Animate && command.Quick && _name is { Typed: null };
        if (run)
        {
            foreach (var leaving in _leaving)
                _gl.DeleteTexture(leaving.Texture);
            _leaving.Clear();
        }
        if (_name is { } current)
        {
            if (command.Animate)
            {
                current.Start = now;
                current.Exit = command.Entrance;
                current.QuickExit = run;
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
                command.Animate ? now : float.NegativeInfinity) { Quick = run }
            : null;
    }

    /// <summary>Lets go of the leaving names that have gone. Returns whether any letter still moves (arriving or leaving).</summary>
    public bool Advance(float now, bool holdStill)
    {
        for (var i = _leaving.Count - 1; i >= 0; i--)
        {
            var leaving = _leaving[i];
            var gone = leaving.UpExit
                ? BigTypeTypingMotion.NameOutMilliseconds(leaving.Name.Letters.Count, holdStill)
                : leaving.QuickExit ? BigTypeMotion.RunDepartureMilliseconds(holdStill)
                : BigTypeMotion.DepartureMilliseconds(leaving.Name.Letters.Count, holdStill);
            if (now - leaving.Start >= gone)
            {
                _gl.DeleteTexture(leaving.Texture);
                _leaving.RemoveAt(i);
            }
        }
        return _leaving.Count > 0 || Arriving(now, holdStill);
    }

    /// <summary>Whether the showing name's letters are still landing.</summary>
    public bool Arriving(float now, bool holdStill) =>
        _name is { } name && now - name.Start < (name.Typed is { } typed ? TypedMilliseconds(name, typed, holdStill)
            : name.Quick ? BigTypeMotion.RunArrivalMilliseconds(holdStill)
            : BigTypeMotion.ArrivalMilliseconds(name.Name.Letters.Count, holdStill));

    // How long a typed word's change moves: its new letters landing, its deleted one falling, its size easing.
    private static float TypedMilliseconds(NameState name, BigTypeTyped typed, bool holdStill)
    {
        if (holdStill)
            return 0f;
        var fresh = name.Name.Letters.Count(letter => letter.Order >= typed.FreshFrom);
        var ghost = name.Name.Letters.Any(letter => letter.Order < 0) ? BigTypeTypingMotion.TypedLetterOutMilliseconds : 0f;
        var resize = typed.FromSize > 0f ? BigTypeTypingMotion.TypedResizeMilliseconds : 0f;
        return Math.Max(Math.Max(BigTypeTypingMotion.TypedInMilliseconds(fresh), ghost), resize);
    }

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
                if (letter.Order < 0)
                    continue;
                var leave = leaving.UpExit
                    ? BigTypeTypingMotion.NameOut(letter.Order, now - leaving.Start, holdStill)
                    : leaving.QuickExit ? BigTypeMotion.DepartInRun(leaving.Exit, now - leaving.Start, holdStill)
                    : BigTypeMotion.Depart(leaving.Exit, letter.Order, count, now - leaving.Start, holdStill);
                if (leave is { } pose)
                    DrawLetter(leaving, letter, pose, quad);
            }
        }
        if (_name is not { } state)
            return;
        if (state.Typed is { } typed)
        {
            // At rest a typed word hops with the tiles as any name does (the mock's hop takes whatever word is shown).
            DrawTyped(state, typed, now - state.Start, holdStill, Arriving(now, holdStill) ? null : sinceHop, quad);
            return;
        }
        var arriving = Arriving(now, holdStill);
        foreach (var letter in state.Name.Letters)
        {
            // A typed word's deleted letter is only ever drawn falling (a replay of its atlas holds it too).
            if (letter.Order < 0)
                continue;
            var pose = !arriving ? BigTypeLetterPose.Rest
                : state.Quick ? BigTypeMotion.ArriveInRun(state.Entrance, now - state.Start, holdStill)
                : BigTypeMotion.Arrive(state.Entrance, letter.Order, state.Name.Letters.Count, now - state.Start, holdStill);
            if (!arriving && sinceHop is { } hop)
                pose = pose with { Y = BigTypeMotion.LetterHop(letter.Order, hop) };
            DrawLetter(state, letter, pose, quad);
        }
    }

    /// <summary>
    /// A typed word (M47 B9, the mock's typeWord): its new letters dropping in, the one deleted (order −1) falling off the end, the whole
    /// word easing to its new size about its box's bottom left. At rest it is simply there (a typed word does not hop).
    /// </summary>
    private static void DrawTyped(NameState state, BigTypeTyped typed, float since, bool holdStill, float? sinceHop, QuadDrawer quad)
    {
        var size = state.Name.Size;
        var scale = typed.FromSize > 0f ? BigTypeTypingMotion.TypedSize(typed.FromSize, size, since, holdStill) / size : 1f;
        foreach (var letter in state.Name.Letters)
        {
            BigTypeLetterPose pose;
            if (letter.Order < 0)
            {
                if (BigTypeTypingMotion.TypedOut(since, holdStill) is not { } fall)
                    continue;
                pose = fall;
            }
            else
            {
                pose = letter.Order >= typed.FreshFrom ? BigTypeTypingMotion.TypedIn(letter.Order - typed.FreshFrom, since, holdStill) : BigTypeLetterPose.Rest;
                if (sinceHop is { } hop)
                    pose = pose with { Y = BigTypeMotion.LetterHop(letter.Order, hop) };
            }
            DrawLetter(state, letter, pose, quad, scale, typed.AnchorX, typed.AnchorY);
        }
    }

    private static void DrawLetter(NameState state, BigTypeLetter letter, BigTypeLetterPose pose, QuadDrawer quad) =>
        DrawLetter(state, letter, pose, quad, 1f, 0f, 0f);

    private static void DrawLetter(NameState state, BigTypeLetter letter, BigTypeLetterPose pose, QuadDrawer quad, float scale, float anchorX, float anchorY)
    {
        var opacity = pose.Opacity * state.Name.Opacity;
        if (opacity <= 0f)
            return;
        var atlas = state.Name.Atlas;
        var uv = new Vector4(letter.AtlasX / atlas.Width, letter.AtlasY / atlas.Height,
            (letter.AtlasX + letter.Width) / atlas.Width, (letter.AtlasY + letter.Height) / atlas.Height);
        var size = state.Name.Size;
        var affine = Pose(letter.X + (letter.Width / 2f), letter.Y + (letter.Height / 2f), pose.X * size, pose.Y * size, pose.Degrees);
        if (scale != 1f)
        {
            // Then the whole word scaled about its anchor (x'' = A + s(x' − A)).
            affine = new StageAffine(affine.M11 * scale, affine.M12 * scale, affine.M21 * scale, affine.M22 * scale,
                (affine.M31 * scale) + (anchorX * (1f - scale)), (affine.M32 * scale) + (anchorY * (1f - scale)));
        }
        quad(state.Texture, letter.X, letter.Y, letter.Width, letter.Height, opacity, uv, affine);
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

        /// <summary>It came within a run of the name before (M47 B11): all its letters together, the short way.</summary>
        public bool Quick { get; init; }

        /// <summary>It goes because a name within a run of it came: all its letters together, quickly.</summary>
        public bool QuickExit { get; set; }

        /// <summary>The arrival that sent it away (which way it leaves).</summary>
        public BigTypeEntrance Exit { get; set; }

        /// <summary>A typed word (M47 B9): how it changed; null for any other name.</summary>
        public BigTypeTyped? Typed { get; init; }

        /// <summary>It leaves upward because typing began (M47 B9, the mock's typeWord).</summary>
        public bool UpExit { get; set; }
    }
}
