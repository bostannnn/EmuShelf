using System.Numerics;
using EmuShelf.Rendering.Models;

namespace EmuShelf.Rendering.Shells;

/// <summary>
/// The authored shells and the constants that turn each one into a framed hero.
/// </summary>
/// <remarks>
/// Mapping a console to a shell is the app layer's job — this assembly knows nothing about EmuShelf
/// system ids, only about media.
/// </remarks>
public static class MediaShellCatalog
{
    /// <summary>
    /// The GBA cartridge was authored standing upright but facing +X, so it only needs a quarter
    /// turn about Y to bring its label round to +Z.
    /// </summary>
    /// <remarks>
    /// Worth stating why this is a plain quarter turn rather than an axis permutation: a Game Pak
    /// is <em>wider than it is tall</em>, and the model's contact fingers — the exposed edge of its
    /// Motherboard mesh — run the length of its long axis. Standing the cartridge on that long edge
    /// is what puts the pins along the bottom where they belong; permuting the axes instead stands
    /// it on a short edge and leaves the pins running up one side.
    /// </remarks>
    private static readonly Matrix4x4 GbaOrientation = Matrix4x4.CreateRotationY(-MathF.PI / 2f);

    private static readonly Dictionary<MediaShell, MediaShellDefinition> Definitions = new()
    {
        // Authored face-up with its long axis along X and its 20mm thickness along Y. Rotating -90
        // degrees about X stands it on its bottom edge and brings the label face round to +Z.
        [MediaShell.SnesCartridge] = new MediaShellDefinition(
            MediaShell.SnesCartridge,
            "EmuShelf.Rendering.Assets.snes-cartridge.glb",
            Matrix4x4.CreateRotationX(-MathF.PI / 2f),
            MaxTextureSize: 512,
            // The cartridge's own UVs are unusable (they span -93..1.7), and it ships untextured
            // grey plastic, so the label is a projected rectangle sized from the real cartridge:
            // a 78x62mm label on a 134x84mm face, sitting a little above centre.
            CoverPanel: new ArtPanel(ArtFace.Front, -0.64f, 0.64f, -0.70f, 0.84f),
            ExtraPanels: [],
            PanelRoughness: 0.42f,
            // A portrait box scan cropped to the landscape label beats the same scan squashed.
            ArtFit: ArtFit.Cover,
            // The label is a sticker over the cartridge's moulded grooves, not paint on them.
            FlattenPanelNormal: true),

        [MediaShell.GbaCartridge] = new MediaShellDefinition(
            MediaShell.GbaCartridge,
            "EmuShelf.Rendering.Assets.gba-cartridge.glb",
            GbaOrientation,
            MaxTextureSize: 512,
            // Taken from the model's own label mesh, which is a separate flat quad: its bounds
            // measured against the whole cartridge's give this rectangle. It sits above centre
            // because the cartridge's grip ridge eats into the bottom, next to the contacts.
            CoverPanel: new ArtPanel(ArtFace.Front, -0.715f, 0.715f, -0.765f, 0.505f),
            ExtraPanels: [],
            PanelRoughness: 0.38f,
            ArtFit: ArtFit.Cover,
            FlattenPanelNormal: true),

        // Authored upright and close to a real keep case (135 x 190 x 14mm, plus the lip around
        // the lid), so no reorientation is needed.
        [MediaShell.DiscKeepCase] = new MediaShellDefinition(
            MediaShell.DiscKeepCase,
            "EmuShelf.Rendering.Assets.disc-keep-case.glb",
            Matrix4x4.Identity,
            MaxTextureSize: 1024,
            // The printed sleeve runs almost edge to edge under the clear overlay.
            CoverPanel: ArtPanel.Full(ArtFace.Front, inset: 0.02f),
            ExtraPanels:
            [
                ArtPanel.Full(ArtFace.Back, inset: 0.02f),
                ArtPanel.Full(ArtFace.Spine, inset: 0.02f),
            ],
            PanelRoughness: 0.13f,
            // The sleeve and the box scan are the same shape by definition.
            ArtFit: ArtFit.Stretch,
            // The clear cover's curve is what sells it as a case; keep the geometry's own normal.
            FlattenPanelNormal: false),
    };

    public static MediaShellDefinition Definition(MediaShell shell) => Definitions[shell];

    public static IEnumerable<MediaShell> All => Definitions.Keys;

    /// <summary>Loads a shell's model out of this assembly's embedded resources.</summary>
    public static ModelAsset Load(MediaShell shell)
    {
        var definition = Definition(shell);
        using var stream = typeof(MediaShellCatalog).Assembly
            .GetManifestResourceStream(definition.ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded shell model '{definition.ResourceName}' is missing. "
                + $"Known resources: {string.Join(", ", ResourceNames())}");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return GlbLoader.Load(buffer.ToArray(), definition.Orientation, definition.MaxTextureSize);
    }

    private static IEnumerable<string> ResourceNames() =>
        typeof(MediaShellCatalog).Assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Resolves an <see cref="ArtPanel"/> against a loaded model's real bounds, giving the plane the
    /// shader projects artwork onto: an origin corner plus the two edge vectors that span it.
    /// </summary>
    public static ArtPanelPlacement Place(ArtPanel panel, ModelAsset model)
    {
        var half = model.Size * 0.5f;
        var centre = (model.BoundsMin + model.BoundsMax) * 0.5f;

        var normal = FaceNormal(panel.Face);
        // u runs along the panel's own left-to-right, which is the direction a viewer standing in
        // front of that face would call right: right = forward x up, with forward being -normal.
        // Getting this from the face rather than hard-coding an axis per case is what keeps the
        // back of a case from coming out mirrored.
        var vAxis = Vector3.UnitY;
        var uAxis = Vector3.Cross(-normal, vAxis);

        // Extents measured along the panel's own axes, so the same expression serves all faces.
        var (uMin, uMax) = Span(panel.MinU, panel.MaxU, uAxis, centre, half);
        var (vMin, vMax) = Span(panel.MinV, panel.MaxV, vAxis, centre, half);

        // The face sits at the model's far edge along the normal.
        var planeOffset = Vector3.Dot(centre, normal) + MathF.Abs(Vector3.Dot(half, normal));

        return new ArtPanelPlacement(
            Origin: (uAxis * uMin) + (vAxis * vMin) + (normal * planeOffset),
            UEdge: uAxis * (uMax - uMin),
            VEdge: vAxis * (vMax - vMin),
            Normal: normal);
    }

    private static (float Min, float Max) Span(
        float min, float max, Vector3 axis, Vector3 centre, Vector3 half)
    {
        var axisCentre = Vector3.Dot(centre, axis);
        var axisHalf = MathF.Abs(Vector3.Dot(half, axis));
        return (axisCentre + (min * axisHalf), axisCentre + (max * axisHalf));
    }

    private static Vector3 FaceNormal(ArtFace face) => face switch
    {
        ArtFace.Front => Vector3.UnitZ,
        ArtFace.Back => -Vector3.UnitZ,
        // A keep case's printed spine is on the hinge side, which canonical space puts at -X.
        ArtFace.Spine => -Vector3.UnitX,
        _ => throw new ArgumentOutOfRangeException(nameof(face), face, "Unknown art face."),
    };
}

/// <summary>
/// An <see cref="ArtPanel"/> resolved to object-space geometry: artwork covers
/// <c>Origin + u*UEdge + v*VEdge</c> for u and v in 0..1, on the face pointing along
/// <see cref="Normal"/>.
/// </summary>
public readonly record struct ArtPanelPlacement(
    Vector3 Origin,
    Vector3 UEdge,
    Vector3 VEdge,
    Vector3 Normal);
