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

        // One temporary geometry family, four distinct profiles. PSP (UMD case) remains a cover
        // card until that shell is authored.
        ["playstation2"] = MediaShell.DiscKeepCase,
        ["playstation3"] = MediaShell.DiscKeepCase,
        ["gamecube"] = MediaShell.DiscKeepCase,
        ["wii"] = MediaShell.DiscKeepCase,
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
        // Anchored on the real 35mm height and otherwise taking the blank-template asset's own
        // ratios, which is the rule every shell follows: a profile that disagrees with its asset
        // does not read as a size error, it silently distorts the shell. Two known deviations, both
        // the asset's and neither worth expressing as a stretch — it is 0.996 W/H against a real
        // card's 0.954, so about 4% squarer, and 2.6mm thick against a real 3.8mm.
        ["nds"] = new(
            MediaShell.DsCard, new(34.85f, 35f, 2.64f), PhysicalArtworkSlots.CartridgeSupport,
            "ds-black", "cartridge-vertical", FloorClearanceInShelfUnits: 0.008f),
        // 142mm is a real CD jewel case's width, with height and depth from the asset's own
        // ratios rather than the nominal 125 x 10mm — the same anchoring the Game Boy and Game Boy
        // Advance profiles use. The depth is the honest figure for this model once its lid is shut;
        // a real case is 10mm, and closing the lid in prep is what brought it from 29mm to here.
        ["playstation"] = new(
            MediaShell.JewelCase, new(142f, 122.5f, 7.6f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Spine,
            "ps1-jewel", "case-downward"),
        // Same case, different finish. A Dreamcast jewel case is the whiter, colder plastic of the
        // two, which is the entire difference the shelf can express without separate geometry.
        ["dreamcast"] = new(
            MediaShell.JewelCase, new(142f, 122.5f, 7.6f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Spine,
            "dreamcast-jewel", "case-downward"),
        ["playstation2"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "ps2-black", "case-downward"),
        // A PS3 game really does ship in the shorter 135x171x12mm Blu-ray case, and this profile
        // used to say so. The trouble is that it says so while sharing the DVD case's geometry, and
        // the scene scales each axis independently: measured against the asset that is a 13.7%
        // horizontal stretch, which does not read as "a shorter case" — it reads as a broken one.
        // Of the two honest options with one mesh, too tall beats the wrong shape, so PS3 renders
        // undistorted at the shared case's proportions until a Blu-ray shell is authored. The real
        // dimensions belong with that shell, not with a squashed stand-in. See DECISIONS 2026-08-14.
        ["playstation3"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "ps3-clear", "case-downward"),
        ["gamecube"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "gamecube-black", "case-downward"),
        ["wii"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "wii-white", "case-downward"),
    };

    /// <summary>The shell for a system, or null when it should keep its flat cover.</summary>
    public static MediaShell? ForSystem(string systemId) =>
        BySystemId.TryGetValue(systemId, out var shell) ? shell : null;

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
