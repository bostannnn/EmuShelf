namespace EmuShelf.Rendering.Stage.Backdrops;

/// <summary>Which of the Big Type Stage's three text zones a picture belongs to (tokens.js <c>legibility</c>).</summary>
public enum StageInkZone
{
    /// <summary>Not text: drawn as pictured, never tinted.</summary>
    None,

    /// <summary>The console name, the big name and the two lines under it.</summary>
    Left,

    /// <summary>The battery and the clock.</summary>
    TopRight,

    /// <summary>The button hints.</summary>
    BottomRight,
}

/// <summary>
/// One zone's answer: whether its text takes the dark ink (the console's colour mixed toward near-black; otherwise white) and
/// how strong the veil of the opposite tone behind it is.
/// </summary>
/// <param name="Baked">False where the prototype's rule picked different inks at different moments, so the answer here is a
/// fixed choice rather than the table's (only <c>ps2-sea</c>'s hints; M47 B4).</param>
public readonly record struct StageZoneInk(bool Dark, float Veil, bool Baked = true);

/// <summary>A backdrop's answer for the three zones.</summary>
public sealed record BackdropInk(string Console, StageZoneInk Left, StageZoneInk TopRight, StageZoneInk BottomRight)
{
    public StageZoneInk this[StageInkZone zone] => zone switch
    {
        StageInkZone.Left => Left,
        StageInkZone.TopRight => TopRight,
        StageInkZone.BottomRight => BottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(zone)),
    };
}

/// <summary>
/// The Big Type legibility rule answered once per approved backdrop (M47 R2, B4): for each of the Stage's three text zones,
/// the ink and the veil. Baked from <c>docs/prototypes/handheld-big-type/legibility.json</c>, which the mock's stress sheet
/// measured on the JavaScript prototypes of these backdrops; a test holds the two equal. The compositor reads it for the
/// backdrop that is actually showing, so the ink follows the rotation.
/// </summary>
public static class BackdropLegibility
{
    /// <summary>The veil under the left zone when nothing stronger is needed (tokens.js <c>BaseVeilLeft</c>).</summary>
    public const float BaseVeilLeft = 0.28f;

    /// <summary>The veil in a right corner when nothing stronger is needed (tokens.js <c>BaseVeilCorner</c>).</summary>
    public const float BaseVeilCorner = 0.25f;

    /// <summary>
    /// Where no backdrop shader runs (the pictured era motif, a light field in the console's hue): the dark ink, with the white veil
    /// at its resting strength (M47 B5; B4 had white, which read poorly on the light field).
    /// </summary>
    public static BackdropInk Fallback { get; } = new(string.Empty, Dark(BaseVeilLeft), Dark(BaseVeilCorner), Dark(BaseVeilCorner));

    /// <summary>The answer for a backdrop by its <see cref="BackdropCatalog"/> id; <see cref="Fallback"/> for null or an unknown id.</summary>
    public static BackdropInk For(string? id) => id is not null && Table.TryGetValue(id, out var ink) ? ink : Fallback;

    /// <summary>
    /// The answer on the narrow Stage (the screens swapped, M47 B11), which shows the backdrop's middle 1240 and so puts the left zone
    /// over other parts of it: the table's ink always, and its veil, or a stronger one where the table's left a zone under tokens.js
    /// <c>legibility.Pass</c> there (<see cref="NarrowLeftVeils"/>).
    /// </summary>
    public static BackdropInk For(string? id, bool narrow)
    {
        var answer = For(id);
        if (!narrow || id is null || !NarrowLeftVeils.TryGetValue(id, out var veil) || veil <= answer.Left.Veil)
            return answer;
        return answer with { Left = answer.Left with { Veil = veil } };
    }

    /// <summary>
    /// The app's own (M47 B11): the left veil on the narrow Stage for the backdrops whose middle reads darker (or lighter) behind the
    /// left zone than the whole did, measured through the real compositor over every backdrop at three moments
    /// (<c>tools/EmuShelf.Rendering.Preview -- --stage-narrow-legibility</c>, the worst of the 96th or 4th percentile as the mock's rule
    /// reads it): a veil that brings every zone to at least tokens.js <c>legibility.Pass</c> (3.0) there. <c>arc-checker</c> splits its
    /// inks (dark left, white clock) and the narrow Stage's left veil reaches under its clock, so its veil is the one that keeps both.
    /// The ink is never changed.
    /// </summary>
    public static IReadOnlyDictionary<string, float> NarrowLeftVeils { get; } = new Dictionary<string, float>(StringComparer.Ordinal)
    {
        // Measured 2026-11-15 on this Mac's GL: the table's veil left these under 3.0 on the narrow Stage (2.26, 2.65, 2.09); with
        // these 3.38 (its clock 3.15), 4.31 and 3.35.
        ["arc-checker"] = 0.46f,
        ["gc-caustics"] = 0.6f,
        ["ps2-sea"] = 0.66f,
    };

    /// <summary>Every baked backdrop, by id.</summary>
    public static IReadOnlyDictionary<string, BackdropInk> All => Table;

    private static StageZoneInk Dark(float veil) => new(true, veil);

    private static StageZoneInk White(float veil) => new(false, veil);

    // ps2-sea's hints change ink over time in the prototype: white, with the veil at 0.5 (M47 B4; for the owner to check on the Thor).
    private static StageZoneInk Changes(float veil) => new(false, veil, Baked: false);

    private static readonly Dictionary<string, BackdropInk> Table = new(StringComparer.Ordinal)
    {
        ["3ds-floorwave"] = new("3ds", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["3ds-tapwave"] = new("3ds", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["3ds-tiles"] = new("3ds", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["arc-bulbs-v2"] = new("arcade", White(0.28f), White(0.25f), White(0.25f)),
        ["arc-checker"] = new("arcade", Dark(0.28f), White(0.25f), Dark(0.25f)),
        ["arc-plating"] = new("arcade", White(0.28f), White(0.25f), White(0.25f)),
        ["arc-vector-v3"] = new("arcade", White(0.28f), White(0.25f), White(0.25f)),
        ["dc-checker"] = new("dreamcast", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["dc-fog-v3"] = new("dreamcast", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["dc-sea-v2"] = new("dreamcast", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["gc-caustics"] = new("gamecube", Dark(0.39f), White(0.25f), Dark(0.25f)),
        ["gc-cubes-v3"] = new("gamecube", White(0.28f), White(0.25f), White(0.25f)),
        ["gc-fur"] = new("gamecube", White(0.38f), White(0.25f), White(0.29f)),
        ["gc-lens"] = new("gamecube", White(0.28f), White(0.25f), White(0.25f)),
        ["gba-corridor-v2"] = new("gba", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["gba-iso"] = new("gba", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["gbc-attr"] = new("gbc", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["gbc-bands"] = new("gbc", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["gbc-confetti-v2"] = new("gbc", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["gbc-sheen-v2"] = new("gbc", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["md-pipe-v2"] = new("megadrive", Dark(0.41f), Dark(0.35f), Dark(0.25f)),
        ["md-planes"] = new("megadrive", White(0.28f), White(0.25f), White(0.25f)),
        ["md-speed-v3"] = new("megadrive", White(0.28f), White(0.25f), White(0.25f)),
        ["ds-dots"] = new("nds", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ds-flip"] = new("nds", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ds-ripplefloor-v2"] = new("nds", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ds-ripples"] = new("nds", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["nes-flow"] = new("nes", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["nes-meta"] = new("nes", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["nes-raster"] = new("nes", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["nes-stars"] = new("nes", White(0.28f), White(0.25f), White(0.25f)),
        ["field"] = new("playstation", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["frames"] = new("playstation", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ps1-pipe"] = new("playstation", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ps1-terrace-v2"] = new("playstation", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["tide"] = new("playstation", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ps2-orbs-v3"] = new("playstation2", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ps2-sea"] = new("playstation2", Dark(0.3f), Dark(0.25f), Changes(0.5f)),
        ["ps2-towers"] = new("playstation2", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["ps2-trails-v3"] = new("playstation2", White(0.28f), White(0.25f), White(0.25f)),
        ["ps3-sheet"] = new("playstation3", White(0.28f), White(0.25f), White(0.25f)),
        ["ps3-twin"] = new("playstation3", White(0.28f), White(0.25f), White(0.25f)),
        ["ps3-wire"] = new("playstation3", White(0.28f), White(0.25f), White(0.25f)),
        ["psp-field-v2"] = new("psp", White(0.28f), White(0.25f), White(0.25f)),
        ["runway"] = new("snes", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["snes-lake-v2"] = new("snes", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["snes-mosaic-v2"] = new("snes", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["tiles"] = new("snes", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["steam-grid-v2"] = new("steam", White(0.28f), White(0.25f), White(0.25f)),
        ["steam-mesh"] = new("steam", White(0.28f), White(0.25f), White(0.25f)),
        ["steam-wall-v2"] = new("steam", White(0.28f), White(0.25f), White(0.25f)),
        ["wii-pointer-v2"] = new("wii", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
        ["wii-tiles"] = new("wii", Dark(0.28f), Dark(0.25f), Dark(0.25f)),
    };
}
