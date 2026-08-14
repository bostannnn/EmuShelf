using System.Diagnostics;
using System.Numerics;
using EmuShelf.Rendering;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Preview;
using EmuShelf.Rendering.Shells;
using EmuShelf.Rendering.Models;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

// Renders every shell at a spread of poses into PNGs, through the same MediaShellRenderer the app
// uses. The couch hero cannot be unit-tested and cannot be seen from a headless checkout, so this
// is how a change to the shaders or the shell constants gets looked at before it ships.

var sourceModel = ArgumentValue("--prepare-snes");
if (sourceModel is not null)
{
    var preparedModel = ArgumentValue("--prepare-out")
        ?? throw new ArgumentException("--prepare-snes requires --prepare-out <runtime.glb>.");
    SnesModelPrep.Prepare(sourceModel, preparedModel);
    Console.WriteLine($"Prepared {preparedModel} ({new FileInfo(preparedModel).Length:N0} bytes)");
    return;
}

// Dumps a candidate model's base-colour atlas, and the same atlas with the front face's UV
// triangles drawn over it. Sourcing a new shell stalls on one question that measurement cannot
// answer — which island in the atlas is the printed label — and the overlay answers it by eye in
// seconds. Runs before any GL setup, so it works on a machine with no usable context.
var prepModel = ArgumentValue("--prepare-model");
if (prepModel is not null)
{
    ModelPrep.Prepare(
        prepModel,
        ArgumentValue("--prepare-out")
            ?? throw new ArgumentException("--prepare-model requires --prepare-out <runtime.glb>."),
        ArgumentValue("--neutral-material")
            ?? throw new ArgumentException("--prepare-model requires --neutral-material <name>."),
        int.Parse(ArgumentValue("--max-texture") ?? "1024"));
    return;
}

var atlasModel = ArgumentValue("--dump-atlas");
if (atlasModel is not null)
{
    AtlasDump.Write(atlasModel, ArgumentValue("--out") ?? "artifacts/atlas");
    return;
}

var outputDirectory = ArgumentValue("--out") ?? "artifacts/shell-preview";
var width = int.Parse(ArgumentValue("--width") ?? "420");
var height = int.Parse(ArgumentValue("--height") ?? "560");
var shelfWidth = int.Parse(ArgumentValue("--shelf-width") ?? "1440");
var shelfHeight = int.Parse(ArgumentValue("--shelf-height") ?? "720");
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

var inspectionModel = ArgumentValue("--model");
if (inspectionModel is not null)
{
    // Yaw alone cannot bring every downloaded model into canonical space: some are authored lying
    // on their side, which needs a rotation about more than one axis. Pitch and roll let a candidate
    // orientation be tried and looked at, rather than deduced from UV winding and guessed at.
    float Degrees(string name) => float.Parse(
        ArgumentValue(name) ?? "0", System.Globalization.CultureInfo.InvariantCulture)
        * MathF.PI / 180f;
    var inspectionOrientation =
        Matrix4x4.CreateRotationX(Degrees("--model-pitch"))
        * Matrix4x4.CreateRotationY(Degrees("--model-yaw"))
        * Matrix4x4.CreateRotationZ(Degrees("--model-roll"));
    var candidate = GlbLoader.Load(
        File.ReadAllBytes(inspectionModel), inspectionOrientation, maxTextureSize: 1024);
    renderer.SetInspectionShell(
        MediaShell.SnesCartridge, candidate, suppressArtworkPanels: args.Contains("--model-raw"));
    Console.WriteLine(
        $"  inspection model: {inspectionModel} — {candidate.Meshes.Sum(mesh => mesh.TriangleCount):N0} triangles, "
        + $"{candidate.Materials.Count} materials, {candidate.Textures.Count} textures, "
        + $"W/H {candidate.Size.X / candidate.Size.Y:F3}, D/H {candidate.Size.Z / candidate.Size.Y:F3}");
}

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

// Phase 1 acceptance image: unlike the per-shell turntable above, this exercises the app's shared
// camera, measured profiles, common baseline and multi-item draw path. The selected keep case is
// deliberately flanked by SNES and GBA cartridges so relative physical scale is visible at a glance.
var shelfProfiles = new[]
{
    new PhysicalMediaProfile(MediaShell.CoverCard, new Vector3(135f, 190f, 5f), PhysicalArtworkSlots.Front, "cover-card", "cover-card"),
    new PhysicalMediaProfile(MediaShell.GbaCartridge, new Vector3(85f, 60f, 6f), PhysicalArtworkSlots.CartridgeSupport, "gba-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
    new PhysicalMediaProfile(MediaShell.SnesCartridge, new Vector3(129f, 87f, 20f), PhysicalArtworkSlots.CartridgeSupport, "snes-pal-grey", "cartridge-vertical", PresentationScale: 1.10f, FloorClearanceInShelfUnits: 0.014f),
    new PhysicalMediaProfile(MediaShell.DiscKeepCase, new Vector3(135f, 190f, 14f), PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine, "ps2-black", "case-vertical"),
    new PhysicalMediaProfile(MediaShell.SnesCartridge, new Vector3(129f, 87f, 20f), PhysicalArtworkSlots.CartridgeSupport, "snes-pal-grey", "cartridge-vertical", PresentationScale: 1.10f, FloorClearanceInShelfUnits: 0.014f),
    new PhysicalMediaProfile(MediaShell.GbaCartridge, new Vector3(85f, 60f, 6f), PhysicalArtworkSlots.CartridgeSupport, "gba-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
    new PhysicalMediaProfile(MediaShell.CoverCard, new Vector3(135f, 190f, 5f), PhysicalArtworkSlots.Front, "cover-card", "cover-card"),
};
var shelfCentres = PhysicalCentres(shelfProfiles, gap: 0.20f);
var shelfAnchor = shelfCentres[3];
var shelfItems = new List<MediaShelfRenderItem>(shelfProfiles.Length);
for (var index = 0; index < shelfProfiles.Length; index++)
{
    var key = 100L + index;
    // Leave one cartridge art-free to keep the authored empty-shell fallback in every review.
    if (index != 4 && !args.Contains("--no-cover"))
    {
        renderer.SetCoverArt(key, TestCover.Create());
    }

    var itemAccent = (index % 3) switch
    {
        0 => MediaShellRenderer.ToLinear(0.48f, 0.30f, 0.70f),
        1 => MediaShellRenderer.ToLinear(0.31f, 0.55f, 0.76f),
        _ => MediaShellRenderer.ToLinear(0.72f, 0.33f, 0.40f),
    };
    shelfItems.Add(new MediaShelfRenderItem(
        key,
        shelfProfiles[index],
        shelfCentres[index] - shelfAnchor,
        index == 3 ? 1f : 0f,
        index == 3 ? -0.28f : -0.18f,
        index == 3 ? -0.06f : 0f,
        itemAccent));
}

var shelfTarget = CreateTargetFramebuffer(gl, (uint)shelfWidth, (uint)shelfHeight);
stopwatch.Restart();
// The acceptance composition deliberately mixes a keep case with cartridges, so the tallest medium
// in it is what the shared camera frames — exactly as the app frames a whole library view.
var shelfMediaHeight = shelfProfiles.Max(
    profile => profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits);
renderer.RenderShelf(shelfItems, shelfMediaHeight, shelfTarget, (uint)shelfWidth, (uint)shelfHeight);
gl.Finish();
var shelfFrame = ReadPixels(gl, shelfTarget, shelfWidth, shelfHeight);
Composite(shelfFrame, background);
var shelfPath = Path.Combine(outputDirectory, "physical-shelf-scene.png");
PngWriter.Write(shelfPath, shelfWidth, shelfHeight, shelfFrame);
Console.WriteLine($"  {shelfPath} ({stopwatch.ElapsedMilliseconds} ms)");

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
    MediaShell.CoverCard => "cover-card",
    _ => shell.ToString().ToLowerInvariant(),
};

static float[] PhysicalCentres(IReadOnlyList<PhysicalMediaProfile> profiles, float gap)
{
    var centres = new float[profiles.Count];
    var cursor = 0f;
    for (var index = 0; index < profiles.Count; index++)
    {
        var width = profiles[index].WidthInShelfUnits;
        centres[index] = cursor + (width * 0.5f);
        cursor += width + gap;
    }

    return centres;
}

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
