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
        // "none" flattens nothing, for a shell whose baked artwork is the modeller's own invention
        // and is therefore covered by the same licence as the mesh.
        ArgumentValue("--neutral-maps"),
        args.Contains("--single-instance"),
        args.Contains("--bake-vertex-colours"),
        // A downloaded scene often holds more than the shell, and often poses it open for a
        // product shot. Both are geometry problems a profile cannot fix.
        ArgumentValue("--drop-meshes"),
        ArgumentValue("--close-lid"),
        args.Contains("--strip-textures"),
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
// Left at zero when unset: the width that actually frames the row is derived once the row exists,
// further down. Only an explicit --shelf-width is honoured here.
var shelfWidth = int.Parse(ArgumentValue("--shelf-width") ?? "0");
var shelfHeight = int.Parse(ArgumentValue("--shelf-height") ?? "900");
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
    // And some cannot be brought back with angles at all. A Sketchfab export often bakes an
    // arbitrary node rotation into the scene graph, which the loader composes into the vertices;
    // undoing it means the inverse of that exact quaternion, and reaching it by turning three
    // Euler dials is a search rather than a correction.
    var inspectionOrientation = ArgumentValue("--model-quat") is { } quaternion
        ? Matrix4x4.CreateFromQuaternion(ParseQuaternion(quaternion))
        : Matrix4x4.CreateRotationX(Degrees("--model-pitch"))
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
// The table itself lives in PreviewShelf so a test in EmuShelf.App.Tests can reach it and assert it
// still matches MediaShellMap. It used to sit inline here, where nothing outside this file could
// see it, and it drifted twice.
var shelfEntries = PreviewShelf.Entries;

// A shelf is one platform at a time in the app, so how a medium spaces against *itself* never
// appears in the mixed row above — and for the arcade cabinet that is the whole question, being
// deeper than it is wide. Repeats one entry instead.
if (ArgumentValue("--shelf-of") is { } singleShell)
{
    var only = Enum.Parse<MediaShell>(singleShell, ignoreCase: true);
    shelfEntries = [.. Enumerable.Repeat(
        shelfEntries.First(entry => entry.Profile.Shell == only),
        int.Parse(ArgumentValue("--shelf-count") ?? "5"))];
}

var shelfProfiles = shelfEntries.Select(entry => entry.Profile).ToArray();
// The shelf entry each shell is drawn with on the turntable sheet — its finish and, through that,
// the shape of its stand-in cover. Grouped rather than keyed directly because a shell no longer has
// one entry: PS2 and PSP share the disc case, and ToDictionary threw on the duplicate the moment the
// second one was added. The sheet is one row per shell, so it takes the first — the disc case's own
// PS2 black. Finishes that share geometry are compared on the shelf shot, which draws them all.
var shelfEntryByShell = shelfEntries
    .GroupBy(entry => entry.Profile.Shell)
    .ToDictionary(group => group.Key, group => group.First());

var sheetColumns = poses.Length;
var shells = MediaShellCatalog.All.ToArray();
var sheet = new byte[width * sheetColumns * height * shells.Length * 4];

for (var row = 0; row < shells.Length; row++)
{
    var shell = shells[row];
    var entry = shelfEntryByShell.GetValueOrDefault(shell);

    // Per shell, not once for the whole sheet. The turntable is the only straight-on view of a
    // label there is, so it is where a fit is actually judged — and judging a SNES cartridge's
    // landscape recess against a portrait stand-in tells you nothing about the landscape scan the
    // scraper will hand it. Same reason the shelf shot below does this; the sheet was simply missed
    // the first time, and it is the more important of the two.
    //
    // This is also the only thing that sets the turntable's cover art. A single 0.707 stand-in used
    // to be assigned once before this loop, which every iteration then overwrote — dead by the time
    // the per-shell call arrived, and its comment still claimed it was what the shells were drawn
    // with. --no-cover skips it here instead: that flag exercises the real fallback for a game with
    // no scraped art, and is the check that no retail artwork baked into a shell shows through.
    if (entry is not null && !args.Contains("--no-cover"))
    {
        renderer.SetCoverArt(TestCover.Create(entry.CoverAspect));
    }

    for (var column = 0; column < poses.Length; column++)
    {
        var (name, yaw, pitch) = poses[column];
        stopwatch.Restart();
        renderer.Render(
            shell, target, (uint)width, (uint)height, yaw, pitch,
            entry?.Profile.MaterialVariant ?? string.Empty);
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
// A --shelf-of row has no SNES cartridge in it; focus its middle instead of indexing with -1.
var shelfFocus = Array.FindIndex(shelfProfiles, profile => profile.Shell == MediaShell.SnesCartridge);
if (shelfFocus < 0)
{
    shelfFocus = shelfProfiles.Length / 2;
}
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
        renderer.SetCoverArt(key, TestCover.Create(shelfEntries[index].CoverAspect));
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

// The acceptance composition deliberately mixes a keep case with cartridges, so the tallest medium
// in it is what the shared camera frames — exactly as the app frames a whole library view.
var shelfMediaHeight = shelfProfiles.Max(
    profile => profile.HeightInShelfUnits + profile.FloorClearanceInShelfUnits);

// Derived, not chosen. A hardcoded width silently truncates the row every time a medium is added,
// which is not a cosmetic default — the shot is the artefact a reviewer trusts to show what
// shipped, and a medium outside the frame is a medium nobody looks at. That is how the Mega Drive
// kept a profile a quarter too big for a whole milestone. It was raised by hand twice while PSP
// and the case finishes were added, then immediately fell short again when the jewel cases merged
// in, which is the point at which guessing stopped being worth defending.
//
// The row is centred on the focused item rather than in the frame, so what has to fit is twice the
// larger of the two distances from focus to the ends — not the row's total width. Half a medium's
// width at each end plus a margin keeps the outermost shells off the frame edge.
if (ArgumentValue("--shelf-width") is null)
{
    var leftEdge = shelfCentres[0] - (shelfProfiles[0].WidthInShelfUnits * 0.5f);
    var rightEdge = shelfCentres[^1] + (shelfProfiles[^1].WidthInShelfUnits * 0.5f);
    var reach = MathF.Max(shelfAnchor - leftEdge, rightEdge - shelfAnchor);
    var aspect = MediaShellRenderer.ShelfAspectForVisibleWidth(
        (reach * 2f) + 0.3f, shelfMediaHeight);
    shelfWidth = (int)MathF.Ceiling(shelfHeight * aspect);
    Console.WriteLine(
        $"  shelf frame: {shelfWidth}x{shelfHeight} for {shelfEntries.Count} media "
        + $"(aspect {aspect:F2}; pass --shelf-width to override)");
}

var shelfTarget = CreateTargetFramebuffer(gl, (uint)shelfWidth, (uint)shelfHeight);
stopwatch.Restart();
renderer.RenderShelf(shelfItems, shelfMediaHeight, shelfTarget, (uint)shelfWidth, (uint)shelfHeight);
gl.Finish();
var shelfFrame = ReadPixels(gl, shelfTarget, shelfWidth, shelfHeight);
Composite(shelfFrame, background);
var shelfPath = Path.Combine(outputDirectory, "physical-shelf-scene.png");
PngWriter.Write(shelfPath, shelfWidth, shelfHeight, shelfFrame);
Console.WriteLine($"  {shelfPath} ({stopwatch.ElapsedMilliseconds} ms)");

// A strip through the disc launch. The poses are stated here rather than driven by the app's
// PhysicalShelfLaunchTransitionModel, which this tool cannot reference for the same reason it
// cannot reference MediaShellMap — the renderer knows nothing about the app. They are review
// samples of the renderer's disc path, not a second copy of the choreography: what this shot is
// for is the disc's finish, its size against the case, and the two bodies not intersecting.
(string Name, float CaseLift, float CaseDepth, float CaseScale, MediaShelfDiscPose Disc)[] launchFrames =
[
    // The disc holds the case's own depth until it is clear of the edge — that occlusion is the
    // whole of the reveal, so the strip samples it twice on the way out.
    ("stowed", 0.10f, 0.16f, 1.10f, new MediaShelfDiscPose(0f, 0.10f, 0.16f, 0f, 0f, Scale: 1f)),
    ("half-out", 0.10f, 0.16f, 1.10f, new MediaShelfDiscPose(0.24f, 0.10f, 0.16f, 2.00f, 0f, Scale: 1f)),
    ("clear", 0.10f, 0.16f, 1.10f, new MediaShelfDiscPose(0.44f, 0.10f, 0.16f, 3.77f, 0f, Scale: 1f)),
    // Mid-flip, showing the data side the case never had.
    ("turned-over", 0.10f, 0.16f, 1.10f,
        new MediaShelfDiscPose(0.44f, 0.10f, 0.40f, 3.77f, 0f, Flip: MathF.PI, Scale: 1f)),
    ("spun-up", 0.03f, 0.05f, 1.02f, new MediaShelfDiscPose(0.10f, 0.12f, 0.34f, 9.60f, -0.31f, Scale: 1.10f)),
    ("laid-flat", 0f, 0f, 1f, new MediaShelfDiscPose(0f, -0.30f, 0.40f, 18.0f, -1.31f, Scale: 1.05f)),
];

var launchProfile = new PhysicalMediaProfile(
    MediaShell.DiscKeepCase,
    new Vector3(135f, 190f, 14f),
    PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
    "ps2-black",
    "disc-from-case",
    DiscDiameterMillimetres: 120f);
var launchWidth = shelfHeight * 3 / 4;
var launchTarget = CreateTargetFramebuffer(gl, (uint)launchWidth, (uint)shelfHeight);
var launchStrip = new byte[launchWidth * launchFrames.Length * shelfHeight * 4];
// Front is the case's sleeve; slot 3 is the disc's own scraped label. Uploading both is what
// shows the routing working — the same game, two shells, two different pictures.
renderer.SetPanelArt(500L, 0, TestCover.Create());
renderer.SetPanelArt(500L, 3, TestCover.Create());

for (var index = 0; index < launchFrames.Length; index++)
{
    var (name, lift, depth, scale, disc) = launchFrames[index];
    renderer.RenderShelf(
        [
            new MediaShelfRenderItem(
                500L, launchProfile, 0f, 1f, 0f, 0f,
                MediaShellRenderer.ToLinear(0.31f, 0.55f, 0.76f),
                lift, depth, scale, disc),
        ],
        launchProfile.HeightInShelfUnits,
        launchTarget,
        (uint)launchWidth,
        (uint)shelfHeight);
    gl.Finish();
    var launchFrame = ReadPixels(gl, launchTarget, launchWidth, shelfHeight);
    Composite(launchFrame, background);
    BlitIntoSheet(
        launchStrip, launchWidth * launchFrames.Length, launchFrame,
        launchWidth, shelfHeight, index * launchWidth, 0);
    Console.WriteLine($"  disc launch frame '{name}'");
}

var launchPath = Path.Combine(outputDirectory, "disc-launch-frames.png");
PngWriter.Write(launchPath, launchWidth * launchFrames.Length, shelfHeight, launchStrip);
Console.WriteLine($"  {launchPath}");

string? ArgumentValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static System.Numerics.Quaternion ParseQuaternion(string value)
{
    var parts = value
        .Split(',', StringSplitOptions.TrimEntries)
        .Select(part => float.Parse(part, System.Globalization.CultureInfo.InvariantCulture))
        .ToArray();
    if (parts.Length != 4)
    {
        throw new ArgumentException("--model-quat wants x,y,z,w.");
    }

    return System.Numerics.Quaternion.Normalize(
        new System.Numerics.Quaternion(parts[0], parts[1], parts[2], parts[3]));
}

static string Slug(MediaShell shell) => shell switch
{
    MediaShell.SnesCartridge => "snes-cartridge",
    MediaShell.GbaCartridge => "gba-cartridge",
    MediaShell.GbcCartridge => "gbc-cartridge",
    MediaShell.JewelCase => "jewel-case",
    MediaShell.ArcadeCabinet => "arcade-cabinet",
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
        // Mirrors MediaShelf3DControl: the row reserves each medium's turning circle.
        var width = profiles[index].TurningWidthInShelfUnits;
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
