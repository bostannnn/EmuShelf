using System.Numerics;

namespace EmuShelf.Rendering;

/// <summary>
/// How hard the shelf is pushed through a CRT tube on the way to the screen.
/// </summary>
/// <remarks>
/// Every field is a separate knob rather than a baked constant, because a CRT emulation is judged
/// entirely by eye and the right answer depends on things this code cannot see — panel size, viewing
/// distance, whether the player wanted a nostalgic television or a subtle glass sheen. Only the
/// on/off switch is currently a user setting, though: the rest ship at the defaults below, which aim
/// at a couch — visible, but not so curved that the platform rail bends off the corners of the
/// screen. Keeping them as fields is what makes exposing them later a UI job rather than a
/// refactor.
/// </remarks>
public readonly record struct CrtPresentation
{
    /// <summary>Master mix against the untouched image. At 0 the pass is a pure copy.</summary>
    public float Intensity { get; init; }

    /// <summary>Barrel distortion. 0 is flat glass; ~0.12 is a late-model trinitron.</summary>
    public float Curvature { get; init; }

    /// <summary>
    /// How far the picture is zoomed to carry the warped edges off the panel.
    /// </summary>
    /// <remarks>
    /// Expressed as a multiple of the exact fit rather than as an absolute zoom, so it keeps working
    /// when the curvature moves: 1.0 lands the corners on the edge, above 1.0 adds margin, and 0
    /// restores the black surround. The cost is the same one every console had — a band around the
    /// outside of the picture is now off-screen, so anything the couch UI puts hard against an edge
    /// needs its own safe-area padding.
    /// </remarks>
    public float Overscan { get; init; }

    /// <summary>
    /// Overscan applied to the captured couch UI, on the same scale as <see cref="Overscan"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately independent, and normally 0. The scene needs zooming because its opaque backdrop
    /// leaves black wedges at the warped corners; the chrome is transparent out there and leaves
    /// none, so zooming it too would only push the platform rail off the top of the panel for no
    /// gain. It still warps with everything else — it simply is not cropped.
    /// </remarks>
    public float ChromeOverscan { get; init; }

    /// <summary>How black the gaps between beam traces go.</summary>
    public float ScanlineDepth { get; init; }

    /// <summary>How strongly the RGB phosphor triad tints adjacent columns.</summary>
    public float MaskStrength { get; init; }

    /// <summary>Width of one triad in output pixels.</summary>
    public float MaskPitch { get; init; }

    /// <summary>
    /// Scan lines across the tube's height.
    /// </summary>
    /// <remarks>
    /// The shelf is a smooth 3D render with no native line structure, so unlike a real CRT shader —
    /// which inherits its line count from the emulated framebuffer — this has to be invented. It is
    /// deliberately a count rather than a pixel pitch: tying it to the output resolution makes the
    /// effect vanish on a 4K panel and stripe a 720p one.
    /// </remarks>
    public float VirtualLines { get; init; }

    /// <summary>Halation: how far a lit phosphor bleeds into its neighbours.</summary>
    public float Bloom { get; init; }

    /// <summary>Corner falloff exponent. 0 disables it.</summary>
    public float Vignette { get; init; }

    /// <summary>
    /// How fast the scanline grid drifts vertically, in tube heights per second.
    /// </summary>
    /// <remarks>
    /// Small on purpose. A stationary grid reads as a texture laid over the picture rather than a
    /// raster being painted, but anything fast enough to track with the eye becomes a rolling
    /// picture — a fault, not a finish.
    /// </remarks>
    public float RollSpeed { get; init; }

    /// <summary>Strength of the wide brightness band that crawls up the tube.</summary>
    public float HumBar { get; init; }

    /// <summary>How fast that band crawls, in tube heights per second.</summary>
    public float HumSpeed { get; init; }

    /// <summary>Horizontal separation between the red and blue channels, in output pixels.</summary>
    public float ChromaBleed { get; init; }

    /// <summary>Horizontal instability, applied to whole scan lines a few times a second.</summary>
    public float Jitter { get; init; }

    /// <summary>Flutter on the beam current. Scaled well down in the shader; 1 is a visible pulse.</summary>
    public float Flicker { get; init; }

    /// <summary>
    /// Whether anything in this presentation moves.
    /// </summary>
    /// <remarks>
    /// The host uses this to decide whether the shelf has to keep requesting frames. A still tube
    /// should not pin a Steam Deck at 60fps redrawing an image that is not changing.
    /// </remarks>
    public bool IsAnimated =>
        IsActive
        && (RollSpeed != 0f || HumBar > 0f || ChromaBleed > 0f || Jitter > 0f || Flicker > 0f);

    /// <summary>
    /// The tube's background, in the same sRGB space Avalonia's brushes use.
    /// </summary>
    /// <remarks>
    /// Full-bleed presentation puts this pass underneath the entire couch screen, so the backdrop
    /// the shelf's Avalonia Borders used to paint has to be curved, scanned and vignetted along
    /// with everything else. Passing it in keeps the accent wash following the focused artwork.
    /// </remarks>
    public Vector3 Backdrop { get; init; }

    /// <summary>Presentation disabled: the renderer takes its cheap resolve-blit path instead.</summary>
    public static CrtPresentation Off { get; } = new()
    {
        Intensity = 0f,
        Backdrop = Vector3.Zero,
    };

    /// <summary>The shipped look. These are the values the effect was tuned to by eye.</summary>
    public static CrtPresentation Default { get; } = new()
    {
        Intensity = 1f,
        Curvature = 0.055f,
        Overscan = 1.08f,
        ChromeOverscan = 0f,
        ScanlineDepth = 0.35f,
        MaskStrength = 0.30f,
        MaskPitch = 3f,
        VirtualLines = 340f,
        Bloom = 0.25f,
        Vignette = 0.22f,
        RollSpeed = 0.012f,
        HumBar = 0.06f,
        HumSpeed = 0.09f,
        ChromaBleed = 1.4f,
        Jitter = 0.06f,
        Flicker = 0.35f,
        Backdrop = Vector3.Zero,
    };

    /// <summary>
    /// Whether the pass is worth running at all. Below this the mix against the untouched image is
    /// invisible and the resolve blit is both cheaper and exactly equivalent.
    /// </summary>
    public bool IsActive => Intensity > 0.001f;
}
