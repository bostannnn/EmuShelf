using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Shells;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// The model view on the native Stage (M47 B12): `--stage-model [--out dir]`. Each real medium (keep case, jewel case, cartridge, card) and
/// a flat cover is put in the model view over its console's backdrop and drawn at chosen moments: rising through the hinge, at rest, mid
/// showcase (its back), mid flip into the next game, turned by the stick and swinging back, held still. Writes a PNG per moment and checks
/// that the medium stands in the middle of the Stage, is clear at the rise's first frame, and holds still when asked; exits non-zero on a
/// failure. White blocks stand in for the corners' text.
/// </summary>
internal static class StageModelCheck
{
    private const int Width = 1920;
    private const int Height = 1080;

    private static readonly (StageInkZone Zone, string Key, int X, int Y, int W, int H)[] Blocks =
    [
        (StageInkZone.Left, "bt-label", 88, 64, 260, 44),
        (StageInkZone.Left, "bt-line", 88, 906, 520, 42),
        (StageInkZone.Left, "bt-facts", 88, 968, 640, 36),
        (StageInkZone.TopRight, "bt-status", 1440, 48, 392, 92),
        (StageInkZone.BottomRight, "bt-hints", 1180, 950, 652, 58),
    ];

    private static readonly (string Name, MediaShell Shell, string Backdrop, string Colour)[] Media =
    [
        ("keep-case-ps2", MediaShell.DiscKeepCase, "ps2-towers", "#2B5BC7"),
        ("jewel-case-ps1", MediaShell.JewelCase, "field", "#6B6F7E"),
        ("cartridge-snes", MediaShell.SnesCartridge, "snes-lake-v2", "#91408C"),
        ("card-nds", MediaShell.DsCard, "ds-ripples", "#6272B8"),
        ("card-3ds", MediaShell.Nintendo3dsCard, "3ds-tiles", "#C0446E"),
    ];

    public static int Run(GL gl, string outputDirectory)
    {
        var failures = 0;
        void Report(string what, bool ok)
        {
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok)
                failures++;
        }
        Directory.CreateDirectory(outputDirectory);

        // The places, as HandheldTokens works them out on the 16:9 canvas.
        var besideHeight = 552f / (640f / 820f);
        var besideWidth = besideHeight * (1400f / 820f);
        var (besideX, besideY) = (1920f - 248f - 196f, 236f + 276f);
        var (centreX, centreY) = BigTypeModelMotion.Centre(1920f, 552f);
        var frame = (W: besideWidth * BigTypeModelMotion.ModelScale, H: besideHeight * BigTypeModelMotion.ModelScale);
        StageModelCommand Command(StageMedium? medium, StageImage? cover, BigTypeModelChange change) =>
            new(medium, cover, change, besideX, besideY, centreX, centreY, 392f, 552f);

        // Each medium twice: over the plain pictured ground (still, so frames compare and the checks run), then over its console's
        // animated backdrop (the pictures to look at).
        foreach (var (mediaName, shell, animatedBackdrop, colour) in Media)
        foreach (var plain in new[] { true, false })
        {
            var name = plain ? mediaName + "-plain" : mediaName;
            string? backdrop = plain ? null : animatedBackdrop;
            void Check(string what, bool ok)
            {
                if (plain)
                    Report(what, ok);
            }
            MediaShellCatalog.PrepareAsync(shell).GetAwaiter().GetResult();
            var ground = Colour(colour);
            var ink = new StageInkCommand(ground, BigTypeVeil.Deep(ground * 255f) / 255f);
            var medium = new StageMedium(shell, ground, centreX - (frame.W / 2f), centreY - (frame.H / 2f), frame.W, frame.H);
            var cover = new StageMediumCoverCommand(TestCover.Create());

            // Without the model: the backdrop and the corners only, to find the medium by difference.
            var (framebuffer, renderbuffer) = Target(gl);
            var bare = Stage(gl, framebuffer, ink, backdrop, holdStill: false);
            Settle(bare, 0f, 1_500f);
            var empty = Read(gl, framebuffer);
            bare.Dispose();

            var stage = Stage(gl, framebuffer, ink, backdrop, holdStill: false);
            Settle(stage, 0f, 1_500f);
            stage.Apply(cover, 1_500f);
            stage.Apply(Command(medium, null, BigTypeModelChange.Rise), 1_500f);
            foreach (var since in new[] { 0f, 150f, 300f, 550f, 950f, 3_000f, 11_250f, 13_000f })
            {
                stage.Draw(1_500f + since, Width, Height);
                var picture = Read(gl, framebuffer);
                PngWriter.Write(Path.Combine(outputDirectory, $"{name}-rise-{since:0}.png"), Width, Height, picture);
                var box = Changed(empty, picture);
                if (since == 0f)
                    Check($"{name}: clear on the rise's first frame", box is null);
                if (since == 3_000f)
                {
                    Check($"{name}: at rest it is drawn", box is not null);
                    if (box is { } b)
                    {
                        var middle = (b.Left + b.Right) / 2f;
                        Check($"{name}: at rest its middle is the Stage's middle across ({middle:0}, box {b.Left}..{b.Right} x {b.Top}..{b.Bottom})",
                            MathF.Abs(middle - centreX) < 90f);
                        // The medium itself (not its shadow's faint edge) stays clear of every corner's text.
                        var solid = Changed(empty, picture, threshold: 90);
                        var clear = solid is { } m && Blocks.All(t => m.Right < t.X || m.Left > t.X + t.W || m.Bottom < t.Y || m.Top > t.Y + t.H);
                        Check($"{name}: the medium stands clear of the corners' text (box {solid})", clear);
                    }
                }
            }
            // Mid flip into the next game, the way you moved.
            stage.Apply(Command(medium, null, BigTypeModelChange.FlipForward), 16_000f);
            foreach (var since in new[] { 120f, 300f, 720f })
            {
                stage.Draw(16_000f + since, Width, Height);
                PngWriter.Write(Path.Combine(outputDirectory, $"{name}-flip-{since:0}.png"), Width, Height, Read(gl, framebuffer));
            }
            // The stick turns it half round; let go, it swings back.
            stage.Apply(new StageModelHoldCommand(10f, 160f, Held: true), 18_000f);
            stage.Draw(18_000f, Width, Height);
            PngWriter.Write(Path.Combine(outputDirectory, $"{name}-turned.png"), Width, Height, Read(gl, framebuffer));
            stage.Apply(new StageModelHoldCommand(0f, 0f, Held: false), 18_100f);
            foreach (var since in new[] { 200f, 620f })
            {
                stage.Draw(18_100f + since, Width, Height);
                PngWriter.Write(Path.Combine(outputDirectory, $"{name}-swing-{since:0}.png"), Width, Height, Read(gl, framebuffer));
            }
            // Leaving: it drops out the way it came, and is gone.
            stage.Apply(Command(null, null, BigTypeModelChange.Leave), 20_000f);
            stage.Draw(20_120f, Width, Height);
            PngWriter.Write(Path.Combine(outputDirectory, $"{name}-leave-120.png"), Width, Height, Read(gl, framebuffer));
            stage.Draw(20_700f, Width, Height);
            Check($"{name}: gone after leaving", Changed(empty, Read(gl, framebuffer)) is null);
            stage.Dispose();

            // Held still: at rest at once, the same frame later.
            var still = Stage(gl, framebuffer, ink, backdrop, holdStill: true);
            still.Draw(0f, Width, Height);
            var stillEmpty = Read(gl, framebuffer);
            still.Apply(cover, 10f);
            still.Apply(Command(medium, null, BigTypeModelChange.Rise), 10f);
            still.Draw(10f, Width, Height);
            var first = Read(gl, framebuffer);
            still.Draw(5_000f, Width, Height);
            var later = Read(gl, framebuffer);
            PngWriter.Write(Path.Combine(outputDirectory, $"{name}-still.png"), Width, Height, first);
            Check($"{name}: held still it is there at once", Changed(stillEmpty, first) is not null);
            Check($"{name}: held still it does not move", Changed(first, later) is null);
            still.Dispose();
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(renderbuffer);
        }

        // A platform with no authored medium: its flat cover in the same place, turning in by narrowing.
        {
            var ground = Colour("#3D3D48");
            var ink = new StageInkCommand(ground, BigTypeVeil.Deep(ground * 255f) / 255f);
            var art = TestCover.Create(392 * 2, 552 * 2);
            var width = 392f * BigTypeModelMotion.ModelScale;
            var height = 552f * BigTypeModelMotion.ModelScale;
            var image = new StageImage(new StagePixels(art.Width, art.Height, art.Rgba, IsBgra: false), centreX - (width / 2f), centreY - (height / 2f), width, height);
            var (framebuffer, renderbuffer) = Target(gl);
            var stage = Stage(gl, framebuffer, ink, "psp-field-v2", holdStill: false);
            Report("flat cover: drawn (see flat-cover-rise-*.png)", true);
            Settle(stage, 0f, 1_500f);
            stage.Apply(Command(null, image, BigTypeModelChange.Rise), 1_500f);
            foreach (var since in new[] { 150f, 300f, 950f, 3_000f })
            {
                stage.Draw(1_500f + since, Width, Height);
                PngWriter.Write(Path.Combine(outputDirectory, $"flat-cover-rise-{since:0}.png"), Width, Height, Read(gl, framebuffer));
            }
            stage.Dispose();
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(renderbuffer);
        }

        Console.WriteLine(failures == 0 ? "All model view checks passed." : $"{failures} model view check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    private static Vector3 Colour(string hex) => new Vector3(
        Convert.ToInt32(hex[1..3], 16), Convert.ToInt32(hex[3..5], 16), Convert.ToInt32(hex[5..7], 16)) / 255f;

    private static StageCompositor Stage(GL gl, uint framebuffer, StageInkCommand ink, string? backdrop, bool holdStill)
    {
        var stage = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
        stage.Apply(new StageMotionCommand(holdStill, BigType: true), 0f);
        var grey = new StagePixels(1, 1, [0x40, 0x40, 0x40, 255], IsBgra: false);
        stage.Apply(new StageCanvasCommand(1920f), 0f);
        stage.Apply(ink, 0f);
        stage.Apply(new StageBackdropCommand(new StageBackdrop(new StageImage(grey, 0, 0, 1920, 1080), null, null, null, Vector2.Zero, 0, 0),
            backdrop is null ? null : [backdrop]), 0f);
        // The model view's left veil lies behind the lines (here the widest, the facts, to 728).
        foreach (var (key, zone) in new[] { ("veil-left", StageInkZone.Left), ("veil-top-right", StageInkZone.TopRight), ("veil-bottom-right", StageInkZone.BottomRight) })
            stage.Apply(new StageChromeCommand(key, BigTypeVeil.Mask(zone, 728f + BigTypeVeil.PastName, 1920f, 1080f), zone, Veil: true), 0f);
        var white = new StagePixels(1, 1, [255, 255, 255, 255], IsBgra: false);
        foreach (var block in Blocks)
            stage.Apply(new StageChromeCommand(block.Key, new StageImage(white, block.X, block.Y, block.W, block.H), block.Zone), 0f);
        return stage;
    }

    // The backdrop is prepared over the first frames (M47 B6); held at one moment so the frames compare.
    private static void Settle(StageCompositor stage, float from, float to)
    {
        for (var now = from; now <= to; now += BackdropCatalog.StepMilliseconds)
            stage.Draw(now, Width, Height);
    }

    // The bounding box of pixels that differ by more than a few levels; null when none do.
    private static (int Left, int Top, int Right, int Bottom)? Changed(byte[] a, byte[] b, int threshold = 12)
    {
        int left = Width, top = Height, right = -1, bottom = -1;
        for (var y = 0; y < Height; y += 2)
        {
            for (var x = 0; x < Width; x += 2)
            {
                var i = ((y * Width) + x) * 4;
                if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) <= threshold)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        return right < 0 ? null : (left, top, right, bottom);
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
}
