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
        ArgumentValue("--neutral-material"),
        ArgumentValue("--neutral-rect"),
        ArgumentValue("--neutral-fill"),
        // "base" leaves the model's own normal and metallic/roughness maps alone, for a shell whose
        // surface detail is the object's moulding rather than an embossing of the removed artwork.
        ArgumentValue("--neutral-maps"),
        args.Contains("--single-instance"),
        args.Contains("--bake-vertex-colours"),
        // A downloaded scene often holds more than the shell, and often poses it open for a
        // product shot. Both are geometry problems a profile cannot fix.
        ArgumentValue("--drop-meshes"),
        ArgumentValue("--close-lid"),
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
    // Pitch tips the shell, not the camera: a positive angle rolls its top towards the viewer, so
    // that is the pose that shows the top edge. These two carried each other's names, and a render
    // of a cartridge's underside filed as "top-edge" is a good way to conclude a label that folds
    // over the top is not drawing at all.
    ("top-edge", -0.3f, 0.85f),
    ("bottom-edge", -0.3f, -0.85f),
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
    // A candidate's own measured panel, as MinU,MaxU,MinV,MaxV — the numbers that would go into its
    // catalog entry, tried on it before that entry exists.
    ArtPanel? candidatePanel = null;
    if (ArgumentValue("--model-panel") is { } panelArgument)
    {
        var edges = panelArgument
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(part => float.Parse(part, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        if (edges.Length is not (4 or 5))
        {
            throw new ArgumentException("--model-panel wants minU,maxU,minV,maxV[,cornerRadius].");
        }

        candidatePanel = new ArtPanel(
            ArtFace.Front, edges[0], edges[1], edges[2], edges[3],
            CornerRadius: edges.Length == 5 ? edges[4] : 0f);
    }

    // Which shell's slot the candidate occupies, and therefore whose material calibration it is
    // shaded with. This defaulted silently to SNES, which quietly invalidates any comparison
    // between a candidate and the shell it is meant to replace: a mesh change gets judged under
    // another cartridge's normal strength, ambient fill and albedo scale.
    var inspectionSlot = ArgumentValue("--model-as") is { } slot
        ? Enum.Parse<MediaShell>(slot, ignoreCase: true)
        : MediaShell.SnesCartridge;

    renderer.SetInspectionShell(
        inspectionSlot, candidate,
        suppressArtworkPanels: args.Contains("--model-raw"),
        coverPanel: candidatePanel);
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

// Declared before the per-shell sheet because that sheet needs each shell's finish. Without it the
// turntable draws the model's own plastic, so a shell whose profile is doing part of the colouring
// gets reviewed at the wrong colour — and the turntable is the only straight-on view there is.
var shelfProfiles = new[]
{
    // These mirror EmuShelf.App.Rendering.MediaShellMap, which the tool cannot reference — the
    // renderer deliberately knows nothing about consoles. Keep them in step by hand: this list had
    // silently kept the pre-correction GBA and SNES figures, so the acceptance shot was showing
    // proportions the app had already stopped using.
    new PhysicalMediaProfile(MediaShell.CoverCard, new Vector3(135f, 190f, 5f), PhysicalArtworkSlots.Front, "cover-card", "cover-card"),
    new PhysicalMediaProfile(MediaShell.DsCard, new Vector3(34.85f, 35f, 2.64f), PhysicalArtworkSlots.CartridgeSupport, "ds-black", "cartridge-vertical", FloorClearanceInShelfUnits: 0.008f),
    new PhysicalMediaProfile(MediaShell.GbaCartridge, new Vector3(57.5f, 32.9f, 6.58f), PhysicalArtworkSlots.CartridgeSupport, "gba-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
    new PhysicalMediaProfile(MediaShell.SnesCartridge, new Vector3(129f, 77.5f, 20f), PhysicalArtworkSlots.CartridgeSupport, "snes-pal-grey", "cartridge-vertical", PresentationScale: 1.235f, FloorClearanceInShelfUnits: 0.014f),
    // Inside the frame for the same reason as the Mega Drive below. Appended last it fell off the
    // right-hand edge of the acceptance shot, which was already observed once while reviewing it.
    new PhysicalMediaProfile(MediaShell.GbcCartridge, new Vector3(57f, 64.42f, 8.99f), PhysicalArtworkSlots.CartridgeSupport, "gbc-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
    // Landscape and thin, which is what separates it at a glance from the portrait keep case two
    // along. In frame rather than appended, for the reason the Mega Drive note below records.
    new PhysicalMediaProfile(MediaShell.JewelCase, new Vector3(142f, 125.2f, 9.0f), PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine, "ps1-jewel", "case-downward"),
    // Beside the SNES cartridge on purpose, and no longer last: it was off the right-hand edge of
    // the acceptance shot, which is how it kept a profile a quarter too big for a whole milestone.
    new PhysicalMediaProfile(MediaShell.MegaDriveCartridge, new Vector3(109f, 70f, 11.8f), PhysicalArtworkSlots.CartridgeSupport, "megadrive-black", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
    new PhysicalMediaProfile(MediaShell.DiscKeepCase, new Vector3(135f, 190f, 14f), PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine, "ps2-black", "case-vertical"),
    new PhysicalMediaProfile(MediaShell.NesCartridge, new Vector3(120f, 135f, 18.3f), PhysicalArtworkSlots.CartridgeSupport, "nes-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.012f),
};
var finishes = shelfProfiles.ToDictionary(
    profile => profile.Shell, profile => profile.MaterialVariant);

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
        renderer.Render(
            shell, target, (uint)width, (uint)height, yaw, pitch,
            finishes.GetValueOrDefault(shell, string.Empty));
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
var shelfCentres = PhysicalCentres(shelfProfiles, gap: 0.14f);
// Named, not positional, for the reason the art-free slot below already is: inserting the Game Boy
// cartridge shifted every index after SNES, and a positional anchor would have silently re-centred
// the acceptance shot on a different medium.
var shelfFocus = Array.FindIndex(shelfProfiles, profile => profile.Shell == MediaShell.SnesCartridge);
var shelfAnchor = shelfCentres[shelfFocus];
var shelfItems = new List<MediaShelfRenderItem>(shelfProfiles.Length);
for (var index = 0; index < shelfProfiles.Length; index++)
{
    var key = 100L + index;
    // Leave one cartridge art-free to keep the authored empty-shell fallback in every review.
    // Named rather than numbered: this was a positional index, and reordering the list to bring the
    // Mega Drive into frame silently moved the art-free slot onto the very shell being reviewed.
    if (shelfProfiles[index].Shell != MediaShell.GbaCartridge && !args.Contains("--no-cover"))
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
        index == shelfFocus ? 1f : 0f,
        index == shelfFocus ? -0.28f : -0.18f,
        index == shelfFocus ? -0.06f : 0f,
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
    MediaShell.GbcCartridge => "gbc-cartridge",
    MediaShell.JewelCase => "jewel-case",
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
