using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<MediaShell, Lazy<Task<ModelAsset>>> PreparedAssets = [];

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
        [MediaShell.CoverCard] = new MediaShellDefinition(
            MediaShell.CoverCard,
            ResourceName: string.Empty,
            Matrix4x4.Identity,
            MaxTextureSize: 1,
            CoverPanel: ArtPanel.Full(ArtFace.Front, inset: 0.015f),
            ExtraPanels: [],
            PanelRoughness: 0.48f,
            ArtFit: ArtFit.Stretch,
            FlattenPanelNormal: true),

        // SomeKevin's PAL/Super Famicom shell is authored upright with its label toward -Z. A half
        // turn about Y brings that face to canonical +Z while retaining its correct vertical axis.
        [MediaShell.SnesCartridge] = new MediaShellDefinition(
            MediaShell.SnesCartridge,
            "EmuShelf.Rendering.Assets.snes-cartridge.glb",
            Matrix4x4.CreateRotationY(MathF.PI),
            MaxTextureSize: 1024,
            // The source's fixed placeholder label is neutralized in the runtime derivative. Game
            // art is projected onto the real label area while the surrounding moulding, screws,
            // contacts and their authored PBR maps remain visible.
            // Set against a rendered frame, because the recess cannot be derived from the asset:
            // the shell's front is one near-flat surface with almost no vertices, and its label UV
            // island is shared with geometry elsewhere on the body. The placeholder label was what
            // made the fit legible at all — a flat accent tint has no edge to compare against the
            // moulding. The overhang was almost entirely horizontal, so the authored vertical
            // extent is nearly intact while the width comes in from 0.80. Confirmed on hardware.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.765f, 0.765f, 0.01f, 0.93f, CornerRadius: 0.075f),
            ExtraPanels: [],
            PanelRoughness: 0.38f,
            // A portrait box scan cropped to the landscape label beats the same scan squashed.
            ArtFit: ArtFit.Cover,
            // The decal follows the body surface but hides its moulded shading normal, so it reads
            // as an applied label without a floating gap.
            FlattenPanelNormal: true,
            // The downloaded scan was authored for a brighter viewer and otherwise reads like a
            // glossy miniature under EmuShelf's close product-lighting camera.
            BodyRoughnessScale: 1.16f,
            // Measured, not guessed: 89.5% of this asset's base-colour map sits at sRGB ~107, i.e.
            // linear 0.144. A PAL SNES shell is light grey, nearer sRGB 165 (linear ~0.36). The
            // cartridge was therefore dark before a single light touched it, which is why it read
            // as charcoal under a studio calibrated to keep its plastic grey. 2.4 lands it at about
            // sRGB 160. Turn this knob, not the exposure — exposure would take the labels, the
            // keep cases and the cover cards with it.
            BodyAlbedoScale: 2.4f,
            DielectricReflectance: 0.033f,
            // A lower studio fill lets the key describe the form at couch distance. Actual depth
            // visibility and the authored normal map, rather than a screen-space fake, provide the
            // stronger occlusion in rails, wells and the lower slot.
            AmbientIntensity: 0.70f,
            ShadowFillOcclusion: 0.58f,
            CavityStrength: 0.34f,
            // The scan's shallow normal variation forms broad swirls under a hard key. Reduce its
            // amplitude; real mesh bevels and the new depth map carry the cartridge's large form.
            NormalStrength: 0.72f),

        // dark_igorek's NES shell is authored lying on its side with the front toward +X, so a
        // quarter turn about Y stands it up: width comes from Z, height from Y, depth from X. The
        // orientation was settled by rendering it rather than by reading UV winding, which
        // disagreed with the vertex normals about which way was up.
        [MediaShell.NesCartridge] = new MediaShellDefinition(
            MediaShell.NesCartridge,
            "EmuShelf.Rendering.Assets.nes-cartridge.glb",
            Matrix4x4.CreateRotationY(-MathF.PI / 2f),
            MaxTextureSize: 1024,
            // Measured from the model's own label mesh — it keeps the label on a separate material
            // named "sticker", so the artwork slot is the plate's real bounds rather than a
            // rectangle guessed against the moulding. It sits right of centre and reaches the top
            // edge because the authored plate does; the shader's facing test keeps art off the part
            // that wraps over the top.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.21f, 0.735f, -0.35f, 0.985f, CornerRadius: 0.03f),
            ExtraPanels: [],
            PanelRoughness: 0.42f,
            ArtFit: ArtFit.Cover,
            FlattenPanelNormal: true,
            BodyRoughnessScale: 1.0f,
            BodyAlbedoScale: 1.0f),

        // Naser's Mega Drive shell is authored upright with its label already toward +Z, so it is
        // the one shell needing no reorientation. Rolling it 180 degrees puts the MEGA DRIVE band
        // at the top where a European label carries it, but turns Sonic upside down — the artwork's
        // own orientation is the test that settles it, and identity is what keeps that upright.
        [MediaShell.MegaDriveCartridge] = new MediaShellDefinition(
            MediaShell.MegaDriveCartridge,
            "EmuShelf.Rendering.Assets.megadrive-cartridge.glb",
            Matrix4x4.Identity,
            MaxTextureSize: 1024,
            // This shell has one material and one atlas, so unlike NES its label could not be
            // removed by flattening a material — it needed the rectangle treatment, read off the
            // atlas dump. The label covers nearly the whole face, which is why this panel is close
            // to a full-face inset rather than the small recess a SNES cartridge has.
            CoverPanel: new ArtPanel(ArtFace.Front, -0.88f, 0.88f, -0.86f, 0.86f, CornerRadius: 0.02f),
            ExtraPanels: [],
            PanelRoughness: 0.40f,
            ArtFit: ArtFit.Cover,
            FlattenPanelNormal: true),

        // satchii_'s DS card, reduced to one instance: the download is four cards laid out in a
        // row by node matrices, and loading it whole draws four cartridges. Its node transforms
        // already stand the card upright — the raw accessor bounds suggest otherwise and that
        // misread cost a wrong rotation — so it needs only a half turn to bring the label side
        // round from -Z, where the contact pins are not.
        [MediaShell.DsCard] = new MediaShellDefinition(
            MediaShell.DsCard,
            "EmuShelf.Rendering.Assets.ds-card.glb",
            Matrix4x4.CreateRotationY(MathF.PI),
            MaxTextureSize: 1024,
            // A DS label covers nearly the whole face. Unusually for these shells the geometry
            // carries no label of its own: the source's Super Mario 64 artwork sits in the atlas but
            // no triangle samples it, so the card renders blank and the panel has a clean surface.
            CoverPanel: ArtPanel.Full(ArtFace.Front, inset: 0.06f) with { CornerRadius = 0.06f },
            ExtraPanels: [],
            PanelRoughness: 0.44f,
            ArtFit: ArtFit.Cover,
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

    /// <summary>
    /// Starts decoding one shell away from the UI/GL thread. The immutable decoded asset is shared
    /// by every renderer/context; only the context-bound mesh and texture upload remains per renderer.
    /// </summary>
    public static Task<ModelAsset> PrepareAsync(MediaShell shell) =>
        PreparedAssets.GetOrAdd(
            shell,
            static key => new Lazy<Task<ModelAsset>>(
                () => Task.Run(() => LoadUncached(key)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    /// <summary>Returns an asset only when background decoding has already completed successfully.</summary>
    public static bool TryGetPrepared(MediaShell shell, out ModelAsset asset)
    {
        asset = null!;
        if (!PreparedAssets.TryGetValue(shell, out var lazy) || !lazy.IsValueCreated)
        {
            return false;
        }

        var task = lazy.Value;
        if (!task.IsCompletedSuccessfully)
        {
            return false;
        }

        asset = task.Result;
        return true;
    }

    /// <summary>
    /// Loads a shell synchronously for tools and tests. Runtime controls call <see cref="PrepareAsync"/>
    /// first so model parsing and image decode do not land in a navigation frame.
    /// </summary>
    public static ModelAsset Load(MediaShell shell) => PrepareAsync(shell).GetAwaiter().GetResult();

    private static ModelAsset LoadUncached(MediaShell shell)
    {
        if (shell == MediaShell.CoverCard)
        {
            return CreateCoverCard();
        }

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
    /// Creates the unsupported-system fallback as real scene geometry rather than an Avalonia tile.
    /// Its width is the library's common portrait ratio; a profile can non-uniformly scale it to a
    /// platform's actual cover ratio while retaining one shared mesh.
    /// </summary>
    private static ModelAsset CreateCoverCard()
    {
        const float halfWidth = 0.354f;
        const float halfHeight = 0.5f;
        const float halfDepth = 0.015f;

        var vertices = new List<float>(24 * MeshGeometry.FloatsPerVertex);
        var indices = new List<uint>(36);

        void AddFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
        {
            var first = (uint)(vertices.Count / MeshGeometry.FloatsPerVertex);
            AddVertex(a, normal, 0f, 0f);
            AddVertex(b, normal, 1f, 0f);
            AddVertex(c, normal, 1f, 1f);
            AddVertex(d, normal, 0f, 1f);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        void AddVertex(Vector3 position, Vector3 normal, float u, float v)
        {
            vertices.AddRange(
            [
                position.X, position.Y, position.Z,
                normal.X, normal.Y, normal.Z,
                u, v,
            ]);
        }

        // Winding is counter-clockwise as each face is viewed from outside.
        AddFace(
            new(-halfWidth, -halfHeight, halfDepth), new(halfWidth, -halfHeight, halfDepth),
            new(halfWidth, halfHeight, halfDepth), new(-halfWidth, halfHeight, halfDepth),
            Vector3.UnitZ);
        AddFace(
            new(halfWidth, -halfHeight, -halfDepth), new(-halfWidth, -halfHeight, -halfDepth),
            new(-halfWidth, halfHeight, -halfDepth), new(halfWidth, halfHeight, -halfDepth),
            -Vector3.UnitZ);
        AddFace(
            new(-halfWidth, -halfHeight, -halfDepth), new(-halfWidth, -halfHeight, halfDepth),
            new(-halfWidth, halfHeight, halfDepth), new(-halfWidth, halfHeight, -halfDepth),
            -Vector3.UnitX);
        AddFace(
            new(halfWidth, -halfHeight, halfDepth), new(halfWidth, -halfHeight, -halfDepth),
            new(halfWidth, halfHeight, -halfDepth), new(halfWidth, halfHeight, halfDepth),
            Vector3.UnitX);
        AddFace(
            new(-halfWidth, halfHeight, halfDepth), new(halfWidth, halfHeight, halfDepth),
            new(halfWidth, halfHeight, -halfDepth), new(-halfWidth, halfHeight, -halfDepth),
            Vector3.UnitY);
        AddFace(
            new(-halfWidth, -halfHeight, -halfDepth), new(halfWidth, -halfHeight, -halfDepth),
            new(halfWidth, -halfHeight, halfDepth), new(-halfWidth, -halfHeight, halfDepth),
            -Vector3.UnitY);

        return new ModelAsset
        {
            Meshes =
            [
                new MeshGeometry
                {
                    Vertices = vertices.ToArray(),
                    Indices = indices.ToArray(),
                    MaterialIndex = 0,
                },
            ],
            Materials =
            [
                new ModelMaterial
                {
                    Name = "cover-card",
                    BaseColorFactor = new Vector4(0.18f, 0.18f, 0.20f, 1f),
                    MetallicFactor = 0f,
                    RoughnessFactor = 0.62f,
                },
            ],
            Textures = [],
            BoundsMin = new Vector3(-halfWidth, -halfHeight, -halfDepth),
            BoundsMax = new Vector3(halfWidth, halfHeight, halfDepth),
        };
    }

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
