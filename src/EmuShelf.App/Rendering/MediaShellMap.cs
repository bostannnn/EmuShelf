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
        // Known to have the same defect: a Game Pak is ~57.5 x 35 x 6mm and this asset's own ratio
        // is 1.71, so 85 x 60 stretches it about 20% vertically. Correcting it truthfully also
        // roughly halves the cartridge on screen, which is a composition decision rather than a
        // data fix, so it is deliberately left for the GBA asset pass rather than changed blind.
        ["gba"] = new(
            MediaShell.GbaCartridge, new(85f, 60f, 6f), PhysicalArtworkSlots.CartridgeSupport,
            "gba-grey", "cartridge-vertical", FloorClearanceInShelfUnits: 0.010f),
        ["playstation2"] = new(
            MediaShell.DiscKeepCase, new(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "ps2-black", "case-downward"),
        // PS3 used the shorter Blu-ray case rather than the DVD-height PS2/Wii package. It can
        // share temporary geometry while metric scale and the material variant remain truthful.
        ["playstation3"] = new(
            MediaShell.DiscKeepCase, new(135f, 171f, 14f),
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
