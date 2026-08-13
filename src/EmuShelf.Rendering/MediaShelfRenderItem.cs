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
public readonly record struct MediaShelfRenderItem(
    long Key,
    PhysicalMediaProfile Profile,
    float CentreX,
    float FocusAmount,
    float Yaw,
    float Pitch,
    Vector3 Accent);
