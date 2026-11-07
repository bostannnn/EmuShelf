namespace EmuShelf.Core.Systems;

/// <summary>
/// A game system (console) known to EmuShelf. Instances are defined by the
/// Integrations project; the rest of the app treats them as opaque data.
/// </summary>
/// <param name="CoverAspectRatio">
/// Box-art width divided by height for this platform. PlayStation jewel-case art
/// is square (1.0); the disc-case systems (PS2/PS3/GameCube/Wii) are portrait. The
/// library grid frames each cover at this ratio so <c>Stretch="Uniform"</c> fills
/// the frame without cropping the art or leaving grey letterbox bands.
/// </param>
/// <param name="Manufacturer">
/// The hardware maker (e.g. "Nintendo", "Sony", "Sega", "Arcade"). The navigation list
/// groups systems under this label and orders the groups by their oldest system. It is
/// purely a grouping key — the empty default leaves a system ungrouped. The chronological
/// order <em>within</em> a manufacturer is the authored order in <c>KnownSystems.All</c>.
/// </param>
/// <param name="IsDualScreen">
/// True for consoles whose own hardware has two screens the emulator renders together (the DS and
/// 3DS). EmuShelf launches such an emulator as a single external app that draws both console screens
/// itself, so the "which physical display?" launch-screen choice is meaningless for them — the
/// launch path forces the built-in screen and the setting is hidden. See
/// <see cref="Launching.LaunchScreenResolver"/>.
/// </param>
/// <param name="Era">
/// What the system's games came on: <see cref="SystemEra.Cartridge"/> (ROM cartridges and cards, and
/// arcade boards) or <see cref="SystemEra.Disc"/> (optical media, and digital-only libraries). Purely a
/// look: Handheld mode's top screen draws a pixel grid behind cartridge systems and scanlines behind disc
/// systems (HANDHELD-MODE.md, the Stage's backdrop).
/// </param>
/// <param name="Backdrops">
/// The animated backdrops Handheld mode draws behind this system on the Thor's native Stage (M45 H7), as
/// ids into <c>EmuShelf.Rendering</c>'s backdrop catalog. The Stage rotates through them; null or empty
/// keeps the era motif. Purely a look, like <see cref="Era"/>.
/// </param>
public sealed record GameSystem(
    string Id,
    string Name,
    string ShortName,
    string AccentColor,
    double CoverAspectRatio,
    string Manufacturer = "",
    bool IsDualScreen = false,
    SystemEra Era = SystemEra.Disc,
    IReadOnlyList<string>? Backdrops = null);

/// <summary>The kind of media a system's games shipped on; see <see cref="GameSystem.Era"/>.</summary>
public enum SystemEra
{
    /// <summary>Optical discs (and digital-only libraries): drawn with scanlines and a rolling bar.</summary>
    Disc,

    /// <summary>ROM cartridges and game cards (and arcade boards): drawn with a soft pixel grid.</summary>
    Cartridge,
}
