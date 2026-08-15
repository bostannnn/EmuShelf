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
    /// Undoes the arbitrary scene rotation the compact disc's source bakes into its node graph.
    /// </summary>
    /// <remarks>
    /// A quaternion rather than the Euler triples every other shell uses, because this is not a
    /// choice of pose — it is the exact inverse of one. The export composes a rotation down its node
    /// chain that leaves the disc tumbled on no particular axis, and the loader bakes that into the
    /// vertices: measured as loaded, the disc came out 1.829 wide per unit of height and very nearly
    /// as deep, which is a disc standing on a corner. Turning three angle dials until a round object
    /// looks round is a search with no way to know it has finished; the composed world rotation is
    /// knowable, and its conjugate lands the disc flat in XY exactly.
    ///
    /// That puts its label on +Z like every other shell's cover, and makes a spin about the disc's
    /// own axis a rotation about Z.
    /// </remarks>
    private static readonly Matrix4x4 DiscOrientation = Matrix4x4.CreateFromQuaternion(
        Quaternion.Normalize(
            new Quaternion(-0.73831944f, 0.35954587f, 0.4302002f, -0.37488526f)));

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

        // satchii_'s DS card, reduced to one instance: the download is four cards laid out in a row
        // by node matrices, and loading it whole draws four cartridges. It replaced littlengvfx's
        // blank template, which was the better *kind* of asset — a dedicated label plate on its own
        // material — and the worse object: its front plate is not flat. The plate is modelled as a
        // separate panel floating inside the frame, and the frame itself waves, so the card read as
        // bent at any pose that showed an edge. This one is a scan of a real card: the face is one
        // plane, the back moulds the Nintendo logo and the contact pins, and no pose shows a warp.
        //
        // Its node transforms already stand the card upright — the raw accessor bounds suggest
        // otherwise, and that misread cost a wrong rotation once — so it needs only a half turn to
        // bring the label side round from -Z, where the contact pins are not.
        [MediaShell.DsCard] = new MediaShellDefinition(
            MediaShell.DsCard,
            "EmuShelf.Rendering.Assets.ds-card.glb",
            Matrix4x4.CreateRotationY(MathF.PI),
            MaxTextureSize: 1024,
            // This model keeps its label on the same atlas and material as its body, so the label
            // goes by masking a rectangle rather than by flattening a material — the fallback, and
            // the technique that fails silently. What makes it safe here is that the mask's fill is
            // the card's own plastic, measured off the atlas, so the ring between the mask and this
            // panel reads as plastic rather than as the paper-grey halo this shell shipped once.
            //
            // The panel is the label's own footprint, and the label is a printed sticker rather than
            // a moulded recess: it carries the NINTENDO DS band at its top, so unlike the template
            // shell there is nothing to leave bare above it. Measured twice and to the same place —
            // off the prepared asset's atlas through the face quad's UV mapping, and off a
            // straight-on render of the asset with its label still on. That comes out 29.0 x 29.9mm
            // on a 33.5 x 35mm card, against a real label's ~29 x 30mm.
            // The chamfer is real: a DS label is cut diagonally at the bottom left so it clears the
            // thumb notch. Traced off two photographs at 0.080 of the label's height, and this
            // model's own label measures 23 of 287 pixels in a straight-on render — 0.080 again.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.833f, 0.833f, -0.808f, 0.905f,
                CornerRadius: 0.05f, CutCorner: 0.080f, ArtFit: ArtFit.Cover),
            ExtraPanels: [],
            // These three are the "it reads as plastic" knobs, and they were turned together because
            // the complaint is one thing seen from two surfaces. The label is the larger half: a DS
            // sticker is matte vinyl, and at the 0.44 the other cartridges use, its specular washed
            // flat across the whole panel and killed the diffuse falloff — measured, the panel's
            // light-to-dark spread went from 14 to 24 sRGB when this was raised, which is the print
            // getting its shading back rather than losing it.
            PanelRoughness: 0.58f,
            FlattenPanelNormal: true,
            // The plastic around the label is authored at sRGB 38 against a real DS card's charcoal
            // of nearer 75, so this needs the larger correction the sRGB ~57 template shell did not —
            // and scaling the authored colour rather than replacing it is what keeps the moulding,
            // the seam and the pin-side step visible. Tuned against a straight-on render: the shell
            // frame's median lands at sRGB 91 to a photograph's 90.
            BodyAlbedoScale: 3.2f,
            // The other half, and the same correction the SNES shell takes at 1.16 and 0.033 for the
            // same reason: a downloaded scan's material was tuned in its author's viewer, not under
            // EmuShelf's close product-lighting camera, where it reads like a glossy miniature.
            BodyRoughnessScale: 1.20f,
            DielectricReflectance: 0.033f),

        // SGLilac's 3DS card, which is a photogrammetric scan of a real one rather than a modelled
        // cartridge: 210 triangles carrying a 2048px scan, where the moulding that matters — the
        // anti-insertion tab on the upper right edge, the pin bay and the moulded serial on the back
        // — is in the normal map rather than in the mesh. That is the right trade for this medium.
        // A DS or 3DS card is a flat plate a fingernail thick; there is no form for a denser mesh to
        // describe, and at shelf size the tab is the whole silhouette difference between the two
        // cards.
        //
        // Authored lying flat with its label toward +Y, so a quarter turn about X stands it up.
        // Unlike the DS card, whose node matrices already stand it upright, this one's really is
        // flat as loaded — checked by loading it, not by reading accessor bounds, which is the
        // mistake that cost the DS shell a wrong rotation.
        [MediaShell.Nintendo3dsCard] = new MediaShellDefinition(
            MediaShell.Nintendo3dsCard,
            "EmuShelf.Rendering.Assets.3ds-card.glb",
            Matrix4x4.CreateRotationX(MathF.PI / 2f),
            MaxTextureSize: 1024,
            // Label and body share one atlas and one material, as on the DS card, so the Rune
            // Factory 4 print goes by a masked rectangle and this panel has to stay inside that
            // mask. Two rectangles rather than one: the scanned card also carries that title's
            // product serial moulded into its back, which the shelf shows whenever a card is turned.
            // The fill is the card's own plastic sampled off the atlas — sRGB 186, which the scan
            // holds flat across the whole body — so the hairline between mask and panel reads as
            // plastic rather than as a halo.
            //
            // Derived rather than eyeballed, which is available here and was not on the DS card: the
            // front face is two large triangles with an affine UV map, so the sticker's own bounds
            // in the atlas — 1125..1931 by 86..947 of 2048 — can be carried straight back into
            // object space. Both triangles agree to three decimals. Taken off the sticker rather
            // than off the masked rectangle around it, which is what leaves the mask strictly larger
            // than the panel on all four sides by about 0.018, a third of a millimetre of the card's
            // own plastic.
            //
            // The result is asymmetric in U — -0.864 against 0.765 — and that is the tab: it adds
            // about 1.3 units on +X to a 30.2-unit card, so the body's centre sits left of the
            // bounding box's, and a panel centred on the box would print off the label.
            //
            // A real 3DS label is chamfered at its bottom left, like a DS one. Measured off the
            // atlas rather than off a photograph: the sticker's left edge steps in over the last 55
            // rows of its 861 and 61 columns horizontally, which are 0.064 and 0.071 of the panel's
            // height once the isotropic UV carries them over. 0.067 is those two averaged.
            //
            // The corner radius is the one figure here that is not measured. This scan squares the
            // sticker's corners — its edge is straight to within six texels — where a real label is
            // gently rounded, so 0.03 is a real card's corner rather than this one's, and small
            // enough that either way it stays inside the mask.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.864f, 0.765f, -0.844f, 0.831f,
                CornerRadius: 0.03f, CutCorner: 0.067f, ArtFit: ArtFit.Cover),
            ExtraPanels: [],
            // A 3DS label is a glossier print than a DS card's matte vinyl sticker, which is why
            // this is the cartridges' usual figure rather than the 0.58 the DS shell needs.
            PanelRoughness: 0.44f,
            FlattenPanelNormal: true,
            // No albedo correction, unlike every other cartridge here: this scan's plastic is
            // already a retail card's white at sRGB 186, so scaling it would blow it out rather than
            // rescue it. The one shell in the catalog whose body colour is simply the object's.
            BodyAlbedoScale: 1f,
            BodyRoughnessScale: 1f),
        // No ClearcoatFactor here, and setting one would do nothing. This is the first shell whose
        // own material carries KHR_materials_clearcoat — the scan declares a coat of 1.0 at
        // roughness 1.0, the lacquer on a card's printed face — and the renderer takes the asset's
        // coat over the definition's wherever the asset has one, which is the rule the jewel case
        // established from the other side. So the card's sheen is the model's to change, not this
        // table's; if it ever needs turning down it comes out of the prep, not out of a knob here.


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

        // sodaraptor's Hypnagogia case, and the one shell whose source needed geometry surgery as
        // well as texture work. The download is a jewel case with its disc lying in the tray, posed
        // for a product shot with the lid standing 25 degrees open — 66mm thick against a real
        // case's 10mm. ModelPrep drops the disc and swings the lid shut, which is why this loads at
        // 0.072 D/H rather than 0.533. Authored upright and already facing +Z once shut, so no
        // reorientation.
        [MediaShell.JewelCase] = new MediaShellDefinition(
            MediaShell.JewelCase,
            "EmuShelf.Rendering.Assets.jewel-case.glb",
            Matrix4x4.Identity,
            MaxTextureSize: 1024,
            // The whole front insert, banner included, because that is what a scraped PS1 cover is:
            // a scan of the 120mm card with the platform's own banner printed down its left. An
            // earlier version stopped at -0.493 to clear the banner the source model paints there,
            // which put the scan's banner beside a second, fictional one — the reason that banner
            // is now flattened out of the asset instead.
            // Measured rather than eyeballed, the way the Game Boy label was: the lid's front face
            // carries a clean linear UV, nx = 2.663u - 1.273, and the print runs from u 0.182 (the
            // plastic seam inboard of the hinge) to u 0.838. Past 0.958 is the lid's own 1.5mm rim.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.789f, 0.958f, -0.957f, 0.957f, ArtFit: ArtFit.Cover),
            // Back before Spine, and the order is not cosmetic: ShelfArtworkFace is the panel index
            // the app uploads each scraped face to, and it fixes Back at 1 and Spine at 2. Declared
            // the other way round, a scraped back inlay lands on the spine and the spine on the
            // back — invisible in the shell preview, which only ever supplies a front cover.
            ExtraPanels:
            [
                // The tray inlay. It was left off on the grounds that this model's back inlay is
                // the tray's interior seen through it rather than a face at the shell's -Z bound,
                // so projecting onto it would paint the outside of the tray. That is what a
                // transparent tray is for — the inlay is behind it — and rendered, the projection
                // lands on the inlay and nowhere else. Without it the back is a blank white card.
                ArtPanel.Full(ArtFace.Back, inset: 0.04f, fit: ArtFit.Cover),
                // The hinge side, which is where a CD case carries its title: the tray inlay's flap
                // shows through it, so canonical -X is right here for the same reason it is on a
                // keep case even though the two hinge opposite ways.
                ArtPanel.Full(ArtFace.Spine, inset: 0.06f, fit: ArtFit.Cover),
            ],
            // The insert sits under a clear polystyrene lid, so it is the glossiest printed surface
            // of any shell here.
            PanelRoughness: 0.16f,
            FlattenPanelNormal: false,
            BodyRoughnessScale: 1.0f,
            BodyAlbedoScale: 1.0f,
            // The only shell with a coat, and the reason the renderer reads one at all. Full
            // strength because the lid is not lacquered plastic but a sheet of clear polystyrene:
            // the insert is genuinely behind glass. 0.06 rather than a mirror because a moulded lid
            // has a faint orange peel to it, and a mirror-flat coat reads as a render, not a case.
            ClearcoatFactor: 1.0f,
            ClearcoatRoughness: 0.06f),

        // sanyabeast's cabinet, and the first shell that is a machine rather than a medium: an
        // arcade game was never something a player took home in a box, so what stands on the shelf
        // is what stood in the arcade. Authored upright with its front toward +X — the buttons are
        // the giveaway, sitting at the far positive end of that axis — so it takes the same quarter
        // turn about Y the NES cartridge does.
        [MediaShell.ArcadeCabinet] = new MediaShellDefinition(
            MediaShell.ArcadeCabinet,
            "EmuShelf.Rendering.Assets.arcade-cabinet.glb",
            Matrix4x4.CreateRotationY(-MathF.PI / 2f),
            // 512 rather than the cartridges' 1024, because this shell has twelve materials and
            // thirty-six maps where they have one and three. The source is 62MB; at 1024 the
            // derivative is over 20MB for an object the shelf draws at a few hundred pixels.
            MaxTextureSize: 512,
            // Scoped to the screen material, and therefore measured against the screen's own mesh
            // rather than the cabinet: 1.0 is the edge of the glass, and the picture runs to it.
            // An inset was tried first, on the reasoning that a picture stops short of a tube's
            // edge — but this mesh is the visible glass only, its surround is the cabinet's own
            // dark bezel, and the unprinted margin came out as a pale grey mat around the artwork.
            // 0.99 rather than 1.0 leaves the mask's antialiased edge somewhere to land.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.99f, 0.99f, -0.99f, 0.99f,
                ArtFit: ArtFit.Cover, Material: "screen"),
            ExtraPanels: [],
            // A CRT behind glass, so glossier than any printed label on the other shells.
            PanelRoughness: 0.18f,
            // The tube is curved, and that curve catching the studio key is what stops the screen
            // reading as a sticker on a flat panel.
            FlattenPanelNormal: false,
            // Half the authored height, settled by rendering three of them. The control panel's
            // surface sits at 0.527 and its buttons reach 0.60, so the window is narrow above and
            // open below: 0.58 saws the joysticks off, and 0.42 keeps a tall empty skirt that reads
            // as a cabinet with its legs cut rather than as a bartop. 0.50 takes the whole control
            // panel plus a shallow apron under it.
            //
            // The floor laid across the cut is the bound going down, and it is not obvious from
            // here — see GlbLoader.CreateCutCap. That floor is the convex hull of the cut, which
            // stays hidden only while the cross-section is roughly convex. It is at 0.50. Lower and
            // the control panel's skirt parts company with the body, at which point the hull bridges
            // the two and a ledge of floor shows between them from a low angle. Anything much under
            // 0.45 needs the cap looked at, not just the silhouette.
            TrimBelowHeightFraction: 0.50f),

        // Authored upright and close to a real keep case (135 x 190 x 14mm, plus the lip around
        // the lid), so no reorientation is needed.
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

        // Diablo's case, authored upright at a real Blu-ray case's 135 x 171.5 x 13mm — the scale is
        // literally those three numbers on its root node — so it needs no reorientation and its
        // profile is a transcription rather than a measurement.
        //
        // The one shell sourced blank. Every other model here arrived wearing a specific game's
        // packaging and had to have it flattened out before the derivative could ship; this one has
        // no textures and two untextured materials, because its author modelled it as an empty case
        // to drop your own cover into. That is why the constants below are heavier than any other
        // shell's: with no maps at all, everything the plastic does under the studio light has to
        // come from these numbers rather than from a scan.
        [MediaShell.BluRayCase] = new MediaShellDefinition(
            MediaShell.BluRayCase,
            "EmuShelf.Rendering.Assets.blu-ray-case.glb",
            Matrix4x4.Identity,
            // Nothing to downsample — the shell ships 148KB against the arcade cabinet's 6.5MB —
            // but the loader takes the figure regardless, and a later re-export with maps should
            // not silently arrive at full size.
            MaxTextureSize: 1024,
            // Scoped to the sleeve's own material, which the keep case above could not do: its
            // sleeve and its body are one mesh, so it has to draw a rectangle against the whole
            // shell and then bound how deep the print may reach. Here the clear film is its own
            // mesh — front sheet, spine wrap and back sheet, and nothing else — so the panel is
            // simply "all of it", measured against the film rather than against the case. That is
            // what the inset of zero means: not a tighter margin than the keep case's 0.02, but a
            // rectangle whose edges are already the print's edges. The film is inset 4mm from the
            // case's top and bottom in the mesh, the way it is on the real object.
            CoverPanel: SleevePanel(ArtFace.Front),
            // Back before Spine — ShelfArtworkFace fixes Back at panel 1 and Spine at 2, and the
            // jewel case above records what swapping them costs.
            ExtraPanels: [SleevePanel(ArtFace.Back), SleevePanel(ArtFace.Spine)],
            // The same paper-under-clear-film surface as the keep case, so the same figure.
            PanelRoughness: 0.13f,
            // The film is gently domed away from the sheet behind it; keep that curve.
            FlattenPanelNormal: false,
            // The source material is a flat 0.824 roughness with no map — matte, near enough to
            // paper. A keep case is moulded polypropylene: not a mirror, but plainly glossier than
            // that. This lands it at 0.34 once the ps3-clear finish has taken its 0.76.
            BodyRoughnessScale: 0.54f,
            // And a flat 0.8 linear base colour, which is nearly white — the author modelled a PS5
            // case, and PS5 cases are white. A PS3 case is clear, and clear plastic rendered
            // opaquely is a mid grey with a bright edge, not a pale slab. Left alone this reads as
            // the wrong console before the blue tint is even applied, which is the whole reason
            // this knob exists: the asset's material was tuned for its author's viewer.
            //
            // Set by measuring the rendered rim rather than by eye, because at the hero pose the
            // body is a few pixels of mostly specular and 0.25 against 0.60 is indistinguishable
            // there — the edge poses and the shelf row are where this is legible at all. On the
            // spine pose the body's lit face lands at sRGB 153 here, against 188 at 0.60 and the
            // PS2 case's black plastic at 54. That is the placement being aimed at, and the shelf
            // row is where it is checked: a clear case has to sit plainly above the black PS2 and
            // GameCube cases and plainly below the white Wii one. Above the Wii it stops reading as
            // clear at all and starts reading as the PS5 case this mesh was modelled from.
            BodyAlbedoScale: 0.34f,
            // The case is a sheet of clear polypropylene over a printed sleeve, which is the jewel
            // case's situation and not the DVD case's — that one earns its gloss from a scanned
            // metallic/roughness map this shell does not have. Weaker than the jewel case's full
            // coat, because polypropylene is softer and less optically flat than polystyrene: a
            // keep case has a sheen where a jewel case has a reflection.
            ClearcoatFactor: 0.55f,
            ClearcoatRoughness: 0.10f),

        // SEMA Game Studio's compact disc, stripped to its geometry and material factors — see
        // THIRD-PARTY-NOTICES. The shape is what it was taken for: the raised hub, the stacking
        // ring and the rounded rim, none of which a generated annulus has. Everything about its
        // surface is stated below instead, which is also what removes the last of the source's
        // "SONY CD-R" trade dress from the build.
        [MediaShell.GameDisc] = new MediaShellDefinition(
            MediaShell.GameDisc,
            "EmuShelf.Rendering.Assets.game-disc.glb",
            DiscOrientation,
            // Nothing to downsample: the prepared asset carries no maps at all.
            MaxTextureSize: 1,
            // A pressed disc's printed area runs from the hub out to near the rim, so the label is
            // a circle rather than a rectangle — a corner radius of half the panel's shorter edge
            // rounds the square away entirely. It stays inscribed in the disc: the far corner of a
            // 0.7 square sits at 0.495 of the radius, just inside the edge, so no part of the
            // printed sheet hangs off the medium it is printed on.
            CoverPanel: new ArtPanel(
                ArtFace.Front, -0.7f, 0.7f, -0.7f, 0.7f, CornerRadius: 0.5f, ArtFit: ArtFit.Contain,
                MaxSurfaceDepth: DiscLabelDepth),
            ExtraPanels: [],
            // Screen-printed ink on lacquer: flatter than a keep case's sleeve under its clear
            // overlay, and nowhere near the mirror of the data side beneath it.
            PanelRoughness: 0.34f,
            FlattenPanelNormal: true,
            // glTF defaults an unstated metallic-roughness pair to 1.0/1.0 — a perfectly rough
            // mirror, which is not a plausible object and is what a stripped material inherits.
            // Metalness is right at 1; the roughness has to come almost all the way back down, and
            // this scale is what turns the source's default into a pressed aluminium reflector.
            BodyRoughnessScale: 0.09f,
            // The base colour factor is likewise an unstated white. Darker than aluminium's real
            // reflectance on purpose: this studio is bright, and a full mirror in it returns a flat
            // white face with no contrast left to show either the diffraction or the disc's shape.
            BodyAlbedoScale: 0.66f,
            AmbientIntensity: 0.64f,
            // Nothing on a disc casts onto anything else on it, and the flat faces have no moulding
            // for a cavity term to find.
            ShadowFillOcclusion: 0f,
            CavityStrength: 0f,
            Iridescence: 1f,
            // ScreenScraper's support texture, which for a disc system is a picture of the disc
            // itself. Its own face rather than the case's: the two shells are on screen together
            // during a launch, and slot 0 is the box scan — which is the one medium a disc's label
            // is never a picture of.
            // 3, matching the app layer's ShelfArtworkFace.DiscLabel. A literal because this
            // assembly knows about media and not about how the app names a game's faces; the two
            // are held together by MediaShellTests.GameDisc_DrawsTheScrapedDiscLabelNotTheBoxScan.
            CoverArtIndex: 3,
            // With no such artwork the panel draws nothing at all and the disc is simply a disc. A
            // flat tinted circle over the middle of a mirror does not read as a label — it reads as
            // half a disc and half a pasted-on texture, which is how it was reported.
            RequiresArtwork: true,
            // Ink on a silver substrate rather than a coloured chip, for when there is ink.
            PanelTintLift: 0.34f),
    };

    /// <summary>
    /// One face of the Blu-ray case's printed sleeve, which is a single wrapped sheet.
    /// </summary>
    /// <remarks>
    /// The three faces differ only in which way they point, so they are built rather than written
    /// out: three near-identical eleven-argument panels invite exactly the transcription slip the
    /// jewel case's Back/Spine comment warns about.
    ///
    /// <see cref="ArtFit.Stretch"/> for the same reason the keep case uses it — a box scan and a
    /// sleeve are the same shape by definition — and it is the spine that pays, being a 13mm strip
    /// wearing art scraped for a 135mm face. Nothing scrapes spine art yet, so what it actually
    /// wears is the platform tint, which has no shape to distort.
    ///
    /// The depth allowance is belt-and-braces here rather than load-bearing. Material scoping has
    /// already excluded the body, and the film's other two sheets face the wrong way for the panel
    /// to claim them, so a millimetre on a 171.5mm case exists to catch a re-export that welds the
    /// film into the body rather than to hold the print off anything in this one.
    /// </remarks>
    private static ArtPanel SleevePanel(ArtFace face) =>
        ArtPanel.Full(face, fit: ArtFit.Stretch, maxSurfaceDepth: 1f / 171.5f) with
        {
            Material = "cover",
        };

    /// <summary>
    /// How far behind the disc's front plane its printed label may follow, in canonical units.
    /// </summary>
    /// <remarks>
    /// Measured off the loaded mesh rather than derived from the disc's thickness, because the two
    /// disagree by more than the allowance is wide. A panel's plane sits at the model's furthest
    /// extent along its normal, and on this disc that extent is the raised stacking ring around the
    /// hub — not the face the label is printed on. The front-facing surfaces inside the panel sit
    /// at 0.002, 0.003 and 0.005 behind that plane, so an allowance sized as a fraction of the
    /// 0.0152 thickness rejected every one of them and the label did not draw at all.
    ///
    /// 0.007 clears the deepest of them with room to spare and still stops less than half way
    /// through the disc, so the data side — which is the whole 0.0152 away — can never take the
    /// print. Both bounds matter: the label has to reach the face, and it must not reach the back.
    /// </remarks>
    private const float DiscLabelDepth = 0.007f;

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
        return GlbLoader.Load(
            buffer.ToArray(),
            definition.Orientation,
            definition.MaxTextureSize,
            definition.TrimBelowHeightFraction);
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
        var (boundsMin, boundsMax) = panel.Material is null
            ? (model.BoundsMin, model.BoundsMax)
            : MaterialBounds(model, panel.Material);
        var half = (boundsMax - boundsMin) * 0.5f;
        var centre = (boundsMin + boundsMax) * 0.5f;

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

    /// <summary>
    /// The canonical-space bounds of everything wearing one material, which is what a
    /// material-scoped <see cref="ArtPanel"/> measures its rectangle against.
    /// </summary>
    /// <remarks>
    /// A shell's bounding box is the right frame for a label on the front of a cartridge and the
    /// wrong one for a screen recessed inside a machine: the arcade cabinet's screen is 0.4 of its
    /// width and a third of its height, so expressed against the whole cabinet every edge of the
    /// panel is a number nobody can check. Against the screen's own mesh they are the numbers you
    /// would measure with a rule — full width is 1.0, and an inset for the bezel is the inset.
    /// </remarks>
    public static (Vector3 Min, Vector3 Max) MaterialBounds(ModelAsset model, string material)
    {
        var index = -1;
        for (var candidate = 0; candidate < model.Materials.Count; candidate++)
        {
            if (string.Equals(model.Materials[candidate].Name, material, StringComparison.OrdinalIgnoreCase))
            {
                index = candidate;
                break;
            }
        }

        if (index < 0)
        {
            throw new ArgumentException(
                $"No material named '{material}' in this model; it has "
                + $"{string.Join(", ", model.Materials.Select(entry => entry.Name))}.",
                nameof(material));
        }

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var mesh in model.Meshes.Where(mesh => mesh.MaterialIndex == index))
        {
            for (var offset = 0; offset < mesh.Vertices.Length; offset += MeshGeometry.FloatsPerVertex)
            {
                var position = new Vector3(
                    mesh.Vertices[offset], mesh.Vertices[offset + 1], mesh.Vertices[offset + 2]);
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
        }

        if (min.X > max.X)
        {
            throw new ArgumentException(
                $"The material '{material}' exists but no mesh draws with it.", nameof(material));
        }

        return (min, max);
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
