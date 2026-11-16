using System.Globalization;
using System.Numerics;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// The native Stage's own roll of a line, read back (M47 B11, the owner's "a pixel or two stuck on top"): `--stage-roll DIR [--out
/// dir]`. DIR holds pairs of real pictures of the Stage's line written by HandheldBigTypeRollTests (EMUSHELF_ROLL_RAW), the old text in
/// red and the new one in green, with every character's window. Each pair goes through the real StageCompositor as the line's picture
/// (placed where the line sits), then its replacement, which rolls; every 4 ms of the roll the frame is read back. Once every old glyph
/// has had its time to leave, no red pixel may remain; before any new glyph starts to rise, no green one may show where a glyph rises.
/// Writes a strip of the line every 40 ms per pair. Exits non-zero on a failure.
/// </summary>
internal static class StageRollCheck
{
    private const int Width = 1920;
    private const int Height = 1080;
    private const float LineX = 88f;
    private const float LineY = 880f;

    public static int Run(GL gl, string input, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var failures = 0;
        foreach (var cellsFile in Directory.GetFiles(input, "*.cells.txt").OrderBy(f => f, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(cellsFile)[..^".cells.txt".Length];
            var (old, oldText) = Load(input, name, "old", cellsFile);
            var (next, nextText) = Load(input, name, "new", cellsFile);
            var plan = BigTypeMotion.RollPlan(oldText.Text, nextText.Text);
            var end = BigTypeMotion.RollMilliseconds(plan.Length);
            var allLeft = Math.Min((plan.Length - 1) * BigTypeMotion.DigitStagger, BigTypeMotion.DigitStaggerMax) + BigTypeMotion.DigitOutMilliseconds;
            // Every changed cell's new glyph rises; none before the first one's delay.
            var firstRise = BigTypeMotion.DigitInDelay;

            var framebuffer = gl.GenFramebuffer();
            var colour = gl.GenRenderbuffer();
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour);
            gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, Width, Height);
            gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colour);
            var compositor = new StageCompositor(gl, GlslDialect.Desktop, 3, 3, () => { }) { TargetFramebuffer = framebuffer };
            compositor.Apply(new StageCanvasCommand(Width), 0f);
            compositor.Apply(new StageInkCommand(Vector3.Zero, Vector3.Zero), 0f);
            compositor.Apply(new StageChromeCommand("bt-line", old, StageInkZone.None, Text: oldText), 0f);
            compositor.Draw(0f, Width, Height);
            const float start = 1000f;
            compositor.Apply(new StageChromeCommand("bt-line", next, StageInkZone.None, Text: nextText), start);
            var (boxW, boxH) = ((int)Math.Ceiling(Math.Max(old.Width, next.Width)), (int)Math.Ceiling(Math.Max(old.Height, next.Height)));
            var strip = new List<byte[]>();
            int worstRed = 0, worstGreen = 0;
            for (var since = 0f; since <= end + 40f; since += 4f)
            {
                compositor.Draw(start + since, Width, Height);
                var frame = Read(gl, framebuffer);
                int red = 0, green = 0;
                for (var y = (int)LineY - 4; y < LineY + boxH + 4; y++)
                {
                    for (var x = (int)LineX - 4; x < LineX + boxW + 4; x++)
                    {
                        var i = ((y * Width) + x) * 4;
                        if (frame[i] > 24 && frame[i + 1] < frame[i] / 2)
                            red++;
                        if (frame[i + 1] > 24 && frame[i] < frame[i + 1] / 2)
                            green++;
                    }
                }
                // Old glyphs gone (no cell is the same in these pairs, or it would show its new picture in place from the start).
                if (since >= allLeft && since < end)
                    worstRed = Math.Max(worstRed, red);
                if (since < firstRise && plan.All(cell => cell != BigTypeRollCell.Same))
                    worstGreen = Math.Max(worstGreen, green);
                if (since % 40f == 0f)
                    strip.Add(Crop(frame, (int)LineX - 8, (int)LineY - 8, boxW + 16, boxH + 16));
            }
            var ok = worstRed == 0 && worstGreen == 0;
            if (!ok)
                failures++;
            Console.WriteLine(FormattableString.Invariant(
                $"  {(ok ? "ok  " : "FAIL")} {name}: old pixels after every old glyph's time (from {allLeft:0} ms to {end:0}) {worstRed}; new pixels before the first rise ({firstRise:0} ms) {worstGreen}"));
            WriteStrip(Path.Combine(outputDirectory, $"roll-{name}.png"), strip, boxW + 16, boxH + 16);
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(colour);
            compositor.Dispose();
        }
        Console.WriteLine(failures == 0 ? "All roll checks passed." : $"{failures} roll check(s) FAILED.");
        return failures == 0 ? 0 : 1;
    }

    private static (StageImage Image, StageText Text) Load(string input, string name, string tag, string cellsFile)
    {
        var lines = File.ReadAllLines(cellsFile);
        var head = lines.First(line => line.StartsWith(tag + " ", StringComparison.Ordinal)).Split(' ');
        var (w, h) = (int.Parse(head[1], CultureInfo.InvariantCulture), int.Parse(head[2], CultureInfo.InvariantCulture));
        var data = File.ReadAllBytes(Path.Combine(input, $"{name}.{tag}.rgba"));
        float F(string value) => float.Parse(value, CultureInfo.InvariantCulture);
        var cells = lines.Where(line => line.StartsWith(tag + "cell ", StringComparison.Ordinal)).Select(line => line.Split(' '))
            .Select(p => new StageTextCell((char)int.Parse(p[1], CultureInfo.InvariantCulture), LineX + F(p[2]), LineY + F(p[3]), F(p[4]), F(p[5]), F(p[6])))
            .ToList();
        return (new StageImage(new StagePixels(w, h, data, IsBgra: false), LineX, LineY, w, h), new StageText(cells));
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

    private static byte[] Crop(byte[] frame, int x0, int y0, int w, int h)
    {
        var crop = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            Array.Copy(frame, (((y0 + y) * Width) + x0) * 4, crop, y * w * 4, w * 4);
        return crop;
    }

    private static void WriteStrip(string path, List<byte[]> frames, int w, int h)
    {
        var all = new byte[w * h * frames.Count * 4];
        for (var k = 0; k < frames.Count; k++)
            Array.Copy(frames[k], 0, all, k * w * h * 4, w * h * 4);
        PngWriter.Write(path, w, h * frames.Count, all);
    }
}
