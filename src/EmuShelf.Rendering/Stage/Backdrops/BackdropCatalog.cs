namespace EmuShelf.Rendering.Stage.Backdrops;

/// <summary>How the console's low-resolution frame is overlaid when it is scaled up to the Stage.</summary>
public enum BackdropGrid
{
    None,

    /// <summary>An unlit LCD's cell gaps: the right and bottom third of every pixel darkened (Game Boy Color).</summary>
    Lcd,

    /// <summary>CRT scanlines: the bottom half of every row darkened (arcade monitors).</summary>
    Crt,
}

/// <summary>What a scene pass draws.</summary>
public enum BackdropPrimitive
{
    Triangles,
    Lines,
    Points,
}

/// <summary>How a scene pass lands on what is already in the frame.</summary>
public enum BackdropBlend
{
    Opaque,

    /// <summary>Added on top (light, glows, particles).</summary>
    Additive,

    /// <summary>Blended by the fragment's alpha (glass, reflections, translucent floors).</summary>
    Alpha,
}



/// <summary>
/// One draw into the backdrop's low-resolution frame. Every pass is attribute-less: the vertex shader builds
/// its geometry from <c>gl_VertexID</c> (a fullscreen triangle, a grid of quads, a field of boxes or points).
/// <paramref name="First"/> offsets <c>gl_VertexID</c>, so one vertex shader can serve two passes (a reflection
/// drawn before a floor, the real thing after it).
/// </summary>
public sealed record BackdropPass(
    string Vertex,
    string Fragment,
    int VertexCount,
    BackdropPrimitive Primitive = BackdropPrimitive.Triangles,
    BackdropBlend Blend = BackdropBlend.Opaque,
    bool DepthTest = false,
    bool DepthWrite = true,
    int First = 0)
{
    /// <summary>A fragment shader over the whole frame.</summary>
    public static BackdropPass Fullscreen(string fragment) => new("fullscreen.vert", fragment, 3);
}

/// <summary>
/// The console's output stage, applied when the low-resolution frame is shown on the Stage: the same steps,
/// in the same order, as the motion lab's <c>gOut</c> / <c>pPresent</c> (backdrops.js).
/// </summary>
/// <param name="Smooth">Linear upscaling (later consoles) instead of whole pixels.</param>
/// <param name="Bits">Bits per colour channel (3 for the Mega Drive's 9-bit colour); 0 leaves colour alone.</param>
/// <param name="Dither">Strength of the 4×4 ordered dither used with <paramref name="Bits"/>; 0 is none.</param>
/// <param name="Composite">The TV's composite blur across each row (turns Mega Drive dither into transparency).</param>
/// <param name="BloomThreshold">Brightness (0–1) above which a glow is added; 0 is no bloom.</param>
/// <param name="Ghost">An LCD's slow pixels: how much of the previous frame lingers (0–1).</param>
/// <param name="Persist">Phosphor / motion trails: the previous frame decays by this factor and the brighter wins.</param>
/// <param name="Interlace">How much every other row dims, alternating each frame (the PS2's interlaced output).</param>
public sealed record BackdropOutput(
    bool Smooth = false,
    int Bits = 0,
    float Dither = 0f,
    BackdropGrid Grid = BackdropGrid.None,
    bool Composite = false,
    float BloomThreshold = 0f,
    float BloomAmount = 0f,
    float Ghost = 0f,
    float Persist = 0f,
    float Interlace = 0f);

/// <summary>
/// One animated backdrop: drawn into a frame at its console's own resolution by <see cref="Passes"/>, then shown
/// through <see cref="Output"/>. The look and the motion are ports of the owner-approved motion lab
/// (docs/prototypes/handheld-mode/backdrops-motion-study: backdrops.js, picks.json); <see cref="Id"/> matches it.
/// </summary>
/// <param name="Feedback">The passes draw over the backdrop's own previous frame (<c>uPrevious</c>), for frame feedback.</param>
/// <param name="Finish">A fragment shader that turns the frame (<c>uScene</c>) into what is shown, without feeding back.</param>
public sealed record BackdropSpec(string Id, int Width, int Height, IReadOnlyList<BackdropPass> Passes, BackdropOutput Output,
    bool Feedback = false, string? Finish = null);

/// <summary>How a system's backdrops take turns (M45 H7b): one at a time, a different one each turn.</summary>
public static class BackdropRotation
{
    /// <summary>The crossfade from one backdrop to the next within a system, as in the motion lab.</summary>
    public const float FadeMilliseconds = 1600f;

    /// <summary>
    /// How long a backdrop shows before the next: 12 s when the system has several (the motion lab's dwell);
    /// with only one, it fades into a fresh start of itself every five minutes, so its clock never wraps.
    /// </summary>
    public static float DwellMilliseconds(int count) => count > 1 ? 12_000f : 300_000f;

    /// <summary>A backdrop at random from the system's list, never the one showing now (unless it is the only one).</summary>
    public static string Pick(IReadOnlyList<string> ids, string? current, Random random)
    {
        if (ids.Count == 1)
            return ids[0];
        var choices = ids.Where(id => id != current).ToList();
        return choices[random.Next(choices.Count)];
    }
}

/// <summary>Every animated backdrop the Stage can draw, by id (see <c>KnownSystems</c> for which system uses which).</summary>
public static class BackdropCatalog
{
    /// <summary>A backdrop advances in 30 Hz steps, as the motion lab did; the Stage redraws no faster for it.</summary>
    public const float StepMilliseconds = 1000f / 30f;

    /// <summary>The backdrop's clock wraps here so the shaders' trigonometry keeps its precision.</summary>
    public const float ClockWrapSeconds = 600f;

    // Each console's output stage (the motion lab's CONSOLES post settings).
    private static readonly BackdropOutput Nes = new();
    private static readonly BackdropOutput Snes = new();
    private static readonly BackdropOutput GameBoyColor = new(Bits: 5, Grid: BackdropGrid.Lcd, Ghost: 0.45f);
    private static readonly BackdropOutput GameBoyAdvance = new(Bits: 5);
    private static readonly BackdropOutput GameCube = new(Smooth: true, BloomThreshold: 200f / 255f, BloomAmount: 0.5f);
    private static readonly BackdropOutput Wii = new(Smooth: true, BloomThreshold: 240f / 255f, BloomAmount: 0.3f);
    private static readonly BackdropOutput MegaDrive = new(Bits: 3, Composite: true);
    private static readonly BackdropOutput Arcade = new(Smooth: true, Grid: BackdropGrid.Crt, BloomThreshold: 140f / 255f, BloomAmount: 0.6f);
    private static readonly BackdropOutput Modern = new(Smooth: true);
    private static readonly BackdropOutput NintendoDs = new(Bits: 6);
    private static readonly BackdropOutput Nintendo3ds = new();
    private static readonly BackdropOutput PlayStation = new(Bits: 5, Dither: 0.62f);
    private static readonly BackdropOutput PlayStation2 = new(Smooth: true, BloomThreshold: 228f / 255f, BloomAmount: 0.4f, Interlace: 0.035f);
    private static readonly BackdropOutput PlayStation3 = new(Smooth: true, BloomThreshold: 150f / 255f, BloomAmount: 0.7f);

    private static readonly BackdropSpec[] Specs =
    [
        // ---- H7b, the rest of picks.json: 2D and plane backdrops ----
        Flat("nes-flow", 256, 144, Nes),
        Flat("nes-stars", 256, 144, Nes),
        Flat("nes-raster", 256, 144, Nes),
        Flat("snes-mosaic-v2", 320, 180, Snes),
        Flat("snes-lake-v2", 320, 180, Snes),
        Flat("tiles", 320, 180, Snes),
        Flat("gbc-attr", 160, 90, GameBoyColor),
        Flat("gbc-sheen-v2", 160, 90, GameBoyColor),
        Flat("gbc-confetti-v2", 160, 90, GameBoyColor),
        Flat("gba-corridor-v2", 240, 135, GameBoyAdvance),
        Flat("gc-lens", 240, 135, GameCube),
        Flat("gc-fur", 320, 180, GameCube),
        Flat("wii-pointer-v2", 480, 270, Wii),
        Flat("md-speed-v3", 320, 180, MegaDrive),
        Flat("md-pipe-v2", 320, 180, MegaDrive),
        Flat("arc-bulbs-v2", 320, 180, Arcade),
        Flat("arc-plating", 320, 180, Arcade),
        Flat("steam-grid-v2", 240, 135, Modern),

        // ---- H7b: polygons, particles and lines ----
        new("3ds-floorwave", 400, 225,
        [
            BackdropPass.Fullscreen("backdrop-3ds-floor-bg.frag"),
            // 17 × 12 pillow tiles laid as a floor, a box (36 vertices) each.
            new("backdrop-3ds-floorwave.vert", "backdrop-3ds-tiles.frag", 17 * 12 * 36, DepthTest: true),
        ], Nintendo3ds),
        new("3ds-tapwave", 400, 225,
        [
            BackdropPass.Fullscreen("backdrop-3ds-bg.frag"),
            new("backdrop-3ds-tapwave.vert", "backdrop-3ds-tiles.frag", 91 * 36, DepthTest: true),
        ], Nintendo3ds),
        new("ds-ripplefloor-v2", 256, 144,
        [
            BackdropPass.Fullscreen("backdrop-ds-ripplefloor-bg.frag"),
            // 49 lines of 280 dots running away, 38 lines of 551 across.
            new("backdrop-ds-ripplefloor-v2.vert", "backdrop-color.frag", (49 * 280) + (38 * 551), BackdropPrimitive.Points),
            // Nine tap slots, a quad each.
            new("backdrop-ds-ripplefloor-taps.vert", "backdrop-color.frag", 9 * 6, Blend: BackdropBlend.Alpha),
        ], NintendoDs),
        new("ds-dots", 256, 144,
        [
            BackdropPass.Fullscreen("backdrop-ds-dots-bg.frag"),
            new("backdrop-ds-dots.vert", "backdrop-color.frag", 34 * 33 * 2, BackdropPrimitive.Points),
        ], NintendoDs),
        new("ds-flip", 256, 144,
        [
            BackdropPass.Fullscreen("backdrop-ds-flip-paper.frag"),
            // Five doodles: 72 outline segments and 3 sparkle dashes each.
            new("backdrop-ds-flip.vert", "backdrop-color.frag", 5 * 75 * 2, BackdropPrimitive.Lines, BackdropBlend.Alpha),
        ], NintendoDs),
        new("gc-cubes-v3", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-gc-cubes-v3.frag"),
            new("backdrop-gc-cubes-v3.vert", "backdrop-lit.frag", 21 * 16 * 36, DepthTest: true),
        ], GameCube),
        new("dc-fog-v3", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-dc-fog-v3.frag"),
            new("backdrop-dc-fog-v3.vert", "backdrop-lit.frag", 17 * 14 * 36, DepthTest: true),
        ], Modern),
        new("dc-sea-v2", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-dc-sea-v2.frag"),
            // 48 columns × 32 bands between the sea's 33 depth rows.
            new("backdrop-dc-sea-v2.vert", "backdrop-color.frag", 48 * 32 * 6, DepthTest: true),
        ], Modern),
        new("tide", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-tide-sky.frag"),
            new("backdrop-tide.vert", "backdrop-ps1.frag", 28 * 11 * 6, DepthTest: true),
        ], PlayStation),
        new("frames", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-frames-sky.frag"),
            // 10 frames × 8 segments × a face and an inner side.
            new("backdrop-frames.vert", "backdrop-ps1.frag", 10 * 8 * 2 * 6, DepthTest: true),
        ], PlayStation),
        new("ps1-terrace-v2", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-ps1-terrace-v2.frag"),
            // 19 × 13 blocks: top, front and inner side.
            new("backdrop-ps1-terrace-v2.vert", "backdrop-ps1.frag", 19 * 13 * 18, DepthTest: true),
        ], PlayStation),
        new("ps1-pipe", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-ps1-pipe.frag"),
            new("backdrop-ps1-pipe.vert", "backdrop-ps1.frag", 16 * 12 * 6, DepthTest: true),
        ], PlayStation),
        new("ps2-towers", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-ps2-towers-sky.frag"),
            // 165 tower slots × 5 faces × 6 vertices: first the reflections, then (after the floor) the towers.
            new("backdrop-ps2-towers.vert", "backdrop-color.frag", 165 * 30, Blend: BackdropBlend.Alpha),
            new("backdrop-ps2-towers-floor.vert", "backdrop-color.frag", 15 * 14 * 6, Blend: BackdropBlend.Alpha),
            new("backdrop-ps2-towers.vert", "backdrop-color.frag", 165 * 30, Blend: BackdropBlend.Alpha, First: 165 * 30),
        ], PlayStation2),
        new("ps2-trails-v3", 480, 270,
        [
            // The last frame decays toward the gradient, then 1500 particles and 40 sparkles are added.
            BackdropPass.Fullscreen("backdrop-ps2-trails-v3.frag"),
            new("backdrop-ps2-trails.vert", "backdrop-splat.frag", 1540, BackdropPrimitive.Points, BackdropBlend.Additive),
        ], PlayStation2 with { BloomThreshold = 120f / 255f, BloomAmount = 0.7f }, Feedback: true, Finish: "backdrop-ps2-trails-finish.frag"),
        new("ps2-orbs-v3", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-ps2-orbs-sky.frag"),
            // 7 pearls × a 22 × 13 sphere: first mirrored under the floor, then (after it) the pearls.
            new("backdrop-ps2-orbs.vert", "backdrop-color.frag", 7 * 22 * 13 * 6, DepthTest: true),
            new("backdrop-ps2-orbs-floor.vert", "backdrop-color.frag", 20 * 16 * 6, Blend: BackdropBlend.Alpha, DepthTest: true, DepthWrite: false),
            new("backdrop-ps2-orbs.vert", "backdrop-color.frag", 7 * 22 * 13 * 6, DepthTest: true, First: 7 * 22 * 13 * 6),
        ], PlayStation2),
        new("ps3-wire", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-ps3-bg.frag"),
            new("backdrop-ps3-wire.vert", "backdrop-color.frag", (49 * 39 * 2) + (38 * 56 * 2), BackdropPrimitive.Lines, BackdropBlend.Additive),
        ], PlayStation3 with { Persist = 0.7f }),
        new("ps3-twin", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-ps3-bg.frag"),
            new("backdrop-ps3-twin.vert", "backdrop-color.frag", 2 * 36 * 49, BackdropPrimitive.Points, BackdropBlend.Additive),
        ], PlayStation3 with { Persist = 0.8f }),
        new("arc-vector-v3", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-arc-vector-bg.frag"),
            new("backdrop-arc-vector-v3.vert", "backdrop-color.frag", (25 * 42 * 2) + (22 * 48 * 2) + 24, BackdropPrimitive.Lines, BackdropBlend.Additive),
        ], Arcade with { Persist = 0.8f }),
        new("steam-wall-v2", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-steam-bg.frag"),
            // Reflections, the floor, then the covers: 2 walls × 4 rows × 14 covers.
            new("backdrop-steam-wall-v2.vert", "backdrop-color.frag", (2 * 2 * 4 * 14 * 6) + 6, Blend: BackdropBlend.Alpha),
        ], Modern),

        // ---- Nintendo ----
        Flat("nes-meta", 256, 144, new BackdropOutput()),
        Flat("runway", 320, 180, new BackdropOutput()),
        Flat("gbc-bands", 160, 90, new BackdropOutput(Bits: 5, Grid: BackdropGrid.Lcd, Ghost: 0.45f)),
        Flat("gba-iso", 240, 135, new BackdropOutput(Bits: 5)),
        Flat("gc-caustics", 240, 135, new BackdropOutput(Smooth: true, BloomThreshold: 200f / 255f, BloomAmount: 0.5f)),
        Flat("ds-ripples", 256, 144, new BackdropOutput(Bits: 6)),
        Flat("wii-tiles", 480, 270, new BackdropOutput(Smooth: true, BloomThreshold: 240f / 255f, BloomAmount: 0.3f)),
        new("3ds-tiles", 400, 225,
        [
            BackdropPass.Fullscreen("backdrop-3ds-bg.frag"),
            // 91 tile slots (13 × 7; the 9 behind the folder are skipped) × 36 vertices of a box.
            new("backdrop-3ds-tiles.vert", "backdrop-3ds-tiles.frag", 91 * 36, DepthTest: true),
        ], new BackdropOutput()),

        // ---- Sega ----
        Flat("md-planes", 320, 180, new BackdropOutput(Bits: 3, Composite: true)),
        Flat("dc-checker", 480, 270, new BackdropOutput(Smooth: true)),

        // ---- Sony ----
        new("field", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-field-sky.frag"),
            // 30 × 11 quads of the folded field, two triangles each.
            new("backdrop-field.vert", "backdrop-field.frag", 30 * 11 * 6, DepthTest: true),
        ], new BackdropOutput(Bits: 5, Dither: 0.62f)),
        new("ps2-sea", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-ps2-sea-sky.frag"),
            // 44 columns × 25 bands of the sea between its 26 depth rows.
            new("backdrop-ps2-sea.vert", "backdrop-ps2-sea.frag", 44 * 25 * 6, DepthTest: true),
        ], new BackdropOutput(Smooth: true, BloomThreshold: 228f / 255f, BloomAmount: 0.4f, Interlace: 0.035f)),
        new("psp-field-v2", 320, 180,
        [
            BackdropPass.Fullscreen("backdrop-psp-field-sky.frag"),
            new("backdrop-psp-field.vert", "backdrop-psp-field.frag", 44 * 31 * 6, DepthTest: true),
            // Three crests, 80 segments each, drawn three times (the line and its glow above and below).
            new("backdrop-psp-field-lines.vert", "backdrop-color.frag", 3 * 80 * 2 * 3, BackdropPrimitive.Lines, BackdropBlend.Additive),
        ], new BackdropOutput(Smooth: true, Ghost: 0.25f)),
        new("ps3-sheet", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-ps3-bg.frag"),
            // 61 × 40 particles.
            new("backdrop-ps3-sheet.vert", "backdrop-color.frag", 61 * 40, BackdropPrimitive.Points, BackdropBlend.Additive),
        ], new BackdropOutput(Smooth: true, BloomThreshold: 150f / 255f, BloomAmount: 0.7f, Persist: 0.8f)),

        // ---- Arcade and Valve ----
        Flat("arc-checker", 320, 180, new BackdropOutput(Smooth: true, Grid: BackdropGrid.Crt, BloomThreshold: 140f / 255f, BloomAmount: 0.6f)),
        new("steam-mesh", 480, 270,
        [
            BackdropPass.Fullscreen("backdrop-steam-bg.frag"),
            // 22 columns × 19 bands of the low-poly ground.
            new("backdrop-steam-mesh.vert", "backdrop-color.frag", 22 * 19 * 6, DepthTest: true),
        ], new BackdropOutput(Smooth: true)),
    ];

    private static readonly Dictionary<string, BackdropSpec> ById = Specs.ToDictionary(spec => spec.Id, StringComparer.Ordinal);

    public static IReadOnlyCollection<BackdropSpec> All => Specs;

    public static BackdropSpec? Find(string id) => ById.GetValueOrDefault(id);

    // A backdrop that is one fragment shader over the whole frame, named after its id.
    private static BackdropSpec Flat(string id, int width, int height, BackdropOutput output) =>
        new(id, width, height, [BackdropPass.Fullscreen($"backdrop-{id}.frag")], output);
}
