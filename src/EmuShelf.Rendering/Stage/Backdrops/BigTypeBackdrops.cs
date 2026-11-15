using System.Numerics;

namespace EmuShelf.Rendering.Stage.Backdrops;

/// <summary>How a Big Type backdrop's frame reaches the 1920 × 1080 Stage (M47 B13).</summary>
public enum BackdropUpscale
{
    /// <summary>
    /// Visible pixels are the console's character: the frame keeps its console's size and is shown in whole pixels, every one the same
    /// number of screen pixels across, with no blur between them (a frame that does not divide the Stage is shown at the next whole
    /// scale up, centred, its edges cropped as a TV's overscan crops them).
    /// </summary>
    Crisp,

    /// <summary>Pixels are not the point: the frame is drawn larger than its console's size, so no stair-steps or soft upscaling show.</summary>
    Sharp,
}

/// <summary>
/// One animation Big Type shows (M47 B13): which, how far it is pulled toward its console's colour, the size its frame is drawn at, and
/// how that frame is shown. tokens.js <c>backdrops</c> holds the same table; a test holds the two equal.
/// </summary>
/// <param name="Pull">The recolour's strength, 0 to 1: the review page's <c>.tint</c> opacity (0 shows the animation as it is).</param>
/// <param name="BloomThreshold">Big Type's own bloom threshold (0 to 1), where it differs from the console's finish; null keeps it.</param>
public sealed record BigTypeBackdrop(string Id, float Pull, int Width, int Height, BackdropUpscale Upscale, float? BloomThreshold = null);

/// <summary>How a running backdrop is drawn: today's look (the catalog's sizes, no recolour) or Big Type's, with its ground colour.</summary>
/// <param name="Ground">The colour a Big Type backdrop is recoloured toward (sRGB, 0 to 1): its console's ground, or Recent's.</param>
public readonly record struct BackdropLook(bool BigType, Vector3 Ground)
{
    public static BackdropLook Classic => default;
}

/// <summary>
/// The Big Type look's own animated backdrops (M47 B13, the owner's choices of 15 November 2026 on backdrop-review.html): one animation
/// per console, nothing rotating, except the 3DS, which keeps today's three while new ones are designed; and Recent's checker. Today's
/// look keeps <c>KnownSystems</c>' <c>Backdrops</c> lists and the catalog's sizes untouched; every animation Big Type no longer shows
/// stays in the catalog for it.
/// </summary>
public static class BigTypeBackdrops
{
    /// <summary>The key of Recent's backdrop (Recent has no platform).</summary>
    public const string Recent = "recent";

    /// <summary>The Stage's canvas, which a crisp frame is scaled to fill in whole pixels.</summary>
    public const int StageWidth = 1920;

    public const int StageHeight = 1080;

    // The draw sizes: crisp ones keep their console's size; sharp ones are drawn as large as the Thor's GPU allowed (measured, M47 B13).
    private static readonly (string Owner, BigTypeBackdrop[] Backdrops)[] Table =
    [
        ("nes", [new("nes-stars", 0.7f, 256, 144, BackdropUpscale.Crisp)]),
        ("snes", [new("tiles", 0.5f, 320, 180, BackdropUpscale.Crisp)]),
        ("gbc", [new("gbc-attr", 0.55f, 160, 90, BackdropUpscale.Crisp)]),
        ("gba", [new("gba-iso", 0.6f, 240, 135, BackdropUpscale.Crisp)]),
        ("gamecube", [new("gc-fur", 0f, 960, 540, BackdropUpscale.Sharp)]),
        ("nds", [new("ds-dots", 1f, 256, 144, BackdropUpscale.Crisp)]),
        // Wii's background gradient (250 down to 224) crossed its bloom threshold (240) two fifths of the way down, so the top clipped to
        // white and ended in a hard band; just above the gradient's brightest row only the pointer glows (the owner's "more smooth").
        ("wii", [new("wii-pointer-v2", 1f, 1920, 1080, BackdropUpscale.Sharp, BloomThreshold: 251f / 255f)]),
        ("3ds",
        [
            new("3ds-tiles", 0f, 1920, 1080, BackdropUpscale.Sharp),
            new("3ds-floorwave", 0f, 1920, 1080, BackdropUpscale.Sharp),
            new("3ds-tapwave", 0f, 1920, 1080, BackdropUpscale.Sharp),
        ]),
        ("megadrive", [new("md-pipe-v2", 1f, 320, 180, BackdropUpscale.Crisp)]),
        ("dreamcast", [new("dc-sea-v2", 0f, 1920, 1080, BackdropUpscale.Sharp)]),
        ("playstation", [new("tide", 0f, 320, 180, BackdropUpscale.Crisp)]),
        ("playstation2", [new("ps2-orbs-v3", 0f, 1920, 1080, BackdropUpscale.Sharp)]),
        ("psp", [new("psp-ribbon", 0.6f, 1920, 1080, BackdropUpscale.Sharp)]),
        ("playstation3", [new("ps3-sheet", 0f, 1920, 1080, BackdropUpscale.Sharp)]),
        ("arcade", [new("arc-vector-v3", 1f, 320, 180, BackdropUpscale.Crisp)]),
        ("steam", [new("steam-mesh", 0f, 1920, 1080, BackdropUpscale.Sharp)]),
        (Recent, [new("arc-checker", 1f, 320, 180, BackdropUpscale.Crisp)]),
    ];

    private static readonly Dictionary<string, string[]> IdsByOwner =
        Table.ToDictionary(row => row.Owner, row => row.Backdrops.Select(backdrop => backdrop.Id).ToArray(), StringComparer.Ordinal);

    private static readonly Dictionary<string, BigTypeBackdrop> ById =
        Table.SelectMany(row => row.Backdrops).ToDictionary(backdrop => backdrop.Id, StringComparer.Ordinal);

    /// <summary>Every console (and Recent) with the animations Big Type shows for it, in the table's order.</summary>
    public static IEnumerable<(string Owner, IReadOnlyList<BigTypeBackdrop> Backdrops)> All =>
        Table.Select(row => (row.Owner, (IReadOnlyList<BigTypeBackdrop>)row.Backdrops));

    /// <summary>The animations Big Type shows for a system id (<c>KnownSystems</c>) or <see cref="Recent"/>; null when it has none.</summary>
    public static IReadOnlyList<string>? IdsFor(string? owner) => owner is not null && IdsByOwner.TryGetValue(owner, out var ids) ? ids : null;

    /// <summary>Big Type's entry for a backdrop id; null for one Big Type does not show.</summary>
    public static BigTypeBackdrop? Find(string? id) => id is not null && ById.TryGetValue(id, out var entry) ? entry : null;

    /// <summary>
    /// A measurement override for the Thor (M47 B13, <c>debug.emushelf.bdsize</c>): every sharp backdrop drawn this wide (its height
    /// at 16:9), or at its catalog size when 0. Null draws each at the table's size. Never set by the app itself.
    /// </summary>
    public static int? SharpWidthOverride { get; set; }

    /// <summary>The size a backdrop's frame is drawn at under Big Type (the override applied).</summary>
    public static (int Width, int Height) DrawSize(BigTypeBackdrop entry, BackdropSpec spec)
    {
        if (entry.Upscale == BackdropUpscale.Sharp && SharpWidthOverride is { } width)
            return width <= 0 ? (spec.Width, spec.Height) : (width, (int)MathF.Round(width * 9f / 16f));
        return (entry.Width, entry.Height);
    }

    /// <summary>
    /// The whole-pixel scale a crisp frame is shown at on the Stage: the smallest whole number that fills it (7.5 for a 256-wide
    /// frame becomes 8, its edges cropped).
    /// </summary>
    public static int CrispScale(int width, int height) =>
        Math.Max((int)Math.Ceiling(StageWidth / (double)width - 1e-9), (int)Math.Ceiling(StageHeight / (double)height - 1e-9));

    /// <summary>The share of a crisp frame shown across each axis at <see cref="CrispScale"/> (1 when it divides the Stage).</summary>
    public static Vector2 CrispShown(int width, int height)
    {
        var scale = CrispScale(width, height);
        return new Vector2(StageWidth / (float)(width * scale), StageHeight / (float)(height * scale));
    }
}
