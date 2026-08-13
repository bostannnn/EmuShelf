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
        ["snes"] = new(
            MediaShell.SnesCartridge, new(129f, 87f, 20f), PhysicalArtworkSlots.CartridgeSupport,
            "snes-pal-grey", "cartridge-vertical"),
        ["gba"] = new(
            MediaShell.GbaCartridge, new(85f, 60f, 6f), PhysicalArtworkSlots.CartridgeSupport,
            "gba-grey", "cartridge-vertical"),
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
