using System.Numerics;

namespace EmuShelf.Rendering.Shells;

/// <summary>The physical media EmuShelf can render as a rotatable 3D hero.</summary>
/// <remarks>
/// One entry per authored shell, not per console: a PS2, GameCube, Wii and PS3 game all shipped in
/// the same 135x190x14mm keep case, so they share <see cref="DiscKeepCase"/>. Systems with no shell
/// keep their flat cover — the console-to-medium table lives in the app layer, at
/// <c>EmuShelf.App.Rendering.MediaShellMap</c>.
/// </remarks>
public enum MediaShell
{
    /// <summary>The SNES/Super Famicom ROM cartridge.</summary>
    SnesCartridge,

    /// <summary>The Game Boy Advance ROM cartridge.</summary>
    GbaCartridge,

    /// <summary>The 135x190x14mm DVD keep case used by PS2, PS3, GameCube and Wii releases.</summary>
    DiscKeepCase,
}

/// <summary>
/// Where a game's scraped artwork is pasted onto a shell, as a rectangle on one of its faces.
/// </summary>
/// <remarks>
/// Deliberately expressed against the model's own bounds rather than its UV atlas. Two of the three
/// shells have UV layouts that cannot carry a decal — the SNES cartridge's are degenerate (they run
/// from -93 to 1.7) and the GBA's label is packed rotated into a shared atlas — so the shader
/// projects artwork onto a face in object space instead. That is one code path for every shell, and
/// it stays correct if a model is ever re-exported with different UVs.
/// </remarks>
/// <param name="Face">Which face of the shell the artwork sits on.</param>
/// <param name="MinU">Left edge, as a fraction of the face's half-width (-1 is the far edge).</param>
/// <param name="MaxU">Right edge, in the same units.</param>
/// <param name="MinV">Bottom edge, as a fraction of the shell's half-height.</param>
/// <param name="MaxV">Top edge, in the same units.</param>
public readonly record struct ArtPanel(ArtFace Face, float MinU, float MaxU, float MinV, float MaxV)
{
    /// <summary>A panel covering the whole of a face, inset by <paramref name="inset"/>.</summary>
    public static ArtPanel Full(ArtFace face, float inset = 0f) =>
        new(face, -1f + inset, 1f - inset, -1f + inset, 1f - inset);
}

/// <summary>How artwork whose shape does not match a panel's is fitted to it.</summary>
/// <remarks>
/// This matters because a keep case's sleeve really is the same shape as the box scan the scraper
/// returns, while a cartridge label is landscape and the scan is portrait. Stretching the latter
/// onto the former is the difference between a cartridge and a squashed poster.
/// </remarks>
public enum ArtFit
{
    /// <summary>Fill the panel exactly, distorting if the shapes disagree.</summary>
    Stretch,

    /// <summary>Fill the panel, cropping whatever overflows.</summary>
    Cover,

    /// <summary>Fit inside the panel, leaving the tint visible around it.</summary>
    Contain,
}

/// <summary>Which side of a shell an <see cref="ArtPanel"/> lies on, in canonical space.</summary>
public enum ArtFace
{
    /// <summary>+Z, the face that greets the player.</summary>
    Front,

    /// <summary>-Z.</summary>
    Back,

    /// <summary>-X, the spine of a keep case.</summary>
    Spine,
}

/// <summary>Everything the renderer needs to turn one .glb into a framed, art-bearing hero.</summary>
/// <param name="Shell">Which medium this describes.</param>
/// <param name="ResourceName">Embedded resource name of the .glb inside this assembly.</param>
/// <param name="Orientation">Rotates the authored model into canonical space (Y up, +Z front).
/// The three models were authored lying in three different directions, so each carries its own.</param>
/// <param name="MaxTextureSize">Longest edge the model's own maps are downsampled to at load.</param>
/// <param name="CoverPanel">Where the game's cover art is projected.</param>
/// <param name="ExtraPanels">Further panels — a case's back and spine — painted with the accent
/// tint until the scraper supplies real art for them.</param>
/// <param name="PanelRoughness">Roughness the printed panels take, overriding the shell's own map.
/// A paper sleeve behind a keep case's clear overlay is far glossier than a cartridge's printed
/// label, and that difference is most of what distinguishes the two materials on screen.</param>
/// <param name="ArtFit">How a cover whose shape does not match the panel is fitted.</param>
/// <param name="FlattenPanelNormal">True where printed art should hide the moulding under it.</param>
public sealed record MediaShellDefinition(
    MediaShell Shell,
    string ResourceName,
    Matrix4x4 Orientation,
    int MaxTextureSize,
    ArtPanel CoverPanel,
    IReadOnlyList<ArtPanel> ExtraPanels,
    float PanelRoughness,
    ArtFit ArtFit,
    bool FlattenPanelNormal);
