using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Checks the Launcher compositor's Big Type motion (M47 B5) under a real GL context, at chosen moments: `--launcher-bigtype
/// [--out dir]`. Grey squares stand in for home tiles; the ring is the look's one white band. Moving focus one tile right, the ring
/// covers both tiles at 38% and is squashed on the new one at 74%; the neighbours of the focused tile stand 8 px off; a count
/// rolls only its changed cell, each clipped to itself; the idle hop lifts the first tile; at rest the Launcher asks for 30 Hz for a
/// minute and then nothing. Held still, the ring jumps, nothing travels, hops or rolls. Writes a PNG per moment; exits non-zero on a
/// failure.
/// </summary>
internal static class LauncherBigTypeCheck
{
    private const int Width = 1240;
    private const int Height = 1080;
    private const float Tile = 176f;
    private const float PitchX = 224f;
    private const float PitchY = 196f;
    private const float Left = 100f;
    private const float Top = 100f;

    private static readonly Vector3 Ground = new Vector3(0x2B, 0x5B, 0xC7) / 255f;
    private static readonly Vector3 Grey = new(0.5f, 0.5f, 0.5f);
    private static readonly Vector3 White = Vector3.One;
    private static readonly Vector3 Red = new(1f, 0f, 0f);
    private static readonly Vector3 Green = new(0f, 1f, 0f);

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

        // ---- Moving, then at rest ----
        {
            var (framebuffer, colour) = Target(gl);
            var launcher = new LauncherCompositor(gl, GlslDialect.Desktop, 3, 3) { TargetFramebuffer = framebuffer };
            Feed(launcher, holdStill: false);
            byte[] Frame(float now, string name)
            {
                launcher.Draw(now, Width, Height);
                var frame = Read(gl, framebuffer);
                PngWriter.Write(Path.Combine(outputDirectory, $"launcher-{name}.png"), Width, Height, frame);
                return frame;
            }

            launcher.Apply(new LauncherInputCommand(), 1000f);
            launcher.Apply(Focus(1, 0, instant: false), 1000f);
            var both = Frame(1000f + (0.38f * 300f), "ring-38");
            Check("38%: one ring covers both tiles (its left band at the first tile's left edge)", Close(Pixel(both, 78, 200), White));
            Check("38%: …and its right band at the second tile's right edge", Close(Pixel(both, 522, 200), White));
            Check("38%: …with nothing between the two tiles", Close(Pixel(both, 300, 200), Ground));
            var squash = Frame(1000f + (0.74f * 300f), "ring-74");
            Check("74%: on the new tile, squashed 10 px in from each side (left band at 308)", Close(Pixel(squash, 312, 200), White) && Close(Pixel(squash, 302, 200), Ground));
            Check("74%: …and 6 px taller than the ring at rest", Close(Pixel(squash, 412, 70), White));
            var rest = Frame(1000f + 600f, "ring-rest");
            Check("at rest: the ring's left band 26 px out from the new tile", Close(Pixel(rest, 302, 200), White));
            Check("at rest: the squash is gone", Close(Pixel(rest, 312, 200), Ground));
            Check("neighbours: the tile to the right stands 8 px further right", Close(Pixel(rest, 552, 200), Ground) && Close(Pixel(rest, 558, 200), Grey));
            Check("neighbours: the tile below stands 8 px lower", Close(Pixel(rest, 412, 476), Grey) && Close(Pixel(rest, 412, 482), Ground));
            Check("neighbours: the tile to the left stands 8 px further left", Close(Pixel(rest, 95, 200), Grey) && Close(Pixel(rest, 270, 200), Ground));

            // The count rolls: "12" → "13", only the second cell.
            launcher.Apply(new LauncherSpriteCommand("count", Count("13", Green)), 2000f);
            var roll = Frame(2000f + 100f, "roll-100");
            Check("roll: the unchanged cell shows the new picture", Close(Pixel(roll, 1020, 905), Green));
            Check("roll: the changed cell's old glyph is on its way up (top of its cell)", Close(Pixel(roll, 1060, 905), Red));
            Check("roll: …the new one rising in under it (bottom of its cell)", Close(Pixel(roll, 1060, 945), Green));
            Check("roll: nothing is drawn above the cell (each glyph clipped to its own)", Close(Pixel(roll, 1060, 895), Ground));
            var rolled = Frame(2000f + 700f, "roll-done");
            Check("roll: done, the new picture as it is", Close(Pixel(rolled, 1060, 905), Green));

            // The idle hop: 4.8 s after the press, the first tile (diagonal 0) rises 20 px at its highest.
            var peak = Enumerable.Range(0, 561).OrderBy(t => BigTypeMotion.Hop(0, 0, t)).First();
            var hop = Frame(1000f + BigTypeMotion.HopAfterMilliseconds + peak, "hop");
            Check("hop: the first tile rises 20 px", Close(Pixel(hop, 180, (int)Top - 15), Grey) && Close(Pixel(hop, 180, (int)(Top + Tile) - 15), Ground));
            Check("hop: the frame is drawn at the panel's rate while the wave runs", launcher.Draw(1000f + BigTypeMotion.HopAfterMilliseconds + peak + 1, Width, Height));

            // At rest: 30 Hz for the breath and the bob for a minute after the press, then nothing.
            Check("at rest within the minute: nothing travels", !launcher.Draw(1000f + 28_000f, Width, Height));
            Check("at rest within the minute: the next frame in 33 ms (30 Hz)", launcher.NextFrameMilliseconds is { } step && Math.Abs(step - (1000f / 30f)) < 0.01f);
            // Landed at 1300; its breath is widest half a period in, every 2.2 s.
            var breath = Frame(1300f + (13 * 2200f) + 1100f, "breath");
            Check("breathing: the ring is 3% wider than at rest", Close(Pixel(breath, 296, 200), White));
            // The flood (M47 B6): a new ground grows as a circle from the focused tile (1, 0), whose middle is (412, 188), over the old.
            var violet = new Vector3(0x55, 0x48, 0xC8) / 255f;
            const float floodAt = 40_000f;
            launcher.Apply(new LauncherGroundCommand(violet.X, violet.Y, violet.Z, Flood: true), floodAt);
            var since = Enumerable.Range(1, 639).First(t => BigTypeMotion.FloodRadiusAt(t) >= 150f);
            var radius = BigTypeMotion.FloodRadiusAt(since);
            var flood = Frame(floodAt + since, "flood");
            Check($"flood ({since} ms, radius {radius:0}): the new colour just inside the circle above the tile", Close(Pixel(flood, 412, (int)(188 - radius + 12)), violet));
            Check("flood: the old ground just outside it", Close(Pixel(flood, 412, (int)(188 - radius - 12)), Ground));
            Check("flood: the far corner is still the old ground", Close(Pixel(flood, 1200, 1050), Ground));
            Check("flood: tagged for the frame probe", launcher.ProbeTag == "flood");
            var flooded = Frame(floodAt + BigTypeMotion.FloodMilliseconds, "flood-done");
            Check("flood: covered after 640 ms, the new ground everywhere", Close(Pixel(flooded, 1200, 1050), violet) && Close(Pixel(flooded, 412, 20), violet));
            Check("flood: done, nothing tagged", launcher.ProbeTag is null);
            launcher.Apply(new LauncherGroundCommand(Ground.X, Ground.Y, Ground.Z, Flood: true), floodAt + 1000f);
            Frame(floodAt + 1000f + BigTypeMotion.FloodMilliseconds, "flood-back");

            launcher.Draw(1000f + 63_000f, Width, Height);
            Check("after the minute: no frame is asked for", launcher.NextFrameMilliseconds is null);
            var still = Frame(1000f + 64_000f, "rested");
            Check("after the minute: the ring rests at its own size", Close(Pixel(still, 302, 200), White) && Close(Pixel(still, 296, 200), Ground));

            launcher.Dispose();
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
        }

        // ---- Held still (battery saver, reduced motion) ----
        {
            var (framebuffer, colour) = Target(gl);
            var launcher = new LauncherCompositor(gl, GlslDialect.Desktop, 3, 3) { TargetFramebuffer = framebuffer };
            Feed(launcher, holdStill: true);
            launcher.Apply(new LauncherInputCommand(), 1000f);
            launcher.Apply(Focus(1, 0, instant: false), 1000f);
            var moving = launcher.Draw(1000f + 114f, Width, Height);
            var frame = Read(gl, framebuffer);
            PngWriter.Write(Path.Combine(outputDirectory, "launcher-still.png"), Width, Height, frame);
            Check("still: nothing animates after a move", !moving);
            Check("still: the ring is on the new tile at once", Close(Pixel(frame, 302, 200), White) && Close(Pixel(frame, 78, 200), Ground));
            Check("still: the neighbours are already where they go", Close(Pixel(frame, 552, 200), Ground));
            launcher.Apply(new LauncherSpriteCommand("count", Count("13", Green)), 2000f);
            launcher.Draw(2000f + 150f, Width, Height);
            frame = Read(gl, framebuffer);
            Check("still: the count swaps", Close(Pixel(frame, 1060, 905), Green));
            var peak = Enumerable.Range(0, 561).OrderBy(t => BigTypeMotion.Hop(0, 0, t)).First();
            launcher.Draw(1000f + BigTypeMotion.HopAfterMilliseconds + peak, Width, Height);
            frame = Read(gl, framebuffer);
            Check("still: no hop", Close(Pixel(frame, 180, (int)Top - 15), Ground));
            Check("still: no frame is asked for at rest", launcher.NextFrameMilliseconds is null);
            launcher.Apply(new LauncherGroundCommand(0.3f, 0.2f, 0.7f, Flood: true), 9000f);
            Check("still: a new ground is there at once (no flood)", !launcher.Draw(9000f, Width, Height));
            frame = Read(gl, framebuffer);
            Check("still: …everywhere", Close(Pixel(frame, 1200, 1050), new Vector3(0.3f, 0.2f, 0.7f)));
            launcher.Dispose();
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
        }

        Console.WriteLine(failures == 0 ? "All Big Type Launcher checks passed." : $"{failures} Big Type Launcher check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    // Four home tiles, (0, 0), (1, 0), (2, 0) and (1, 1), on PS2's ground, a count in the dock row, focus on the first tile.
    private static void Feed(LauncherCompositor launcher, bool holdStill)
    {
        launcher.Apply(new LauncherMotionCommand(BigType: true, HoldStill: holdStill), 0f);
        launcher.Apply(new LauncherLayoutCommand(Width, Height, new StageRect(0, 0, Width, Height), new StageRect(0, 0, Width, Height), Width, 264, 30, -17), 0f);
        launcher.Apply(new LauncherGroundCommand(Ground.X, Ground.Y, Ground.Z), 0f);
        foreach (var (column, row) in new[] { (0, 0), (1, 0), (2, 0), (1, 1) })
        {
            var x = Left + (column * PitchX);
            var y = Top + (row * PitchY);
            var id = $"h:0:{(row * 5) + column}";
            launcher.Apply(new LauncherSpriteCommand(id, new LauncherSprite(id, Solid(Grey, x, y, Tile, Tile), LauncherGroup.Home, LauncherTrack.Page,
                Liftable: true, x + (Tile / 2), y + (Tile / 2), Lift: 1.10f, Cell: new LauncherCell(LauncherGroup.Home, 0, column, row))), 0f);
        }
        launcher.Apply(new LauncherSpriteCommand("count", Count("12", Red)), 0f);
        launcher.Apply(new LauncherTracksCommand(0, 0, true, true), 0f);
        launcher.Apply(Focus(0, 0, instant: true), 0f);
        launcher.Draw(0f, Width, Height);
    }

    private static LauncherFocusCommand Focus(int column, int row, bool instant)
    {
        var x = Left + (column * PitchX) - 26f;
        var y = Top + (row * PitchY) - 26f;
        return new LauncherFocusCommand($"h:0:{(row * 5) + column}",
            new LauncherSelectorTarget(x, y, Tile + 52f, Tile + 52f, 50f, LauncherTrack.Page, null, LauncherRingBands.One(8f, 1f, 1f, 1f)), instant);
    }

    // A two-character count at (1000, 900): two 40 px cells 50 tall, em 40, the picture one solid colour.
    private static LauncherSprite Count(string text, Vector3 colour) =>
        new("count", Solid(colour, 1000, 900, 80, 50), LauncherGroup.Dock, LauncherTrack.None, Liftable: false, 0, 0,
            Text: new StageText([new StageTextCell(text[0], 1000, 900, 40, 50, 40), new StageTextCell(text[1], 1040, 900, 40, 50, 40)]));

    private static StageImage Solid(Vector3 colour, float x, float y, float width, float height)
    {
        var w = (int)width;
        var h = (int)height;
        var data = new byte[w * h * 4];
        for (var i = 0; i < data.Length; i += 4)
        {
            data[i] = (byte)MathF.Round(colour.X * 255);
            data[i + 1] = (byte)MathF.Round(colour.Y * 255);
            data[i + 2] = (byte)MathF.Round(colour.Z * 255);
            data[i + 3] = 255;
        }
        return new StageImage(new StagePixels(w, h, data, IsBgra: false), x, y, width, height);
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

    private static bool Close(Vector3 a, Vector3 b, float tolerance = 3f / 255f) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Z - b.Z) <= tolerance;
}
