using System.Numerics;
using EmuShelf.Integrations.Systems;
using EmuShelf.Rendering.Shells;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// One medium in the acceptance shot: the system it stands for and the profile the app renders it
/// with.
/// </summary>
/// <param name="SystemId">An EmuShelf system id, which is what makes this entry checkable. The
/// profile below is a hand-copy of the app's, and the id is the key that lets
/// <c>MediaShellTests.PreviewShelf_RendersTheSameProfilesTheAppDoes</c> fetch the real one and
/// compare — without it the two tables can only be compared by eye, which is how they drifted.</param>
/// <param name="Profile">The metric profile, copied from
/// <c>EmuShelf.App.Rendering.MediaShellMap</c>.</param>
public sealed record PreviewShelfEntry(string SystemId, PhysicalMediaProfile Profile)
{
    /// <summary>
    /// The shape of the cover the scraper would really return for this system, width over height.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="KnownSystems"/> rather than copied, because this is the third table in
    /// this file's history to hold the same numbers and the first two both went stale. It matters
    /// more than it looks: the stand-in cover used to be one fixed 512 x 724 image — 0.707, a disc
    /// case's shape — so a fit was never reviewed against the shape the scraper actually returns. A
    /// PSP box scan is 0.581 and its sleeve was being stretched 20% wider with nothing to show for
    /// it on screen; SNES cover art is landscape at 1.434, and its label was being judged against
    /// portrait art. A preview tool whose placeholder is the wrong shape reviews the placeholder.
    ///
    /// Resolved explicitly rather than with Single() so a bad id says which id. This runs inside a
    /// static initializer, so the exception a future editor of this table sees first is wrapped in
    /// a TypeInitializationException — and "Sequence contains no matching element", thrown from
    /// there, names neither the entry nor the id, which is the least useful message available at
    /// exactly the moment it matters most.
    /// </remarks>
    public double CoverAspect { get; } =
        KnownSystems.All.SingleOrDefault(system => system.Id == SystemId)?.CoverAspectRatio
        ?? throw new ArgumentException(
            $"PreviewShelf names the system '{SystemId}', which is not in KnownSystems.",
            nameof(SystemId));
}

/// <summary>
/// The media the acceptance shot draws, in the left-to-right order it draws them.
/// </summary>
/// <remarks>
/// A hand-copy of <c>EmuShelf.App.Rendering.MediaShellMap</c>, which this tool cannot reference: the
/// app is an Avalonia <c>WinExe</c> with a git-stamping build target, and pulling the whole UI into a
/// headless dev tool to read one static table is the worse trade. The renderer, which the tool does
/// reference, deliberately knows nothing about consoles.
///
/// So the copy stays, and a test in <c>EmuShelf.App.Tests</c> — which can see both — asserts it is
/// exact. That test is the point of this class existing at all rather than the array sitting inline
/// in Program.cs, where nothing outside the file could reach it. The list had already kept
/// pre-correction GBA and SNES figures through a whole milestone, and was still naming insertion
/// animations (<c>case-vertical</c>, <c>cover-card</c>) the app stopped using.
///
/// Order is a review decision, not an arbitrary one, and the test deliberately does not check it —
/// see the per-entry notes.
/// </remarks>
public static class PreviewShelf
{
    public static IReadOnlyList<PreviewShelfEntry> Entries { get; } =
    [
        // First rather than last, unlike every shell before it: it is the widest and tallest thing
        // in the composition, and appended it pushed the NES cartridge off the right-hand edge —
        // the same way the Mega Drive and Game Boy shells fell off it in their turn. Leading the
        // row also keeps it near the cases, which is the pairing worth checking by eye: the metric
        // contract is the only thing keeping a machine and a box in one scene at honest sizes.
        new("arcade", new PhysicalMediaProfile(
            MediaShell.ArcadeCabinet, new Vector3(341f, 480f, 533f),
            PhysicalArtworkSlots.Front, "arcade-cabinet", "cartridge-vertical")),
        // 3DS stands in for every system with no authored shell. A real system rather than a
        // synthetic card, so the entry has an id to check against, and its 1.129 box art is also
        // the only thing in this list exercising the shared card mesh scaled to a ratio that is not
        // portrait. The width is 190 x 1.129 — the fallback in MediaShellMap.ProfileForSystem
        // computes it, so it is derived rather than measured and has to be spelled out to the float
        // the app will produce.
        //
        // This was PS1 until the jewel case was authored, at which point PS1 stopped being an
        // unauthored system and the entry silently stopped standing for anything. Only two systems
        // are left with no shell; if 3DS ever gets one, this entry has to move again or the cover
        // card goes unrendered.
        new("3ds", new PhysicalMediaProfile(
            MediaShell.CoverCard, new Vector3(214.51f, 190f, 5f),
            PhysicalArtworkSlots.Front, "cover-card", "card-downward")),
        new("nds", new PhysicalMediaProfile(
            MediaShell.DsCard, new Vector3(33.6f, 35f, 1.75f),
            PhysicalArtworkSlots.CartridgeSupport, "ds-black", "cartridge-vertical",
            FloorClearanceInShelfUnits: 0.008f)),
        new("gba", new PhysicalMediaProfile(
            MediaShell.GbaCartridge, new Vector3(57.5f, 32.9f, 6.58f),
            PhysicalArtworkSlots.CartridgeSupport, "gba-grey", "cartridge-vertical",
            FloorClearanceInShelfUnits: 0.010f)),
        new("snes", new PhysicalMediaProfile(
            MediaShell.SnesCartridge, new Vector3(129f, 77.5f, 20f),
            PhysicalArtworkSlots.CartridgeSupport, "snes-pal-grey", "cartridge-vertical",
            PresentationScale: 1.235f, FloorClearanceInShelfUnits: 0.014f)),
        // Inside the frame for the same reason as the Mega Drive below. Appended last it fell off
        // the right-hand edge of the acceptance shot, which was already observed once while
        // reviewing it.
        new("gbc", new PhysicalMediaProfile(
            MediaShell.GbcCartridge, new Vector3(57f, 64.42f, 8.99f),
            PhysicalArtworkSlots.CartridgeSupport, "gbc-grey", "cartridge-vertical",
            FloorClearanceInShelfUnits: 0.010f)),
        // Landscape and thin, which is what separates it at a glance from the portrait keep case
        // further along. In frame rather than appended, for the reason the Mega Drive note records.
        // Dreamcast follows it for the same reason PSP follows the PS2 case: the two share this
        // geometry and differ only in finish, and a finish can only be judged against its neighbour.
        new("playstation", new PhysicalMediaProfile(
            MediaShell.JewelCase, new Vector3(142f, 125.2f, 9.0f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "ps1-jewel", "case-downward")),
        new("dreamcast", new PhysicalMediaProfile(
            MediaShell.JewelCase, new Vector3(142f, 125.2f, 9.0f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "dreamcast-jewel", "case-downward")),
        // Beside the SNES cartridge on purpose, and no longer last: it was off the right-hand edge
        // of the acceptance shot, which is how it kept a profile a quarter too big for a whole
        // milestone.
        new("megadrive", new PhysicalMediaProfile(
            MediaShell.MegaDriveCartridge, new Vector3(109f, 70f, 11.8f),
            PhysicalArtworkSlots.CartridgeSupport, "megadrive-black", "cartridge-vertical",
            FloorClearanceInShelfUnits: 0.010f)),
        new("playstation2", new PhysicalMediaProfile(
            MediaShell.DiscKeepCase, new Vector3(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "ps2-black", "case-downward")),
        // Immediately after the PS2 case on purpose: PSP shares that geometry and the entire claim
        // being reviewed is that it stands shorter and clearer beside it. Split apart in this list
        // the two would be judged separately, which is the one comparison that cannot answer the
        // question.
        new("psp", new PhysicalMediaProfile(
            MediaShell.DiscKeepCase, new Vector3(104f, 178f, 15f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "psp-clear", "case-downward")),
        // The other three disc-case finishes, in a row after PS2 and PSP. They carry identical
        // metrics to the PS2 case and differ only in material, which is exactly why they belong
        // here: a finish is the whole reason these platforms are distinguishable at all, and until
        // this list included them `ps3-clear`, `gamecube-black` and `wii-white` were rendered by
        // nothing. Three of the app's eight finishes had never appeared in any acceptance artefact.
        new("playstation3", new PhysicalMediaProfile(
            MediaShell.DiscKeepCase, new Vector3(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "ps3-clear", "case-downward")),
        new("gamecube", new PhysicalMediaProfile(
            MediaShell.DiscKeepCase, new Vector3(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "gamecube-black", "case-downward")),
        new("wii", new PhysicalMediaProfile(
            MediaShell.DiscKeepCase, new Vector3(135f, 190f, 14f),
            PhysicalArtworkSlots.Front | PhysicalArtworkSlots.Back | PhysicalArtworkSlots.Spine,
            "wii-white", "case-downward")),
        new("nes", new PhysicalMediaProfile(
            MediaShell.NesCartridge, new Vector3(120f, 135f, 18.3f),
            PhysicalArtworkSlots.CartridgeSupport, "nes-grey", "cartridge-vertical",
            FloorClearanceInShelfUnits: 0.012f)),
    ];
}
