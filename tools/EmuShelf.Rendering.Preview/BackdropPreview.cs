using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage.Backdrops;
using Silk.NET.OpenGL;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Renders every animated Stage backdrop (M45 H7) through the app's <see cref="BackdropRenderer"/> at a few
/// moments, so a port can be compared with its motion-lab prototype at the same time values before it goes to
/// the Thor: `--backdrops [--out dir] [--times 3,8,14] [--only id,id]`. Each moment is reached by stepping the
/// last second at 30 Hz, so ghosting and trails look as they would on the device.
/// </summary>
internal static class BackdropPreview
{
    public static void Run(GL gl, string outputDirectory, float[] times, IReadOnlyCollection<string>? only)
    {
        const int width = 960, height = 540;
        Directory.CreateDirectory(outputDirectory);
        var framebuffer = gl.GenFramebuffer();
        var colour = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, width, height);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colour);

        using var renderer = new BackdropRenderer(gl, GlslDialect.Desktop, 3, 3);
        var specs = BackdropCatalog.All.Where(spec => only is null || only.Contains(spec.Id)).ToList();
        var sheet = new byte[width / 2 * times.Length * (height / 2) * specs.Count * 4];
        var sheetStride = width / 2 * times.Length * 4;
        for (var row = 0; row < specs.Count; row++)
        {
            var spec = specs[row];
            var instance = renderer.Start(spec.Id, 0f, clockOffsetSeconds: 0f)
                ?? throw new InvalidOperationException($"Backdrop '{spec.Id}' did not compile (see the trace above).");
            var started = System.Diagnostics.Stopwatch.StartNew();
            for (var column = 0; column < times.Length; column++)
            {
                var target = times[column] * 1000f;
                for (var now = Math.Max(0f, target - 1000f); now <= target + 0.01f; now += BackdropCatalog.StepMilliseconds)
                    renderer.Update(instance, now);
                gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                gl.Viewport(0, 0, width, height);
                gl.ClearColor(1, 0, 1, 1);
                gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                gl.Enable(EnableCap.Blend);
                gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                renderer.Present(instance, 1f);
                gl.Disable(EnableCap.Blend);
                var pixels = Read(gl, framebuffer, width, height);
                PngWriter.Write(Path.Combine(outputDirectory, $"{spec.Id}-{times[column]:0.#}s.png"), width, height, pixels);
                for (var y = 0; y < height / 2; y++)
                    for (var x = 0; x < width / 2; x++)
                    {
                        var from = ((y * 2 * width) + (x * 2)) * 4;
                        var to = (((row * (height / 2)) + y) * sheetStride) + (((column * (width / 2)) + x) * 4);
                        Array.Copy(pixels, from, sheet, to, 4);
                    }
            }
            var error = gl.GetError();
            Console.WriteLine($"  {spec.Id,-14} {spec.Width}×{spec.Height}  {started.ElapsedMilliseconds,5} ms for {times.Length} moments{(error == GLEnum.NoError ? "" : $"  GL error {error}")}");
            renderer.Stop(instance);
        }
        PngWriter.Write(Path.Combine(outputDirectory, "backdrops-sheet.png"), width / 2 * times.Length, height / 2 * specs.Count, sheet);
        gl.DeleteFramebuffer(framebuffer);
        gl.DeleteRenderbuffer(colour);
        Console.WriteLine($"Wrote {specs.Count * times.Length} frames and backdrops-sheet.png to {outputDirectory}");
    }

    /// <summary>
    /// Big Type's animations (M47 B13) as the Stage shows them: each at its draw size, recoloured toward its console's ground (Recent's
    /// for Recent's), on a 1920 × 1080 frame: `--backdrops --bigtype [--out dir] [--times …] [--only id,id] [--sharp-width w]`
    /// (w: the Thor's <c>debug.emushelf.bdsize</c> override, 0 for the console's own size). Writes <c>owner-id-Ns.png</c> per moment.
    /// </summary>
    public static void RunBigType(GL gl, string outputDirectory, float[] times, IReadOnlyCollection<string>? only, string? sharpWidth)
    {
        const int width = BigTypeBackdrops.StageWidth, height = BigTypeBackdrops.StageHeight;
        if (sharpWidth is { } value)
            BigTypeBackdrops.SharpWidthOverride = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Directory.CreateDirectory(outputDirectory);
        var framebuffer = gl.GenFramebuffer();
        var colour = gl.GenRenderbuffer();
        gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour);
        gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, width, height);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
        gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, colour);
        using var renderer = new BackdropRenderer(gl, GlslDialect.Desktop, 3, 3);
        foreach (var (owner, backdrops) in BigTypeBackdrops.All)
        {
            var ground = Ground(owner);
            foreach (var entry in backdrops.Where(entry => only is null || only.Contains(entry.Id)))
            {
                var instance = renderer.Start(entry.Id, 0f, clockOffsetSeconds: 0f, new BackdropLook(true, ground))
                    ?? throw new InvalidOperationException($"Backdrop '{entry.Id}' did not compile (see the trace above).");
                var started = System.Diagnostics.Stopwatch.StartNew();
                foreach (var time in times)
                {
                    var target = time * 1000f;
                    for (var now = Math.Max(0f, target - 1000f); now <= target + 0.01f; now += BackdropCatalog.StepMilliseconds)
                        renderer.Update(instance, now);
                    gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                    gl.Viewport(0, 0, width, height);
                    gl.ClearColor(1, 0, 1, 1);
                    gl.Clear((uint)ClearBufferMask.ColorBufferBit);
                    gl.Enable(EnableCap.Blend);
                    gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
                    renderer.Present(instance, 1f);
                    gl.Disable(EnableCap.Blend);
                    PngWriter.Write(Path.Combine(outputDirectory, $"{owner}-{entry.Id}-{time:0.#}s.png"), width, height, Read(gl, framebuffer, width, height));
                }
                var error = gl.GetError();
                Console.WriteLine($"  {owner,-12} {entry.Id,-16} {instance.Width}×{instance.Height} pull {entry.Pull:0.##}  {started.ElapsedMilliseconds,5} ms{(error == GLEnum.NoError ? "" : $"  GL error {error}")}");
                renderer.Stop(instance);
            }
        }
        gl.DeleteFramebuffer(framebuffer);
        gl.DeleteRenderbuffer(colour);
    }

    /// <summary>A console's Big Type ground (KnownSystems), or Recent's (tokens.js <c>colour.Recent</c>), as sRGB from 0 to 1.</summary>
    internal static System.Numerics.Vector3 Ground(string owner)
    {
        var hex = owner == BigTypeBackdrops.Recent ? "#39465F"
            : EmuShelf.Integrations.Systems.KnownSystems.All.Single(system => system.Id == owner).GroundColor!;
        int Channel(int at) => Convert.ToInt32(hex.Substring(at, 2), 16);
        return new System.Numerics.Vector3(Channel(1) / 255f, Channel(3) / 255f, Channel(5) / 255f);
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
