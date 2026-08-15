using System.Numerics;
using EmuShelf.Rendering.Shells;

namespace EmuShelf.Rendering;

/// <summary>One visible object in the bounded physical-media shelf scene.</summary>
/// <param name="Key">Stable game key used by the renderer's bounded cover-texture cache.</param>
/// <param name="Profile">Geometry plus real-world dimensions.</param>
/// <param name="CentreX">Horizontal centre relative to the continuous shelf position.</param>
/// <param name="FocusAmount">Zero off-centre, one at the selection centre.</param>
/// <param name="Yaw">Rotation around the medium's up axis.</param>
/// <param name="Pitch">Rotation around the medium's right axis.</param>
/// <param name="Accent">This game's platform accent in linear colour space.</param>
/// <param name="LaunchVerticalOffset">Launch-only translation above/below the shared floor.</param>
/// <param name="LaunchDepthOffset">Launch-only translation toward the product camera.</param>
/// <param name="LaunchScale">Launch-only uniform presentation scale.</param>
/// <param name="Disc">Where this medium's loose disc is, once a launch has taken it out of the
/// case, or null whenever the two are still one object — which is every frame of ordinary browsing
/// and every frame of a cartridge's launch.</param>
public readonly record struct MediaShelfRenderItem(
    long Key,
    PhysicalMediaProfile Profile,
    float CentreX,
    float FocusAmount,
    float Yaw,
    float Pitch,
    Vector3 Accent,
    float LaunchVerticalOffset = 0f,
    float LaunchDepthOffset = 0f,
    float LaunchScale = 1f,
    MediaShelfDiscPose? Disc = null);

/// <summary>
/// The second body a disc-based launch puts on screen: the disc itself, free of its case.
/// </summary>
/// <remarks>
/// Deliberately measured against the medium's resting centre rather than against the case's current
/// pose. The whole point of the choreography is that the two separate — the case is set down while
/// the disc goes on alone — and offsets chained onto a falling case would drag the disc down with
/// it.
/// </remarks>
/// <param name="HorizontalOffset">Sideways travel from the medium's own shelf position.</param>
/// <param name="VerticalOffset">Travel above the medium's resting centre.</param>
/// <param name="DepthOffset">Travel toward the product camera.</param>
/// <param name="Spin">Rotation about the disc's own axis, in radians.</param>
/// <param name="Tilt">Rotation about the shelf's right axis: zero stands the disc up facing the
/// player, and a quarter turn lays it flat the way a tray takes it.</param>
/// <param name="Flip">Rotation about the shelf's up axis, which turns the disc over. Distinct from
/// <paramref name="Spin"/>, which turns it in its own plane and never shows the other face.</param>
/// <param name="Scale">Uniform scale about the disc's centre.</param>
public readonly record struct MediaShelfDiscPose(
    float HorizontalOffset,
    float VerticalOffset,
    float DepthOffset,
    float Spin,
    float Tilt,
    float Flip = 0f,
    float Scale = 1f);
