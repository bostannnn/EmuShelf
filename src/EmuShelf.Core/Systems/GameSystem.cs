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
public sealed record GameSystem(
    string Id,
    string Name,
    string ShortName,
    string AccentColor,
    double CoverAspectRatio);
