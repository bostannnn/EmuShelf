using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Whether the baked legibility table still holds when the Big Type Stage is laid out for the narrow screen (M47 B11):
/// `--stage-narrow-legibility [--out dir]`. The table (legibility.json, M47 R2) was baked over the whole 16:9 backdrop; the narrow
/// Stage shows the backdrop's middle 1240 and puts its zones over other parts of it. Every backdrop the table knows is drawn through
/// the real compositor, with its veils, on the wide canvas and on the narrow one, at three moments; in each zone's text area the
/// contrast of the table's ink against the veiled backdrop is measured as the mock's rule reads it (the 96th percentile of luminance
/// against white ink, the 4th against the dark ink) and compared with tokens.js <c>legibility.Pass</c> (3.0); the narrow Stage takes
/// its own left veils (<see cref="BackdropLegibility.NarrowLeftVeils"/>). Prints one line per backdrop and zone (the veil on each
/// width, the worst contrast on each) and a summary; writes the narrow frames of any that fall short.
/// </summary>
internal static class StageNarrowLegibilityCheck
{
    private const int Height = 1080;
    private const float Pass = 3.0f;
    private static readonly float[] Moments = [1_500f, 3_000f, 5_000f];

    // Where each zone's text can stand (canvas pixels): the wide Stage's from tokens.js stage, the narrow one's from B11's narrow tokens.
    private static (StageInkZone Zone, int X, int Y, int W, int H)[] Areas(bool narrow) => narrow
        ?
        [
            (StageInkZone.Left, 80, 64, 1080, 52),     // the label
            (StageInkZone.Left, 80, 160, 1080, 584),   // the name's box (no medium beside it: the widest)
            (StageInkZone.Left, 88, 790, 1064, 120),   // the line and the second line, a hint row higher
            (StageInkZone.TopRight, 690, 48, 462, 100), // battery and clock
            (StageInkZone.BottomRight, 520, 940, 632, 68), // the hints' row
        ]
        :
        [
            (StageInkZone.Left, 80, 64, 1080, 52),
            (StageInkZone.Left, 80, 160, 1360, 680),
            (StageInkZone.Left, 88, 886, 1060, 120),
            (StageInkZone.TopRight, 1370, 48, 462, 100),
            (StageInkZone.BottomRight, 1190, 940, 642, 68),
        ];

    public static int Run(GL gl, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var ground = new Vector3(0x2B, 0x5B, 0xC7) / 255f;
        var deepBytes = BigTypeVeil.Deep(new Vector3(0x2B, 0x5B, 0xC7));
        var deep = deepBytes / 255f;
        var ink = new StageInkCommand(ground, deep);
        int wideFails = 0, narrowFails = 0, zones = 0;
        foreach (var (id, answer) in BackdropLegibility.All.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var worst = new Dictionary<(bool Narrow, StageInkZone Zone), float>();
            foreach (var narrow in new[] { false, true })
            {
                var width = narrow ? 1240 : 1920;
                var (framebuffer, colour) = RenderTarget(gl, width, Height);
                var compositor = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
                Feed(compositor, ink, id, width);
                var now = 0f;
                byte[]? last = null;
                foreach (var moment in Moments)
                {
                    for (; now < moment; now += BackdropCatalog.StepMilliseconds)
                        compositor.Draw(now, width, Height);
                    var frame = Read(gl, framebuffer, width, Height);
                    last = frame;
                    foreach (var area in Areas(narrow))
                    {
                        var zoneInk = answer[area.Zone];
                        var contrast = Contrast(frame, width, area, zoneInk.Dark ? deep : Vector3.One, zoneInk.Dark);
                        var key = (narrow, area.Zone);
                        worst[key] = worst.TryGetValue(key, out var was) ? Math.Min(was, contrast) : contrast;
                    }
                }
                gl.DeleteFramebuffer(framebuffer);
                gl.DeleteRenderbuffer(colour);
                compositor.Dispose();
                if (narrow && last is not null && worst.Where(w => w.Key.Narrow).Any(w => w.Value < Pass))
                    PngWriter.Write(Path.Combine(outputDirectory, $"narrow-{id}.png"), width, Height, last);
            }
            foreach (var zone in new[] { StageInkZone.Left, StageInkZone.TopRight, StageInkZone.BottomRight })
            {
                zones++;
                var wide = worst[(false, zone)];
                var narrowWorst = worst[(true, zone)];
                if (wide < Pass)
                    wideFails++;
                if (narrowWorst < Pass)
                    narrowFails++;
                var narrowVeil = BackdropLegibility.For(id, narrow: true)[zone].Veil;
                Console.WriteLine(FormattableString.Invariant(
                    $"  {(narrowWorst >= Pass ? "ok  " : "FAIL")} {id,-18} {zone,-11} ink {(answer[zone].Dark ? "dark " : "white")} veil {answer[zone].Veil:0.00}/{narrowVeil:0.00}  wide {wide:0.00}  narrow {narrowWorst:0.00}"));
            }
        }
        Console.WriteLine($"{zones} zones over {BackdropLegibility.All.Count} backdrops: under {Pass} on the wide Stage {wideFails}, on the narrow one {narrowFails}.");
        return 0;
    }

    // The contrast of the ink against the zone's veiled backdrop, read as the mock's rule reads it: against white ink the brightest
    // pixels that matter (the 96th percentile of luminance), against the dark ink the darkest (the 4th).
    private static float Contrast(byte[] frame, int width, (StageInkZone Zone, int X, int Y, int W, int H) area, Vector3 ink, bool dark)
    {
        var lums = new List<float>(area.W * area.H / 16);
        for (var y = area.Y; y < area.Y + area.H; y += 4)
        {
            for (var x = area.X; x < Math.Min(width, area.X + area.W); x += 4)
            {
                var i = ((y * width) + x) * 4;
                lums.Add(Luminance(new Vector3(frame[i], frame[i + 1], frame[i + 2]) / 255f));
            }
        }
        lums.Sort();
        var background = dark ? lums[(int)(lums.Count * 0.04f)] : lums[Math.Min(lums.Count - 1, (int)(lums.Count * 0.96f))];
        var text = Luminance(ink);
        var (hi, lo) = text > background ? (text, background) : (background, text);
        return (hi + 0.05f) / (lo + 0.05f);
    }

    private static float Luminance(Vector3 c)
    {
        static float Lin(float v) => v <= 0.03928f ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f);
        return (0.2126f * Lin(c.X)) + (0.7152f * Lin(c.Y)) + (0.0722f * Lin(c.Z));
    }

    private static void Feed(StageCompositor compositor, StageInkCommand ink, string backdropId, float canvas)
    {
        var grey = new StagePixels(1, 1, [0x40, 0x40, 0x40, 255], IsBgra: false);
        var pictured = new StageBackdrop(new StageImage(grey, 0, 0, 1920, 1080), null, null, null, Vector2.Zero, 0, 0);
        compositor.Apply(new StageCanvasCommand(canvas), 0f);
        compositor.Apply(ink, 0f);
        compositor.Apply(new StageBackdropCommand(pictured, [backdropId]), 0f);
        // The left veil as wide as the widest name box on each canvas (no medium beside it).
        var flatEnd = BigTypeVeil.LeftFlatEnd(80, canvas < 1920 ? 1080 : 1360);
        foreach (var (key, zone) in new[] { ("veil-left", StageInkZone.Left), ("veil-top-right", StageInkZone.TopRight), ("veil-bottom-right", StageInkZone.BottomRight) })
            compositor.Apply(new StageChromeCommand(key, BigTypeVeil.Mask(zone, flatEnd, canvas, 1080), zone, Veil: true), 0f);
    }

    private static (uint Framebuffer, uint Colour) RenderTarget(GL gl, int width, int height)
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
}
