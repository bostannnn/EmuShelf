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

    /// <summary>
    /// How far the keep case's printed sleeve may follow the shell away from a face, in canonical
    /// object units. One millimetre on a case that stands 190mm tall.
    /// </summary>
    /// <remarks>
    /// This is the knob for "the cover art is bleeding round the edge". Measured off the mesh: the
    /// front plate is flat or gently domed out to 0.94 of its half-width, where it has fallen 0.53mm
    /// behind the front plane, and the rim then turns away hard — 1.23mm at 0.965 and 2.04mm at
    /// 0.991. A millimetre keeps the whole plate, including the clear cover's intentional bulge, and
    /// stops at the fillet.
    ///
    /// It has to be a depth rather than a tighter facing threshold. The source geometry is a cube
    /// scaled 13.5 x 19.0 x 1.4, so the inverse transpose that carries its normals into canonical
    /// space flattens every rim normal toward the face: the shallowest thing the front panel was
    /// painting sat at 0.61 against a guard that rejects below 0.5, and 14% of the painted area was
    /// behind the front plane — as deep as 9.7mm on a 13.7mm case, which is nearly the back.
    ///
    /// And it has to override <see cref="MediaShellDefinition.PanelDepthFraction"/> rather than
    /// retune it. That figure answers a different question — how far in is definitely the inside of
    /// the shell — and at 0.40 of this case's 13.7mm thickness it allows 5.5mm, which the entire
    /// rim sits comfortably within. Both bounds are wanted: the shell's keeps print off any
    /// interior, this one keeps it off the fillet.
    /// </remarks>
    private const float KeepCaseSleeveDepth = 1f / 190f;

    private static readonly Dictionary<MediaShell, MediaShellDefinition> Definitions = new()
    {
        [MediaShell.CoverCard] = new MediaShellDefinition(
            MediaShell.CoverCard,
            ResourceName: string.Empty,
            Matrix4x4.Identity,
            MaxTextureSize: 1,
            // The card is generated at the cover's own aspect, so a stretch is the identity here.
            CoverPanel: ArtPanel.Full(ArtFace.Front, inset: 0.015f, fit: ArtFit.Stretch),
            ExtraPanels: [],
            PanelRoughness: 0.48f,
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
            // A portrait box scan cropped to the landscape label beats the same scan squashed.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.765f, 0.765f, 0.01f, 0.93f, CornerRadius: 0.075f,
                ArtFit: ArtFit.Cover),
            ExtraPanels: [],
            PanelRoughness: 0.38f,
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
            // rectangle guessed against the moulding. It sits right of centre.
            // An NES label folds like a Mega Drive one, and this plate is modelled with the fold:
            // 57.5 x 90.7mm on the face and a 7.3mm strip over the top, against a published
            // 55 x 90..91mm plus 7.19mm. MaxV is the crease rather than the shell's own top edge,
            // which is where the two shells differ — a Mega Drive sheet runs right to the top of
            // its cartridge, and this one stops 0.4mm short of it. 0.994 is the last of the bend
            // the front face can still claim: past it the surface has turned more than 45 degrees
            // and belongs to the folded strip. It was 0.985, and that 0.58mm shortfall was a pale
            // hairline along the crease.
            // TopWrap is 0.0796 rather than the sheet's own 7.19/97.5, because the strip is laid
            // from the shell's front plane and this plate begins 0.3mm behind it. Under-reaching
            // leaves blank plate at the fold's far edge, which is the whole defect this fixes.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.21f, 0.735f, -0.35f, 0.994f, CornerRadius: 0.03f,
                ArtFit: ArtFit.Cover, TopWrap: 0.0796f),
            ExtraPanels: [],
            PanelRoughness: 0.42f,
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
            // atlas dump. What the panel is allowed to be is therefore capped by that mask: it can
            // be smaller, which leaves flat plastic-coloured fill showing as plastic, but it cannot
            // be larger without exposing Sonic 2.
            // Measured from the printed label rather than from the model: a Mega Drive label is a
            // 75 x 68mm sheet on a 109 x 70mm cartridge, of which the top 7.7mm folds over the top
            // edge. That fixes all four numbers — 75/109 of the width, hard against the shell's own
            // top edge because the sheet runs over it, and 60.3 of 70mm down the face, leaving the
            // bare band at the bottom that the moulded grid sits in. The earlier rectangle was a
            // seventh too wide and stopped short at both ends, which read as a sticker applied by
            // eye on a cartridge whose real label is placed to a millimetre.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.688f, 0.688f, -0.723f, 1f,
                CornerRadius: 0.02f, ArtFit: ArtFit.Cover, TopWrap: 0.113f),
            ExtraPanels: [],
            PanelRoughness: 0.40f,
            FlattenPanelNormal: true),

        // A blank DS card template, which replaced satchii_'s four-card model. This one is authored
        // as a cartridge to put artwork on rather than as a copy of one particular game: the label
        // is its own quad on its own material and texture, and the shell moulds the NINTENDO DS band
        // above it, so EmuShelf's artwork lands where a real label's artwork does instead of over
        // the whole face. It is authored lying flat with the label facing +Y, so a quarter turn
        // about X stands it up and brings that face round to +Z.
        [MediaShell.DsCard] = new MediaShellDefinition(
            MediaShell.DsCard,
            "EmuShelf.Rendering.Assets.ds-card.glb",
            Matrix4x4.CreateRotationX(MathF.PI / 2f),
            MaxTextureSize: 1024,
            // The artwork area is the shell's moulded recess, which this model carries a dedicated
            // quad for on the "presetNdsiCartridgeFront4" material. Measured off a straight-on
            // render of the prepared asset rather than off that quad's bounds: the quad's world-space
            // AABB is larger than the face it presents, so reading the bounds alone put the panel
            // 0.08 past the recess on the right and 0.12 below it, which painted artwork onto the
            // moulding. What is rendered is what can be checked.
            // The top stops just under the moulded NINTENDO DS band, which is separate geometry — a
            // panel taken to the recess's own top edge paints over the bottom of the branding.
            // The chamfer is real: a DS label is cut diagonally at the bottom left, and this shell
            // moulds the cut into the recess while the quad stays rectangular, so the panel still
            // has to describe it. Traced off two photographs, one blank card and one retail cart, it
            // runs 0.080 of the full label height both times — and this panel is the label minus the
            // branding band, 0.825 of it, so the same chamfer is 0.097 here.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.805f, 0.805f, -0.712f, 0.605f,
                CornerRadius: 0.04f, CutCorner: 0.097f, ArtFit: ArtFit.Cover),
            ExtraPanels: [],
            PanelRoughness: 0.44f,
            FlattenPanelNormal: true,
            // This asset's plastic is sRGB ~57 (linear 0.041) against a real DS card's charcoal of
            // nearer sRGB 75, so it needs a fraction of the correction the previous model did — that
            // one was authored at sRGB ~31 and needed 3.2 to reach the same place. Tuned against a
            // straight-on render: the shell frame averages sRGB 89 to the photograph's 90.
            BodyAlbedoScale: 1.95f),

        // thegraphicsgeek's Game Pak, which replaced a smaller-textured shell that had no source
        // in models/ and so could not be regenerated or corrected. It is authored upright and
        // already facing +Z, so it needs no reorientation — and it moulds "GAME BOY ADVANCE SP"
        // across the shell, which is what identifies it as a GBA cartridge rather than the Game Boy
        // one its folder name suggests.
        [MediaShell.GbaCartridge] = new MediaShellDefinition(
            MediaShell.GbaCartridge,
            "EmuShelf.Rendering.Assets.gba-cartridge.glb",
            Matrix4x4.Identity,
            MaxTextureSize: 1024,
            // The label is a clean, isolated island in this atlas, well clear of both the shell and
            // the exposed board, so the masked rectangle could be drawn generously without risking
            // the moulding around it.
            // Measured off a straight-on render against the moulded well, which is asymmetric: the
            // cartridge's top lip eats into it, so the label sits lower than centre. The first pass
            // was inset well inside the recess on every side and read as a label applied by eye.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.70f, 0.70f, -0.78f, 0.50f, CornerRadius: 0.06f,
                ArtFit: ArtFit.Cover),
            ExtraPanels: [],
            PanelRoughness: 0.38f,
            FlattenPanelNormal: true,
            // This is the shell that made the depth allowance necessary. Its board and the inside
            // of its back wall face the player through the pin opening, 0.75 of the cartridge's
            // depth behind the label, and inside the label rectangle: printed, they put a band of
            // cover art straight across the contacts. Its own label recess is the deepest of any
            // shell at 0.30, so the default allowance is what fits it and nothing further in.
            PanelDepthFraction: 0.40f),

        // Bob's Game Boy cartridge, authored upright and already facing +Z, so like the GBA shell it
        // needs no reorientation. It is a DMG cartridge rather than a Game Boy Color one — grey
        // plastic, and it wears a Super Mario Land 2 label — but the two share one 57 x 65 x 8mm
        // shell, and "gbc" is the only system id EmuShelf gives the whole Game Boy line.
        [MediaShell.GbcCartridge] = new MediaShellDefinition(
            MediaShell.GbcCartridge,
            "EmuShelf.Rendering.Assets.gbc-cartridge.glb",
            Matrix4x4.Identity,
            MaxTextureSize: 1024,
            // Not eyeballed against a render, unlike the shells before it: the masked label's UV
            // rectangle was projected back through the front face's triangles into object space, so
            // this panel is the removed sticker's own footprint. That is available here and was not
            // for SNES or GBA because this model's label is a flat, isolated island on a face whose
            // UVs are an undistorted plan of it. It lands where a Game Boy label does — nearly the
            // full width, sitting low enough to leave the moulded ridges above it clear.
            // No TopWrap: unlike the Mega Drive and NES labels, a Game Boy sticker stops at the
            // recess and does not fold over the cartridge's top edge — the moulded "Nintendo GAME
            // BOY" band occupies exactly the strip it would fold onto.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.765f, 0.749f, -0.746f, 0.433f, CornerRadius: 0.045f,
                ArtFit: ArtFit.Cover),
            ExtraPanels: [],
            // A Game Boy label is printed paper under no overlay, so it stays matte — nearer the
            // DS card's 0.44 than the SNES decal's 0.38.
            PanelRoughness: 0.50f,
            FlattenPanelNormal: true,
            // This shell needs no tighter allowance than the default. Its label recess is shallow,
            // and nothing faces the player from behind the panel rectangle the way the GBA's board
            // does through its pin opening — the Game Boy's contacts sit below the label, not
            // behind it.
            PanelDepthFraction: 0.40f,
            // The asset's roughness map medians 0.392, which renders as showroom-fresh injection
            // moulding under this studio's key. Real cartridge ABS sits nearer 0.55-0.70, and 1.7
            // lands the median at 0.67.
            BodyRoughnessScale: 1.70f,
            // Measured off a render, the same way the SNES figure was: the body arrived at sRGB
            // ~117 where a real DMG shell is 150-165. 1.8 was right until the ambient came down
            // below; 2.05 restores it to ~143. Read this one together with AmbientIntensity —
            // moving either alone moves the apparent plastic colour.
            BodyAlbedoScale: 2.05f,
            DielectricReflectance: 0.028f,
            // This shell is only 510 triangles, so unlike the other cartridges almost none of its
            // detail is in the mesh — the moulded "Nintendo GAME BOY" band, the grip ridges and the
            // label recess all live in a 1024px normal map, which carries real high-frequency grain
            // (6.8/255 against a base-colour map that is flat to within 0.4). Leaving the normal and
            // cavity terms at their defaults is what made it read as a featureless grey slab: the
            // geometry has nothing to catch the key with, so the map has to do the work the mesh
            // does elsewhere. This is the opposite correction to the SNES shell, whose scan had too
            // much normal noise and needed 0.72.
            //
            // The low fill is the other half of the same fix. Roughness alone barely touched the
            // plastic look, because what read as gloss was an even ambient sheen across a perfectly
            // smooth surface rather than a specular highlight — dropping the fill and letting the
            // key and cavity describe the moulding is what actually removed it.
            AmbientIntensity: 0.60f,
            ShadowFillOcclusion: 0.66f,
            CavityStrength: 0.42f,
            NormalStrength: 2.40f),

        // xqspx's PS1 case, and the one shell whose source needed geometry surgery rather than
        // texture work. The download is a jewel case with its disc lying beside it, and the case is
        // posed for a product shot with the lid standing 9.2 degrees open — 29mm thick against a
        // real case's 10mm. ModelPrep drops the disc and swings the lid shut, which is why this
        // loads at 0.062 D/H rather than 0.234. Authored lying flat and facing away, so it needs a
        // quarter turn up and a half turn round.
        [MediaShell.JewelCase] = new MediaShellDefinition(
            MediaShell.JewelCase,
            "EmuShelf.Rendering.Assets.jewel-case.glb",
            // Authored upright and already facing +Z once its lid is shut, so no reorientation.
            Matrix4x4.Identity,
            MaxTextureSize: 1024,
            // Measured off the insert's own UV island and projected back through the lid's
            // triangles, the way the Game Boy label was. It stops well short of the hinge side
            // because a PS1 front insert does — the strip beside it is the printed banner and the
            // moulded hinge, and covering them is what made the first attempts read as a poster in
            // a frame rather than a case.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.493f, 0.942f, -0.957f, 0.957f, ArtFit: ArtFit.Cover),
            ExtraPanels:
            [
                ArtPanel.Full(ArtFace.Spine, inset: 0.06f, fit: ArtFit.Cover),
            ],
            // The insert sits under a clear polystyrene lid, so it is the glossiest printed surface
            // of any shell here.
            PanelRoughness: 0.16f,
            // No Back panel: this model's back inlay is the tray's interior seen through it, not a
            // face at the shell's -Z bound, so a back projection paints the outside of the tray.
            // The tray already carries the author's own inlay, which is licensed to us.
            FlattenPanelNormal: false,
            BodyRoughnessScale: 1.0f,
            BodyAlbedoScale: 1.0f),

        [MediaShell.DiscKeepCase] = new MediaShellDefinition(
            MediaShell.DiscKeepCase,
            "EmuShelf.Rendering.Assets.disc-keep-case.glb",
            Matrix4x4.Identity,
            MaxTextureSize: 1024,
            // The printed sleeve runs almost edge to edge under the clear overlay. The rectangle
            // alone cannot say where it stops, though: it is measured against the bounding box,
            // and 2% in from that is still on the rounded rim. KeepCaseSleeveDepth does the rest.
            // The sleeve and the box scan are the same shape by definition, so front and back
            // stretch. The spine is a 14mm strip and only shares that fit because nothing scrapes
            // spine art yet — it wears the platform tint, which has no shape to distort.
            CoverPanel: ArtPanel.Full(
                ArtFace.Front, inset: 0.02f,
                fit: ArtFit.Stretch, maxSurfaceDepth: KeepCaseSleeveDepth),
            ExtraPanels:
            [
                ArtPanel.Full(
                    ArtFace.Back, inset: 0.02f,
                    fit: ArtFit.Stretch, maxSurfaceDepth: KeepCaseSleeveDepth),
                ArtPanel.Full(
                    ArtFace.Spine, inset: 0.02f,
                    fit: ArtFit.Stretch, maxSurfaceDepth: KeepCaseSleeveDepth),
            ],
            PanelRoughness: 0.13f,
            // The clear cover's curve is what sells it as a case; keep the geometry's own normal.
            FlattenPanelNormal: false),
    };

    public static MediaShellDefinition Definition(MediaShell shell) => Definitions[shell];

    /// <summary>
    /// Width over height of a shell's printed cover sheet, once its asset is loaded, or null
    /// before then.
    /// </summary>
    /// <remarks>
    /// Anything drawn to fill that panel has to be drawn at this shape. A placeholder authored at
    /// one shell's proportions and pasted onto another is cropped by <see cref="ArtFit.Cover"/>,
    /// which is how a portrait NES label ended up showing "TWORK MI".
    /// </remarks>
    public static float? TryGetPanelAspect(MediaShell shell) =>
        TryGetPrepared(shell, out var asset)
            ? TrySheetAspect(Definition(shell).CoverPanel, asset)
            : null;

    /// <summary>
    /// Width over height of the whole printed sheet a panel carries, folded strip included.
    /// </summary>
    /// <remarks>
    /// A folding label is fitted to the sheet, not to the face: cropping the artwork to the front
    /// panel alone and then folding part of it away would lose the top of the picture twice over.
    /// </remarks>
    public static float? TrySheetAspect(ArtPanel panel, ModelAsset model)
    {
        var placement = Place(panel, model);
        var height = placement.VEdge.Length() / MathF.Max(1f - panel.TopWrap, 1e-3f);
        return height <= 1e-6f ? null : placement.UEdge.Length() / height;
    }

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
        // A top face has no up, so its second axis runs away from the viewer instead: v = 0 at the
        // front edge, which is where a folded label's strip continues from.
        var vAxis = panel.Face == ArtFace.Top ? -Vector3.UnitZ : Vector3.UnitY;
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

    /// <summary>
    /// The strip a folded label lays across the shell's top face, or null where it does not fold.
    /// </summary>
    /// <remarks>
    /// Derived rather than authored, because the fold has to satisfy two things at once. It starts
    /// exactly at the front edge, so the print crosses the corner without a seam; and its length
    /// comes from the front panel's height and the fold fraction — the printed sheet's own scale —
    /// rather than from a fraction of this model's depth. That distinction matters here: the Mega
    /// Drive asset is about 12mm deep where a real cartridge is 17mm, so a fold sized against the
    /// model would print the title strip smaller than the label it belongs to. Clamped to the
    /// shell's depth so a large fraction cannot run the strip off the back edge.
    /// </remarks>
    public static ArtPanel? TryWrapPanel(ArtPanel panel, ModelAsset model)
    {
        if (panel.TopWrap <= 0f)
        {
            return null;
        }

        // Loud rather than silent: the strip runs backwards from the front edge, so a fold asked
        // for on any other face would be laid down in the wrong place and would quietly eat the
        // front label's share of the sheet as well.
        if (panel.Face != ArtFace.Front || panel.TopWrap >= 1f)
        {
            throw new ArgumentException(
                $"A label can only fold from the front face and by less than all of itself; "
                + $"this one folds {panel.TopWrap:P0} of a {panel.Face} panel.",
                nameof(panel));
        }

        var frontHeight = Place(panel, model).VEdge.Length();
        var foldLength = frontHeight * panel.TopWrap / (1f - panel.TopWrap);
        var depth = MathF.Max(model.Size.Z, 1e-6f);
        var fraction = MathF.Min(foldLength / depth, 1f);

        // Copied from the label rather than built fresh, so anything that describes how the sheet
        // is printed — its fit, its depth allowance — carries across the crease instead of
        // silently reverting to a default on one half of it. v = -1 is the top face's front edge;
        // see Place. Corners stay square: the label's own rounded ones are on the front panel, and
        // the fold line itself is a straight crease.
        return panel with
        {
            Face = ArtFace.Top,
            MinV = -1f,
            MaxV = -1f + (2f * fraction),
            CornerRadius = 0f,
            CutCorner = 0f,
            TopWrap = 0f,
        };
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
        ArtFace.Top => Vector3.UnitY,
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
