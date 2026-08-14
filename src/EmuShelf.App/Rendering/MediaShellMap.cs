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
        ["nes"] = MediaShell.NesCartridge,
        ["megadrive"] = MediaShell.MegaDriveCartridge,
        ["nds"] = MediaShell.DsCard,

        // One temporary geometry family, four distinct profiles. PS1 (jewel case), Dreamcast
        // (jewel case) and PSP (UMD case) remain cover cards until those shells are authored.
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
        // A NES cartridge is portrait — 120mm across, 135mm tall — which is why it dwarfs a SNES
        // cart on the shelf. The depth is the asset's 18.3mm rather than the real 20mm: taking the
        // ratio from the model is what keeps the scene from absorbing the difference as a stretch,
        // which is exactly how the SNES shell shipped 12% wrong.
        ["nes"] = new(
            MediaShell.NesCartridge, new(120f, 135f, 18.3f), PhysicalArtworkSlots.CartridgeSupport,
            "nes-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.012f),
        // 135 x 87mm is a Mega Drive cartridge, and the asset's own W/H of 1.553 agrees with it to
        // three decimals. The depth is the asset's 14.6mm rather than a real cart's ~16mm, taken
        // from the model's ratio for the same reason as NES: a profile that disagrees with its
        // asset does not read as a size error, it silently distorts the shell.
        ["megadrive"] = new(
            MediaShell.MegaDriveCartridge, new(135f, 87f, 14.6f),
            PhysicalArtworkSlots.CartridgeSupport,
            "megadrive-black", "cartridge-vertical", FloorClearanceInShelfUnits: 0.013f),
        // A DS card is 33.4 x 35mm, and genuinely tiny beside a keep case — at true scale it is
        // under a fifth of one's height. That is the metric contract working, not a bug. The 1.75mm
        // depth is the asset's ratio; a real card is 3.8mm, so this model is about half as thick as
        // it should be, and correcting that belongs in the asset rather than in a profile that
        // would then distort it.
        ["nds"] = new(
            MediaShell.DsCard, new(33.4f, 35f, 1.75f), PhysicalArtworkSlots.CartridgeSupport,
            "ds-white", "cartridge-vertical", FloorClearanceInShelfUnits: 0.008f),
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
