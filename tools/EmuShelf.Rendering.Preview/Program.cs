using System.Diagnostics;
using System.Numerics;
using EmuShelf.Rendering;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Preview;
using EmuShelf.Rendering.Shells;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

// Renders every shell at a spread of poses into PNGs, through the same MediaShellRenderer the app
// uses. The couch hero cannot be unit-tested and cannot be seen from a headless checkout, so this
// is how a change to the shaders or the shell constants gets looked at before it ships.

var outputDirectory = ArgumentValue("--out") ?? "artifacts/shell-preview";
var width = int.Parse(ArgumentValue("--width") ?? "420");
var height = int.Parse(ArgumentValue("--height") ?? "560");
var background = ParseColour(ArgumentValue("--background") ?? "1A1C20");

Directory.CreateDirectory(outputDirectory);

(string Name, float Yaw, float Pitch)[] poses =
[
    ("front", 0f, 0f),
    ("hero", -0.42f, -0.10f),
    ("turned", -1.05f, -0.06f),
    ("spine", -1.48f, 0f),
    ("back", MathF.PI, 0f),
    ("top-edge", -0.3f, -0.85f),
    ("bottom-edge", -0.3f, 0.85f),
];

Console.WriteLine("Creating a surfaceless EGL context...");
var resolve = HeadlessGlContext.CreateCurrent();
var gl = GL.GetApi(new LamdaNativeContext(resolve));

Console.WriteLine($"  renderer: {gl.GetStringS(StringName.Renderer)}");
Console.WriteLine($"  version : {gl.GetStringS(StringName.Version)}");

// A surfaceless context has no default framebuffer, so the renderer is given one of ours to
// resolve into — the same contract Avalonia's OpenGlControlBase uses when it hands over its FBO.
var target = CreateTargetFramebuffer(gl, (uint)width, (uint)height);

// The shelf tints the studio with the focused system's accent; use a representative one.
var accent = MediaShellRenderer.ToLinear(0.36f, 0.45f, 0.72f);

var stopwatch = Stopwatch.StartNew();
using var renderer = MediaShellRenderer.Create(gl, GlslDialect.Desktop, 3, 3, accent);
Console.WriteLine($"  renderer ready in {stopwatch.ElapsedMilliseconds} ms (includes baking the IBL)");

// --no-cover exercises the real fallback for a game with no scraped art, and is the check that
// no retail artwork baked into the authored shells can show through.
if (!args.Contains("--no-cover"))
{
    renderer.SetCoverArt(TestCover.Create());
}

// Canonical proportions, printed so a shell that loads rotated or mis-scaled is caught here
// rather than by squinting at a render.
foreach (var candidate in MediaShellCatalog.All)
{
    var size = MediaShellCatalog.Load(candidate).Size;
    Console.WriteLine(
        $"  {Slug(candidate),-16} W/H {size.X / size.Y:F3}  D/H {size.Z / size.Y:F3}");
}

var sheetColumns = poses.Length;
var shells = MediaShellCatalog.All.ToArray();
var sheet = new byte[width * sheetColumns * height * shells.Length * 4];

for (var row = 0; row < shells.Length; row++)
{
    var shell = shells[row];
    for (var column = 0; column < poses.Length; column++)
    {
        var (name, yaw, pitch) = poses[column];
        stopwatch.Restart();
        renderer.Render(shell, target, (uint)width, (uint)height, yaw, pitch);
        gl.Finish();
        var frame = ReadPixels(gl, target, width, height);
        Composite(frame, background);

        var file = Path.Combine(outputDirectory, $"{Slug(shell)}-{name}.png");
        PngWriter.Write(file, width, height, frame);
        Console.WriteLine($"  {file} ({stopwatch.ElapsedMilliseconds} ms)");

        BlitIntoSheet(sheet, sheetColumns * width, frame, width, height, column * width, row * height);
    }
}

var sheetPath = Path.Combine(outputDirectory, "contact-sheet.png");
PngWriter.Write(sheetPath, width * sheetColumns, height * shells.Length, sheet);
Console.WriteLine($"  {sheetPath}");

string? ArgumentValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string Slug(MediaShell shell) => shell switch
{
    MediaShell.SnesCartridge => "snes-cartridge",
    MediaShell.GbaCartridge => "gba-cartridge",
    MediaShell.DiscKeepCase => "disc-keep-case",
    _ => shell.ToString().ToLowerInvariant(),
};

static (byte R, byte G, byte B) ParseColour(string hex) =>
(
    Convert.ToByte(hex.Substring(0, 2), 16),
    Convert.ToByte(hex.Substring(2, 2), 16),
    Convert.ToByte(hex.Substring(4, 2), 16)
);

static uint CreateTargetFramebuffer(GL gl, uint width, uint height)
{
    var framebuffer = gl.GenFramebuffer();
    gl.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);

    var colour = gl.GenRenderbuffer();
    gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, colour);
    gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Rgba8, width, height);
    gl.FramebufferRenderbuffer(
        FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
        RenderbufferTarget.Renderbuffer, colour);

    var status = gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
    if (status != GLEnum.FramebufferComplete)
    {
        throw new InvalidOperationException($"Preview framebuffer incomplete: {status}.");
    }

    return framebuffer;
}

static byte[] ReadPixels(GL gl, uint framebuffer, int width, int height)
{
    var pixels = new byte[width * height * 4];
    gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
    gl.PixelStore(PixelStoreParameter.PackAlignment, 4);
    gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, new Span<byte>(pixels));

    // GL reads bottom row first; PNG stores top row first.
    var stride = width * 4;
    var flipped = new byte[pixels.Length];
    for (var y = 0; y < height; y++)
    {
        Array.Copy(pixels, (height - 1 - y) * stride, flipped, y * stride, stride);
    }

    return flipped;
}

// The hero is drawn with a transparent surround so the shelf shows through; composite it over a
// representative backdrop so the preview shows what the player would actually see.
static void Composite(byte[] rgba, (byte R, byte G, byte B) background)
{
    for (var i = 0; i < rgba.Length; i += 4)
    {
        var alpha = rgba[i + 3] / 255f;
        rgba[i] = (byte)((rgba[i] * alpha) + (background.R * (1f - alpha)));
        rgba[i + 1] = (byte)((rgba[i + 1] * alpha) + (background.G * (1f - alpha)));
        rgba[i + 2] = (byte)((rgba[i + 2] * alpha) + (background.B * (1f - alpha)));
        rgba[i + 3] = 255;
    }
}

static void BlitIntoSheet(
    byte[] sheet, int sheetWidth, byte[] frame, int width, int height, int x, int y)
{
    for (var row = 0; row < height; row++)
    {
        Array.Copy(
            frame, row * width * 4,
            sheet, (((y + row) * sheetWidth) + x) * 4,
            width * 4);
    }
}
