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
public sealed record GameSystem(
    string Id,
    string Name,
    string ShortName,
    string AccentColor,
    double CoverAspectRatio,
    string Manufacturer = "");
