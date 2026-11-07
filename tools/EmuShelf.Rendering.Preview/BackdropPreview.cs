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
