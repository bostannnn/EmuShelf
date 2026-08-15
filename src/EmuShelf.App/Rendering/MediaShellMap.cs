using System.Numerics;
using EmuShelf.Rendering.Shells;

namespace EmuShelf.App.Rendering;

/// <summary>
/// Maps an EmuShelf system id to the physical medium its games shipped on.
/// </summary>
/// <remarks>
/// This is the app layer's job rather than the renderer's: EmuShelf.Rendering knows about media,
/// not about consoles. Systems absent from the authored table use a thin cover card in the shared
/// scene rather than borrowing an inaccurate generic cartridge or box.
/// </remarks>
public static class MediaShellMap
{
    private static readonly Dictionary<string, MediaShell> BySystemId = new(StringComparer.Ordinal)
    {
        ["snes"] = MediaShell.SnesCartridge,
        ["gba"] = MediaShell.GbaCartridge,
        ["gbc"] = MediaShell.GbcCartridge,
        // One jewel case, two consoles: a PAL/US Dreamcast game shipped in the same CD case a PS1
        // game did. The same arrangement the keep case already has, and the reason MediaShell is
        // one entry per authored geometry family rather than per console.
        ["playstation"] = MediaShell.JewelCase,
        ["dreamcast"] = MediaShell.JewelCase,
        ["nes"] = MediaShell.NesCartridge,
        ["megadrive"] = MediaShell.MegaDriveCartridge,
        ["nds"] = MediaShell.DsCard,
        // Its own card, not the DS one recoloured: the two footprints agree to a millimetre, and
        // the tab that stops a 3DS card entering a DS is the only thing that says which is which.
        ["3ds"] = MediaShell.Nintendo3dsCard,

        // Arcade has no physical medium — nobody ever owned the ROM board — so the machine stands
        // in for it, cut off under its control panel so it is a bartop rather than a wardrobe.
        ["arcade"] = MediaShell.ArcadeCabinet,

        // A PS3 game ships in the shorter Blu-ray case, which is now its own authored geometry
        // rather than a profile stretched over the DVD one.
        ["playstation3"] = MediaShell.BluRayCase,

        // One temporary geometry family, four distinct profiles. PSP is the odd one: the other
        // three shipped in this exact case, and a UMD case is a different object that borrows it —
        // see its profile below for what that costs and why it is still worth it.
        ["playstation2"] = MediaShell.DiscKeepCase,
        ["gamecube"] = MediaShell.DiscKeepCase,
        ["wii"] = MediaShell.DiscKeepCase,
        ["psp"] = MediaShell.DiscKeepCase,
    };

    private static readonly Dictionary<string, PhysicalMediaProfile> ProfilesBySystemId = new(StringComparer.Ordinal)
    {
        // 77.5mm, not the 87mm first recorded here. The scene scales each axis of a shell
        // independently onto its profile, so a profile that disagrees with its asset's real
        // proportions does not read as a size error — it silently distorts the model. This shell's
        // own width/depth ratios agree with 129mm and 20mm to within 0.4%, which is what identifies
        // the height as the wrong figure: 87mm was stretching the PAL cartridge 12% vertically.
        // The presentation correction absorbs the height that removing the stretch gives back, so
        // the reviewed composition keeps its vertical framing. Guarded by
        // MediaShellTests.MetricProfiles_MatchTheProportionsOfTheirAuthoredAsset.
        ["snes"] = new(
            MediaShell.SnesCartridge, new(129f, 77.5f, 20f), PhysicalArtworkSlots.CartridgeSupport,
            "snes-pal-grey", "cartridge-vertical", PresentationScale: 1.235f,
            FloorClearanceInShelfUnits: 0.014f),
        // Anchored on a real Game Pak's 57.5mm width and otherwise taking the asset's own ratios,
        // so the shape is exact even though the height lands at 32.9mm against a cited 35mm. The
        // previous 85 x 60mm figures stretched the old shell about 20%.
        ["gba"] = new(
            MediaShell.GbaCartridge, new(57.5f, 32.9f, 6.58f), PhysicalArtworkSlots.CartridgeSupport,
            "gba-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
        // Anchored on a real Game Boy cartridge's 57mm width, the same way GBA is, with the height
        // and depth following the asset's own ratios rather than the cited 65 x 8mm. The width
        // ratio is only 0.9% from the real one, so this is a small correction — but taking it means
        // the shell renders at exactly its authored shape, and the 8.99mm depth is honestly the
        // model's rather than a real cart's 8mm.
        ["gbc"] = new(
            MediaShell.GbcCartridge, new(57f, 64.42f, 8.99f), PhysicalArtworkSlots.CartridgeSupport,
            "gbc-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
        // A NES cartridge is portrait — 120mm across, 135mm tall — which is why it dwarfs a SNES
        // cart on the shelf. The depth is the asset's 18.3mm rather than the real 20mm: taking the
        // ratio from the model is what keeps the scene from absorbing the difference as a stretch,
        // which is exactly how the SNES shell shipped 12% wrong.
        ["nes"] = new(
            MediaShell.NesCartridge, new(120f, 135f, 18.3f), PhysicalArtworkSlots.CartridgeSupport,
            "nes-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.012f),
        // 109 x 70mm, not the 135 x 87mm first recorded here. Both carry the asset's W/H of 1.553,
        // which is why the error survived the proportion test and why the shell was never
        // distorted — it was simply a quarter too big, standing taller than a SNES cartridge that
        // really is a head taller than it. Ratio agreement checks shape, not size; only a
        // measurement does that. The depth is the asset's 11.8mm rather than a real cart's ~17mm,
        // taken from the model's ratio for the same reason as NES: a profile that disagrees with
        // its asset does not read as a size error, it silently distorts the shell.
        ["megadrive"] = new(
            MediaShell.MegaDriveCartridge, new(109f, 70f, 11.8f),
            PhysicalArtworkSlots.CartridgeSupport,
            "megadrive-black", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
        // A DS card is 33.4 x 35 x 3.8mm, and genuinely tiny beside a keep case — at true scale it
        // is under a fifth of one's height. That is the metric contract working, not a bug.
        // Anchored on the real 35mm height and otherwise taking satchii_'s asset's own ratios, which
        // is the rule every shell follows: a profile that disagrees with its asset does not read as
        // a size error, it silently distorts the shell. Its face is almost exactly right — 0.960 W/H
        // against a real card's 0.954 — and its thickness is not: 1.75mm against a real 3.8mm, which
        // is the price of the flat, unwarped front this asset was chosen for. Correcting that belongs
        // in the asset, not here.
        ["nds"] = new(
            MediaShell.DsCard, new(33.6f, 35f, 1.75f), PhysicalArtworkSlots.CartridgeSupport,
            "ds-black", "cartridge-vertical", FloorClearanceInShelfUnits: 0.008f),
        // The same 35mm anchor and the same rule as its DS sibling, and it lands closer to the real
        // object than any other cartridge here: 33.7 x 35 x 3.2mm against a real card's 33 x 35 x
        // 3.8mm, all three from a scan of one. The depth is the honest deviation — a scanned card is
        // 0.6mm thinner than a measured one, which at shelf scale is a sixth of a millimetre and
        // belongs in the asset rather than in a profile that would distort the shell to state it.
        // On a shelf beside a DS card the two are the same size, and that is not an error to correct:
        // the two cards really are one footprint, which is why the tab had to come from geometry.
        ["3ds"] = new(
            MediaShell.Nintendo3dsCard, new(33.7f, 35f, 3.2f),
            PhysicalArtworkSlots.CartridgeSupport,
            "3ds-white", "cartridge-vertical", FloorClearanceInShelfUnits: 0.008f),
        // Anchored on a real CD jewel case's 142mm width, with height and depth from the asset's
        // own ratios. It lands at 125.2 x 9.0mm against a nominal 125 x 10mm, which is as close as
        // any shell here has come — the width and height are the object's, and the 9mm is honestly
        // the model's once its lid is shut in prep. It ships open at 66mm.
        ["playstation"] = new(
            MediaShell.JewelCase, new(142f, 125.2f, 9.0f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "ps1-jewel", "case-downward"),
        // Same case, different finish. A Dreamcast jewel case is the whiter, colder plastic of the
        // two, which is the entire difference the shelf can express without separate geometry.
        ["dreamcast"] = new(
            MediaShell.JewelCase, new(142f, 125.2f, 9.0f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "dreamcast-jewel", "case-downward"),
        // The only profile that is not a measurement of a real object, and deliberately so. Cut
        // under its control panel the shell is the head of an upright cabinet, whose real
        // dimensions are about 660 x 930 x 1030mm — nearly five keep cases tall, and since the
        // shelf camera frames the tallest medium in the library view, one arcade game would shrink
        // every cartridge beside it to a chip. So it is sized as the bartop machine it now looks
        // like, 480mm tall, and takes the asset's own width and depth ratios from there. That keeps
        // it undistorted and plainly the biggest thing on the shelf — a machine among boxes — at
        // two and a half keep cases rather than five. The ratios are the chopped upright's, not a
        // real bartop's: deeper than it is wide, which is exactly what the geometry is.
        ["arcade"] = new(
            MediaShell.ArcadeCabinet, new(341f, 480f, 533f), PhysicalArtworkSlots.Front,
            "arcade-cabinet", "cartridge-vertical"),
        // The four keep-case profiles take "disc-from-case": their launch opens the case and sends the
        // disc on alone, because nobody has ever turned a DVD case over and pushed it into a
        // console. PS1, Dreamcast and PSP are disc media too and deliberately stay on the cartridge
        // motion — they render as flat cover cards until their own shells are authored, and a disc
        // cannot be pulled out of a card. PS1 and Dreamcast now have real jewel cases and a PSP a
        // UMD case, so the first two could plausibly join them and the third could not — a UMD is a
        // disc in a caddy you never take out. Left as they are here rather than decided in passing.
        ["playstation2"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine
                | PhysicalArtworkSlots.DiscLabel,
            "ps2-black", "disc-from-case", DiscDiameterMillimetres: 120f),
        // The Blu-ray shell the note here used to promise. This profile spent a year recording the
        // DVD case's 135 x 190 x 14mm and explaining why: a PS3 game really ships in a shorter
        // case, but saying so over shared DVD geometry came out as a 13.7% stretch, and of the two
        // honest options with one mesh, too tall beat the wrong shape. With the case authored the
        // choice is gone — 135 x 171.5 x 13mm is both the real object and, to three digits, the
        // asset's own scale, so this is the one profile here that is a transcription rather than a
        // measurement reconciled against a mesh. It is also the shortest of the disc cases now,
        // which is the point: a PS3 case really does stand a fifth shorter than a PS2 one beside it.
        // The disc it gives up stays 120mm — a Blu-ray is a full-size disc, and it is the case that
        // is smaller, which is the whole reason the two measurements are separate numbers.
        // See DECISIONS 2026-08-15.
        ["playstation3"] = new(
            MediaShell.BluRayCase, new(135f, 171.5f, 13f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine
                | PhysicalArtworkSlots.DiscLabel,
            "ps3-clear", "disc-from-case", DiscDiameterMillimetres: 120f),
        // 80mm, not 120mm: a GameCube game ships on a mini-DVD, and this is the one place the
        // difference is visible now that the disc leaves the case. The case is the shared stand-in
        // and cannot show it; the disc it gives up can, and comes out two thirds the size of a
        // Wii's from the same mesh.
        ["gamecube"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine
                | PhysicalArtworkSlots.DiscLabel,
            "gamecube-black", "disc-from-case", DiscDiameterMillimetres: 80f),
        ["wii"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine
                | PhysicalArtworkSlots.DiscLabel,
            "wii-white", "disc-from-case", DiscDiameterMillimetres: 120f),
        // A real UMD case, 104 x 178 x 15mm, and the one profile here that knowingly disagrees with
        // its asset: against the shared case's own 0.695 proportions this draws the mesh at 84% of
        // its authored width. That is deliberate, and it is now the only such disagreement left in
        // this table, so it needs its reason recorded.
        //
        // The rule those comments keep restating — take the asset's ratios, a profile that
        // disagrees silently distorts the shell — was written for cartridges, where the moulding
        // *is* the object. A keep case is not that. Its front is a flat sleeve that fills nearly
        // the whole silhouette, and the mesh's own contribution is a rim a few pixels wide. So the
        // squeeze has to be weighed against what it buys, and what it buys is the sleeve: this
        // panel is ArtFit.Stretch, and at 104mm the front face is 0.584 against a PSP box scan's
        // 0.581, so the art lands undistorted. At the asset's own 0.695 it stretches every scraped
        // PSP cover about 20% wider — on the one surface you actually look at.
        //
        // Rendered both before choosing. Squeezed mesh with correct art reads as a UMD case;
        // correct mesh with fat art reads as a slightly small PS2 case with something wrong with
        // the cover. PS3 faced the same trade and answered it differently, taking the undistorted
        // mesh at the wrong height; it is out of the comparison now that its own case is authored,
        // and what that leaves behind is the more useful lesson — the trade only ever existed
        // because two objects were sharing one mesh, and it ends when they stop. Locked by
        // MediaShellTests.MetricProfile_TakesARealUmdCasesShapeToKeepItsSleeveUndistorted, which is
        // also why PSP is the one exclusion from the proportion theory. See DECISIONS 2026-08-15.
        ["psp"] = new(
            MediaShell.DiscKeepCase, new(104f, 178f, 15f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "psp-clear", "case-downward"),
    };

    /// <summary>The shell for a system, or null when it should keep its flat cover.</summary>
    public static MediaShell? ForSystem(string systemId) =>
        BySystemId.TryGetValue(systemId, out var shell) ? shell : null;

    /// <summary>Every system id this table claims to have authored media for.</summary>
    /// <remarks>
    /// Exposed only so tests can check the keys themselves, which <see cref="ForSystem"/> cannot:
    /// asked about an id, it answers about that id, so an entry filed under an id no system has —
    /// a typo, or a system id renamed on the other side of the app — is unreachable rather than
    /// wrong. The shell simply never renders and the platform quietly keeps a flat cover, which
    /// looks exactly like a platform that was never given a shell.
    /// </remarks>
    public static IEnumerable<string> MappedSystemIds => BySystemId.Keys;

    /// <summary>
    /// The metric profile used by the shared shelf scene. Systems without authored media become a
    /// thin card carrying their existing cover, so a mixed-platform row remains one continuous
    /// scene instead of dropping back to a separate 2D strip.
    /// </summary>
    public static PhysicalMediaProfile ProfileForSystem(string systemId, double coverAspectRatio)
    {
        if (ProfilesBySystemId.TryGetValue(systemId, out var profile))
        {
            return profile;
        }

        var safeAspect = float.IsFinite((float)coverAspectRatio) && coverAspectRatio > 0
            ? (float)coverAspectRatio
            : 0.708f;
        return new PhysicalMediaProfile(
            MediaShell.CoverCard,
            new Vector3(PhysicalMediaProfile.ReferenceHeightMillimetres * safeAspect,
                PhysicalMediaProfile.ReferenceHeightMillimetres, 5f),
            PhysicalArtworkSlots.Front,
            "cover-card",
            "card-downward");
    }
}
