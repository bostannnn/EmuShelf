using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Checks the Stage compositor's Big Type drawing (M47 B4) under a real GL context: `--stage-bigtype [--out dir]`. White
/// pictures stand in for the text of each zone, over the real animated backdrops; the ink read back under each must be the one
/// the baked table gives the backdrop that is showing (dark: the console's colour mixed toward near-black; or white), the veils
/// must lie under them, and on the narrow screen the 16:9 canvas must sit whole, letterboxed on the ground. Writes one PNG per
/// backdrop. Prints one line per check; exits non-zero on a failure.
/// </summary>
internal static class StageBigTypeCheck
{
    private const int Width = 1920;
    private const int Height = 1080;

    // Where each zone's stand-in text sits on the canvas (the label, the clock, the hints), all solid white.
    private static readonly (StageInkZone Zone, string Key, int X, int Y, int W, int H)[] Blocks =
    [
        (StageInkZone.Left, "label", 88, 64, 300, 50),
        (StageInkZone.Left, "name", 80, 400, 900, 300),
        (StageInkZone.TopRight, "clock", 1560, 60, 270, 80),
        (StageInkZone.BottomRight, "hints", 1240, 940, 590, 66),
    ];

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

        // PS2's ground and its dark ink.
        var ground = new Vector3(0x2B, 0x5B, 0xC7) / 255f;
        var deep = BigTypeVeil.Deep(new Vector3(0x2B, 0x5B, 0xC7)) / 255f;
        var ink = new StageInkCommand(ground, deep);

        foreach (var id in new[] { "ps2-towers", "ps2-trails-v3", "arc-checker", "gc-caustics" })
        {
            var (framebuffer, colour) = Target(gl, Width, Height);
            var compositor = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
            Feed(compositor, ink, id, 1920f);
            for (var now = 0f; now < 1_500f; now += BackdropCatalog.StepMilliseconds)
                compositor.Draw(now, Width, Height);
            var frame = Read(gl, framebuffer, Width, Height);
            PngWriter.Write(Path.Combine(outputDirectory, $"bigtype-{id}.png"), Width, Height, frame);
            var answer = BackdropLegibility.For(id);
            foreach (var block in Blocks)
            {
                var expected = answer[block.Zone].Dark ? deep : Vector3.One;
                var got = Pixel(frame, Width, block.X + (block.W / 2), block.Y + (block.H / 2));
                Check($"{id}: {block.Key} is drawn in {(answer[block.Zone].Dark ? "the dark ink" : "white")} ({Hex(got)})", Close(got, expected));
            }
            // Under the clock's veil, just outside the clock: the veil's tone shows (lighter for a white veil, darker for a dark one).
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
            compositor.Dispose();
        }

        // A pictured fallback (no shader, a light field): the dark ink everywhere, over a white veil (M47 B5).
        {
            var (framebuffer, colour) = Target(gl, Width, Height);
            var compositor = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
            Feed(compositor, ink, null, 1920f);
            compositor.Draw(0f, Width, Height);
            var frame = Read(gl, framebuffer, Width, Height);
            foreach (var block in Blocks)
                Check($"no shader: {block.Key} is in the dark ink", Close(Pixel(frame, Width, block.X + (block.W / 2), block.Y + (block.H / 2)), deep));
            // The left veil (white at 0.28) over the pictured grey at (40, 540): grey × 0.72 + white × 0.28.
            var grey = new Vector3(0x40, 0x40, 0x40) / 255f;
            Check("no shader: the left veil lies on the backdrop at its resting strength",
                Close(Pixel(frame, Width, 40, 540), (grey * 0.72f) + (Vector3.One * 0.28f), 3f / 255f));
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
            compositor.Dispose();
        }

        // The narrow screen (the screens swapped): the 16:9 canvas whole, centred, on the ground.
        {
            const int narrow = 1240;
            var (framebuffer, colour) = Target(gl, narrow, Height);
            var compositor = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
            Feed(compositor, ink, "ps2-towers", 1920f);
            compositor.Draw(0f, narrow, Height);
            var frame = Read(gl, framebuffer, narrow, Height);
            PngWriter.Write(Path.Combine(outputDirectory, "bigtype-narrow.png"), narrow, Height, frame);
            var band = (Height - (int)MathF.Round(1080f * narrow / 1920f)) / 2;
            Check($"narrow: a band of {band} px above the canvas is the ground", Close(Pixel(frame, narrow, 620, band / 2), ground));
            Check("narrow: …and below it", Close(Pixel(frame, narrow, 620, Height - (band / 2)), ground));
            var scale = narrow / 1920f;
            var label = Blocks[0];
            Check("narrow: the label is where the canvas puts it, scaled",
                Close(Pixel(frame, narrow, (int)((label.X + (label.W / 2)) * scale), band + (int)((label.Y + (label.H / 2)) * scale)), deep));
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
            compositor.Dispose();
        }

        // The narrow screen with the Stage laid out for it (M47 B11): a 1240 canvas fills the surface, no bands, the backdrop's middle
        // to its top and bottom edges, the text where the canvas puts it, unscaled.
        {
            const int narrow = 1240;
            var (framebuffer, colour) = Target(gl, narrow, Height);
            var compositor = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
            Feed(compositor, ink, "ps2-towers", narrow);
            for (var now = 0f; now < 1_500f; now += BackdropCatalog.StepMilliseconds)
                compositor.Draw(now, narrow, Height);
            var frame = Read(gl, framebuffer, narrow, Height);
            PngWriter.Write(Path.Combine(outputDirectory, "bigtype-narrow-laid-out.png"), narrow, Height, frame);
            Check("narrow, laid out: the top edge is the backdrop, not the ground", !Close(Pixel(frame, narrow, 620, 2), ground, 6f / 255f));
            Check("narrow, laid out: …and the bottom edge", !Close(Pixel(frame, narrow, 620, Height - 3), ground, 6f / 255f));
            var label = Blocks[0];
            Check("narrow, laid out: the label is where the canvas puts it, in the dark ink",
                Close(Pixel(frame, narrow, label.X + (label.W / 2), label.Y + (label.H / 2)), deep));
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
            compositor.Dispose();
        }

        Console.WriteLine(failures == 0 ? "All Big Type Stage checks passed." : $"{failures} Big Type Stage check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// M47 B13, on request: one Stage frame per console and Recent as Big Type draws it, at full size, for the owner to look at:
    /// `--stage-b13 [--out dir] [--at seconds] [--sharp-width w]`. The real compositor under Big Type, the console's one animation (all
    /// three for the 3DS) recoloured toward its ground and drawn at its Big Type size, the veils, and white stand-ins for the text in
    /// the zone's ink; each stand-in's ink is checked against Big Type's baked answer. Writes <c>owner-id.png</c>.
    /// </summary>
    public static int RunB13(GL gl, string outputDirectory, float atSeconds, string? sharpWidth)
    {
        if (sharpWidth is { } value)
            BigTypeBackdrops.SharpWidthOverride = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var failures = 0;
        Directory.CreateDirectory(outputDirectory);
        foreach (var (owner, backdrops) in BigTypeBackdrops.All)
        {
            var ground = BackdropPreview.Ground(owner);
            var deep = BigTypeVeil.Deep(ground * 255f) / 255f;
            foreach (var entry in backdrops)
            {
                var (framebuffer, colour) = Target(gl, Width, Height);
                var compositor = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
                compositor.SeedBackdrops(13);
                compositor.Apply(new StageMotionCommand(HoldStill: false, BigType: true), 0f);
                var ink = new StageInkCommand(ground, deep);
                Feed(compositor, ink, entry.Id, 1920f, ground);
                // Drawn every frame up to the moment; the ink is sent again every few seconds (it counts as navigation), so the
                // backdrop does not come to rest.
                var end = atSeconds * 1000f;
                for (var now = 0f; now <= end; now += 1000f / 60f)
                {
                    if (now % 4000f < 1000f / 60f)
                        compositor.Apply(ink, now);
                    compositor.Draw(now, Width, Height);
                }
                var frame = Read(gl, framebuffer, Width, Height);
                PngWriter.Write(Path.Combine(outputDirectory, $"{owner}-{entry.Id}.png"), Width, Height, frame);
                var answer = BackdropLegibility.ForBigType(entry.Id);
                foreach (var block in Blocks)
                {
                    var expected = answer[block.Zone].Dark ? deep : Vector3.One;
                    var got = Pixel(frame, Width, block.X + (block.W / 2), block.Y + (block.H / 2));
                    var ok = Close(got, expected);
                    Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {owner} {entry.Id}: {block.Key} in {(answer[block.Zone].Dark ? "the dark ink" : "white")} ({Hex(got)})");
                    if (!ok)
                        failures++;
                }
                gl.DeleteFramebuffer(framebuffer);
                gl.DeleteRenderbuffer(colour);
                compositor.Dispose();
            }
        }
        BigTypeBackdrops.SharpWidthOverride = null;
        Console.WriteLine(failures == 0 ? $"Wrote the Big Type Stage frames to {outputDirectory}." : $"{failures} ink check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    private static void Feed(StageCompositor compositor, StageInkCommand ink, string? backdropId, float canvas, Vector3? recolour = null)
    {
        var grey = new StagePixels(1, 1, [0x40, 0x40, 0x40, 255], IsBgra: false);
        var pictured = new StageBackdrop(new StageImage(grey, 0, 0, 1920, 1080), null, null, null, Vector2.Zero, 0, 0);
        compositor.Apply(new StageCanvasCommand(canvas), 0f);
        compositor.Apply(ink, 0f);
        compositor.Apply(new StageBackdropCommand(pictured, backdropId is null ? null : [backdropId], Ground: recolour), 0f);
        var flatEnd = BigTypeVeil.LeftFlatEnd(80, 1360);
        foreach (var (key, zone) in new[] { ("veil-left", StageInkZone.Left), ("veil-top-right", StageInkZone.TopRight), ("veil-bottom-right", StageInkZone.BottomRight) })
            compositor.Apply(new StageChromeCommand(key, BigTypeVeil.Mask(zone, flatEnd, canvas, 1080), zone, Veil: true), 0f);
        var white = new StagePixels(1, 1, [255, 255, 255, 255], IsBgra: false);
        foreach (var block in Blocks)
            compositor.Apply(new StageChromeCommand(block.Key, new StageImage(white, block.X, block.Y, block.W, block.H), block.Zone), 0f);
    }

    private static (uint Framebuffer, uint Colour) Target(GL gl, int width, int height)
    {
        var framebuffer = gl.GenFramebuffer();
        var colour = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, (uint)width, (uint)height);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colour);
        return (framebuffer, colour);
    }

    private static byte[] Read(GL gl, uint framebuffer, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
        gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, new Span<byte>(pixels));
        var stride = width * 4;
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
            Array.Copy(pixels, (height - 1 - y) * stride, flipped, y * stride, stride);
        return flipped;
    }

    private static Vector3 Pixel(byte[] frame, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        return new Vector3(frame[i], frame[i + 1], frame[i + 2]) / 255f;
    }

    private static bool Close(Vector3 a, Vector3 b, float tolerance = 1.5f / 255f) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Z - b.Z) <= tolerance;

    private static string Hex(Vector3 c) => $"#{(int)MathF.Round(c.X * 255):X2}{(int)MathF.Round(c.Y * 255):X2}{(int)MathF.Round(c.Z * 255):X2}";
}
