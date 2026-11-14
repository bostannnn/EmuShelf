using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Checks the Stage compositor's Big Type motion (M47 B6) under a real GL context, at chosen moments: `--stage-letters [--out dir]`.
/// White cells stand in for a name's letters (one atlas cell, as every letter of a name shares its atlas). Each letter is read back
/// where <see cref="BigTypeMotion"/> says it is mid-flight (arriving from the move, leaving the other way); the letters hop on the
/// tiles' schedule after a press; held still the new name is there at once. A system change does not start its backdrop on the frame
/// the command lands on; its circle grows from below the focused tile's x, the old backdrop showing outside it, and text across the
/// circle's edge is in each backdrop's own ink on each side. Writes a PNG per moment; exits non-zero on a failure.
/// </summary>
internal static class StageLettersCheck
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const float Size = 200f;
    private const float CellWidth = 100f;
    private const float CellHeight = 150f;
    private const float OldRow = 300f;
    private const float NewRow = 650f;
    private static readonly float[] Columns = [100f, 300f, 500f, 700f];

    private static readonly Vector3 Ground = new Vector3(0x2B, 0x5B, 0xC7) / 255f;
    private static readonly Vector3 Deep = BigTypeVeil.Deep(new Vector3(0x2B, 0x5B, 0xC7)) / 255f;
    private static readonly Vector3 Grey = new Vector3(0x40, 0x40, 0x40) / 255f;

    public static int Run(GL gl, string outputDirectory)
    {
        var failures = 0;
        void Check(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
        Directory.CreateDirectory(outputDirectory);

        // ---- Letters arriving and leaving ----
        {
            var (framebuffer, colour) = Target(gl);
            var stage = Stage(gl, framebuffer, holdStill: false);
            stage.Apply(new StageNameCommand(Name("Ab", OldRow, 2), BigTypeEntrance.Rise, Animate: false), 0f);
            stage.Draw(0f, Width, Height);
            const float at = 1000f;
            stage.Apply(new StageNameCommand(Name("Cdef", NewRow, 4), BigTypeEntrance.Right), at);
            foreach (var since in new[] { 0f, 80f, 200f, 260f, 1000f })
            {
                stage.Draw(at + since, Width, Height);
                var frame = Read(gl, framebuffer);
                PngWriter.Write(Path.Combine(outputDirectory, $"letters-{since:0}.png"), Width, Height, frame);
                for (var i = 0; i < 2; i++)
                {
                    var pose = BigTypeMotion.Depart(BigTypeEntrance.Right, i, 2, since);
                    Check($"{since:0} ms: old letter {i} {Describe(pose)}", LetterAt(frame, Columns[i], OldRow, pose));
                }
                for (var i = 0; i < 4; i++)
                {
                    var pose = BigTypeMotion.Arrive(BigTypeEntrance.Right, i, 4, since);
                    Check($"{since:0} ms: new letter {i} {Describe(pose)}", LetterAt(frame, Columns[i], NewRow, pose));
                }
            }
            Check("1000 ms: nothing moves any more", !stage.Draw(at + 1100f, Width, Height) || stage.ProbeTag is null);

            // The letters' hop: 4.8 s after a press, on the tiles' schedule, each letter rising 0.07 em in turn.
            const float press = 3000f;
            stage.Apply(new StageInputCommand(), press);
            Check("before the hop the Stage only asks to wake for it", stage.NextFrameMilliseconds is { } wake && wake > 4000f);
            var peak = Enumerable.Range(0, 561).OrderBy(t => BigTypeMotion.LetterHop(0, BigTypeMotion.LetterHopDelay + t)).First();
            var hopAt = press + BigTypeMotion.HopAfterMilliseconds + BigTypeMotion.LetterHopDelay + peak;
            Check("the hop draws at the panel's rate", stage.Draw(hopAt, Width, Height));
            var hop = Read(gl, framebuffer);
            PngWriter.Write(Path.Combine(outputDirectory, "letters-hop.png"), Width, Height, hop);
            var rise = BigTypeMotion.LetterHop(0, BigTypeMotion.LetterHopDelay + peak) * Size;
            Check($"hop: the first letter is {-rise:0.0} px up", LetterAt(hop, Columns[0], NewRow, BigTypeLetterPose.Rest with { Y = rise / Size }));
            var third = BigTypeLetterPose.Rest with { Y = BigTypeMotion.LetterHop(3, BigTypeMotion.LetterHopDelay + peak) };
            Check("hop: the last letter is later in its hop", LetterAt(hop, Columns[3], NewRow, third));
            stage.Dispose();
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
        }

        // ---- Held still: the new name is there at once, the old one gone in 60 ms ----
        {
            var (framebuffer, colour) = Target(gl);
            var stage = Stage(gl, framebuffer, holdStill: true);
            stage.Apply(new StageNameCommand(Name("Ab", OldRow, 2), BigTypeEntrance.Rise, Animate: false), 0f);
            stage.Draw(0f, Width, Height);
            stage.Apply(new StageNameCommand(Name("Cdef", NewRow, 4), BigTypeEntrance.Right), 1000f);
            stage.Draw(1000f, Width, Height);
            var frame = Read(gl, framebuffer);
            PngWriter.Write(Path.Combine(outputDirectory, "letters-still.png"), Width, Height, frame);
            Check("still: every new letter is at rest on the first frame", Enumerable.Range(0, 4).All(i => LetterAt(frame, Columns[i], NewRow, BigTypeLetterPose.Rest)));
            stage.Draw(1060f, Width, Height);
            frame = Read(gl, framebuffer);
            Check("still: the old name has gone 60 ms later", LetterAt(frame, Columns[0], OldRow, null) && LetterAt(frame, Columns[1], OldRow, null));
            stage.Apply(new StageInputCommand(), 2000f);
            Check("still: no hop is waited for", stage.NextFrameMilliseconds is null);
            stage.Dispose();
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
        }

        // ---- The wipe ----
        {
            var (framebuffer, colour) = Target(gl);
            var stage = Stage(gl, framebuffer, holdStill: false, pictured: false);
            var started = new List<(string Id, float At)>();
            var clock = 0f;
            stage.BackdropStarted += id => started.Add((id, clock));
            // A wide white text across the left zone: dark over ps2-towers, white over ps2-trails-v3 (the baked table).
            var white = new StagePixels(1, 1, [255, 255, 255, 255], IsBgra: false);
            stage.Apply(new StageChromeCommand("line", new StageImage(white, 0, 1000, 1920, 30), StageInkZone.Left), 0f);
            stage.Apply(new StageBackdropCommand(Pictured(Grey), ["ps2-towers"], WipeOriginX: 960f), 0f);
            Check("the first backdrop starts at once (nothing is on screen to wipe over)", started.Count == 1 && started[0].Id == "ps2-towers");
            for (clock = 0f; clock <= 1000f; clock += 16f)
                stage.Draw(clock, Width, Height);

            const float at = 2000f;
            const float originX = 700f;
            clock = at;
            stage.Apply(new StageBackdropCommand(Pictured(Grey), ["ps2-trails-v3"], WipeOriginX: originX), at);
            stage.Draw(at, Width, Height);
            Check("the frame the system change lands on does not start the next backdrop", started.Count == 1);
            float wipeAt = 0f;
            for (clock = at + 16f; clock <= at + 110f + 100f; clock += 16f)
            {
                stage.Draw(clock, Width, Height);
                wipeAt = clock;
            }
            Check("the next backdrop started on the frame after (prepared before its circle grows)",
                started.Count == 2 && started[1].Id == "ps2-trails-v3" && started[1].At == at + 16f);
            Check("…and its circle grows from the command's 110 ms delay (it was ready in time)", started[1].At < at + BigTypeMotion.WipeDelay);
            Check("the wipe is tagged for the frame probe", stage.ProbeTag is "wipe" or "letters+wipe");
            var frame = Read(gl, framebuffer);
            PngWriter.Write(Path.Combine(outputDirectory, "wipe-mid.png"), Width, Height, frame);
            var radius = BigTypeMotion.WipeRadiusAt(wipeAt - at - BigTypeMotion.WipeDelay);
            // The text's row is 1015 on the canvas; the circle's middle 40 below the bottom edge at x 700.
            var dy = (Height + BigTypeMotion.WipeBelow) - 1015f;
            var dx = MathF.Sqrt((radius * radius) - (dy * dy));
            var inside = (int)(originX + dx - 6f);
            var outside = (int)(originX + dx + 6f);
            Check($"mid-wipe (radius {radius:0}): the text just inside the circle is white, for the new backdrop",
                Close(Pixel(frame, inside, 1015), Vector3.One));
            Check($"mid-wipe: the text just outside it is the dark ink, for the old one ({Pixel(frame, outside, 1015)} at {outside}, {Pixel(frame, outside + 40, 1015)} further)",
                Close(Pixel(frame, outside, 1015), Deep));
            // (The one antialiased pixel on the circle's edge is left out: there the backdrop shows through a little, as at any edge.)
            var edge = originX + dx;
            Check("mid-wipe: no pixel of the text in neither ink across the edge",
                Enumerable.Range(inside, outside - inside).Where(x => Math.Abs(x + 0.5f - edge) > 1.5f)
                    .All(x => Between(Pixel(frame, x, 1015), Deep, Vector3.One)));
            for (clock = wipeAt + 16f; clock <= at + 110f + 800f; clock += 16f)
                stage.Draw(clock, Width, Height);
            frame = Read(gl, framebuffer);
            PngWriter.Write(Path.Combine(outputDirectory, "wipe-done.png"), Width, Height, frame);
            Check("after the wipe the text is white all along", Close(Pixel(frame, 10, 1015), Vector3.One) && Close(Pixel(frame, 1900, 1015), Vector3.One));
            Check("after the wipe nothing is tagged", stage.ProbeTag is null);

            // Held still the next backdrop cuts in once ready, with no circle.
            stage.Apply(new StageMotionCommand(HoldStill: true, BigType: true), at + 2000f);
            stage.Apply(new StageBackdropCommand(Pictured(Grey), ["ps2-towers"], WipeOriginX: originX), at + 2000f);
            stage.Draw(at + 2000f, Width, Height);
            stage.Draw(at + 2016f, Width, Height);
            stage.Draw(at + 2032f, Width, Height);
            frame = Read(gl, framebuffer);
            Check("still: the system change cuts in whole once ready (dark ink all along)",
                Close(Pixel(frame, 10, 1015), Deep) && Close(Pixel(frame, 1900, 1015), Deep));
            stage.Dispose();
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
        }

        Console.WriteLine(failures == 0 ? "All Big Type letter and wipe checks passed." : $"{failures} Big Type letter and wipe check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    private static StageCompositor Stage(GL gl, uint framebuffer, bool holdStill, bool pictured = true)
    {
        var stage = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
        stage.Apply(new StageMotionCommand(holdStill, BigType: true), 0f);
        stage.Apply(new StageCanvasCommand(1920f), 0f);
        stage.Apply(new StageInkCommand(Ground, Deep), 0f);
        // A pictured backdrop (no shader): the left zone's ink is the dark one.
        if (pictured)
            stage.Apply(new StageBackdropCommand(Pictured(Grey)), 0f);
        return stage;
    }

    private static StageBackdrop Pictured(Vector3 colour)
    {
        var pixels = new StagePixels(1, 1, [(byte)(colour.X * 255), (byte)(colour.Y * 255), (byte)(colour.Z * 255), 255], IsBgra: false);
        return new StageBackdrop(new StageImage(pixels, 0, 0, 1920, 1080), null, null, null, Vector2.Zero, 0, 0);
    }

    // A name of `count` letters in one row, every letter the atlas's one white cell.
    private static BigTypeLetters Name(string text, float row, int count)
    {
        var data = new byte[(int)CellWidth * (int)CellHeight * 4];
        Array.Fill(data, (byte)255);
        var atlas = new StagePixels((int)CellWidth, (int)CellHeight, data, IsBgra: false);
        var letters = Enumerable.Range(0, count).Select(i => new BigTypeLetter(Columns[i], row, CellWidth, CellHeight, 0, 0, i)).ToArray();
        return new BigTypeLetters(text, atlas, letters, Size);
    }

    private static string Describe(BigTypeLetterPose? pose) => pose is not { } p ? "has gone"
        : p == BigTypeLetterPose.Rest ? "is at rest"
        : p.Opacity <= 0f ? "has not come in yet"
        : $"is {p.X * Size:0.0} px across, {p.Opacity:0.00} opaque";

    /// <summary>
    /// Whether a letter's cell is drawn where the pose puts it: its middle (moved by the pose) in the dark ink at the pose's opacity over
    /// the grey backdrop; with no pose (gone) or a clear one, its resting middle is the backdrop.
    /// </summary>
    private static bool LetterAt(byte[] frame, float x, float y, BigTypeLetterPose? pose)
    {
        if (pose is not { } p || p.Opacity <= 0f)
            return Close(Pixel(frame, (int)(x + (CellWidth / 2)), (int)(y + (CellHeight / 2))), Grey);
        var cx = x + (CellWidth / 2) + (p.X * Size);
        var cy = y + (CellHeight / 2) + (p.Y * Size);
        var expected = (Deep * p.Opacity) + (Grey * (1f - p.Opacity));
        // The cell's edge is where the pose says, too: just inside it the ink, just outside the backdrop.
        var left = x + (p.X * Size);
        var top = y + (p.Y * Size);
        return Close(Pixel(frame, (int)cx, (int)cy), expected, 3f / 255f) &&
            Close(Pixel(frame, (int)MathF.Round(left + 2f), (int)cy), expected, 3f / 255f) &&
            Close(Pixel(frame, (int)MathF.Round(left - 2f), (int)cy), Grey, 3f / 255f) &&
            Close(Pixel(frame, (int)cx, (int)MathF.Round(top - 2f)), Grey, 3f / 255f);
    }

    private static (uint Framebuffer, uint Colour) Target(GL gl)
    {
        var framebuffer = gl.GenFramebuffer();
        var colour = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, Width, Height);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colour);
        return (framebuffer, colour);
    }

    private static byte[] Read(GL gl, uint framebuffer)
    {
        var pixels = new byte[Width * Height * 4];
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        gl.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, new Span<byte>(pixels));
        var stride = Width * 4;
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < Height; y++)
            Array.Copy(pixels, (Height - 1 - y) * stride, flipped, y * stride, stride);
        return flipped;
    }

    private static Vector3 Pixel(byte[] frame, int x, int y)
    {
        var i = ((y * Width) + x) * 4;
        return new Vector3(frame[i], frame[i + 1], frame[i + 2]) / 255f;
    }

    private static bool Close(Vector3 a, Vector3 b, float tolerance = 2f / 255f) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Z - b.Z) <= tolerance;

    // On the line between two colours (each channel between theirs): an antialiased edge between the two inks, not a third colour.
    private static bool Between(Vector3 c, Vector3 a, Vector3 b) =>
        In(c.X, a.X, b.X) && In(c.Y, a.Y, b.Y) && In(c.Z, a.Z, b.Z);

    private static bool In(float v, float a, float b) => v >= Math.Min(a, b) - (3f / 255f) && v <= Math.Max(a, b) + (3f / 255f);
}
