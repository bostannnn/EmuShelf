using System;
using System.Collections.Generic;

namespace EmuShelf.App.Controls;

/// <summary>The physical medium a game shipped on, for the couch shelf's 3D hero. v1 authors two
/// archetypes; every other system falls back to its flat cover (see docs/couch-physical-media-shelf.md).</summary>
public enum MediaType
{
    SnesCartridge,
    Ps2KeepCase,
}

/// <summary>The six faces of the box. <see cref="MediaFace.Front"/> carries the cover art.</summary>
public enum MediaFace
{
    Front,
    Back,
    Left,
    Right,
    Top,
    Bottom,
}

public readonly record struct Vec3(double X, double Y, double Z);

public readonly record struct Vec2(double X, double Y);

/// <summary>A pinhole camera on the +Z axis looking down −Z. Model units are projected to screen
/// pixels (y-down) by a perspective divide. Kept as a value so the draw op can re-project the
/// subdivided texture grid with the exact same math the culling used.</summary>
public readonly record struct MediaCamera(double CamZ, double Focal, double CenterX, double CenterY)
{
    /// <summary>Depth of a world point from the camera plane; larger = farther. Always positive for
    /// box points because <see cref="CamZ"/> dwarfs the half-depth.</summary>
    public double Depth(Vec3 p) => CamZ - p.Z;

    public Vec2 Project(Vec3 p)
    {
        var depth = CamZ - p.Z;
        return new Vec2(CenterX + p.X / depth * Focal, CenterY - p.Y / depth * Focal);
    }
}

/// <summary>One box face after rotation + projection, ready to rasterize. Corners are ordered
/// TL, TR, BR, BL (matching cover UVs 0,0 → 1,0 → 1,1 → 0,1). <see cref="World"/> keeps the rotated
/// 3D corners so the draw can subdivide the quad in model space for perspective-correct texturing.</summary>
public sealed class ProjectedFace
{
    public required MediaFace Face { get; init; }
    public required Vec3[] World { get; init; }
    public required Vec2[] Screen { get; init; }
    /// <summary>Centroid depth for the painter's sort (larger = farther, drawn first).</summary>
    public required double Depth { get; init; }
    /// <summary>Lambert shade in [0,1]: 1 fully lit, lower in shadow. Multiply into the face colour.</summary>
    public required double Shade { get; init; }
}

/// <summary>Pure software-3D projection for <see cref="Media3DControl"/>. No Avalonia or Skia types,
/// so the geometry/culling/sort is unit-testable headless (the Skia rasterization is screenshot-tested).</summary>
public static class Media3DProjection
{
    // Camera distance in model units (box height is 1.0). Closer = stronger perspective; this keeps a
    // gentle foreshortening that still lets a thin case read as 3D when turned.
    private const double CameraDistance = 2.9;

    // Directional light (surface → light), upper-front-left. Normalized in the shade calc.
    private static readonly Vec3 LightDirection = new(-0.35, 0.55, 0.90);
    private const double Ambient = 0.55; // floor so a face turned from the light is dimmed, not black.

    /// <summary>Which physical medium a system's games render as on the shelf, or null to fall back to
    /// the flat cover. v1 authors the two most iconic archetypes; every other system stays flat.</summary>
    public static MediaType? ForSystem(string systemId) => systemId switch
    {
        "snes" => MediaType.SnesCartridge,
        "playstation2" => MediaType.Ps2KeepCase,
        _ => null,
    };

    /// <summary>Half-extents (x = width/2, y = height/2, z = depth/2) in model units, height ≈ 1.
    /// PS2 keep case ≈ 0.72 : 1.0 : 0.11; SNES cartridge is chunkier and near-square, and thick.</summary>
    public static Vec3 HalfExtents(MediaType type) => type switch
    {
        MediaType.Ps2KeepCase => new Vec3(0.36, 0.5, 0.055),
        MediaType.SnesCartridge => new Vec3(0.55, 0.5, 0.17),
        _ => new Vec3(0.36, 0.5, 0.055),
    };

    /// <summary>Builds the camera for a control of the given pixel size, sizing the box to a calm
    /// fraction of the height and centring it slightly high to leave room for the ground shadow.</summary>
    public static MediaCamera BuildCamera(double width, double height)
    {
        // Box model height is 1.0; map it to ~80% of the control height at the box's centre depth so the
        // face-on hero reads at about the same size as the flat cover it replaces.
        var focal = 0.80 * height * CameraDistance;
        return new MediaCamera(CameraDistance, focal, width / 2.0, height * 0.47);
    }

    /// <summary>Rotates, projects, back-face culls and painter-sorts the box for the given pose.
    /// Returns only the visible faces, farthest first.</summary>
    public static IReadOnlyList<ProjectedFace> Project(MediaType type, double yaw, double pitch, double width, double height)
    {
        var camera = BuildCamera(width, height);
        var e = HalfExtents(type);
        var faces = BuildFaces(e);
        var cameraPosition = new Vec3(0, 0, camera.CamZ);
        var light = Normalize(LightDirection);

        var visible = new List<ProjectedFace>(6);
        foreach (var (face, corners, normal) in faces)
        {
            var world = new Vec3[4];
            for (var i = 0; i < 4; i++)
                world[i] = Rotate(corners[i], yaw, pitch);
            var worldNormal = Rotate(normal, yaw, pitch);
            var centroid = Centroid(world);

            // Visible if the face normal points toward the camera.
            var view = new Vec3(cameraPosition.X - centroid.X, cameraPosition.Y - centroid.Y, cameraPosition.Z - centroid.Z);
            if (Dot(worldNormal, view) <= 0)
                continue;

            var screen = new Vec2[4];
            for (var i = 0; i < 4; i++)
                screen[i] = camera.Project(world[i]);

            var lambert = Math.Max(0, Dot(Normalize(worldNormal), light));
            visible.Add(new ProjectedFace
            {
                Face = face,
                World = world,
                Screen = screen,
                Depth = camera.Depth(centroid),
                Shade = Ambient + (1 - Ambient) * lambert,
            });
        }

        visible.Sort((a, b) => b.Depth.CompareTo(a.Depth)); // farthest first (painter's algorithm)
        return visible;
    }

    // Rotate a point: yaw about Y (turntable), then pitch about X (tilt).
    private static Vec3 Rotate(Vec3 p, double yaw, double pitch)
    {
        var (sinY, cosY) = (Math.Sin(yaw), Math.Cos(yaw));
        var x1 = p.X * cosY + p.Z * sinY;
        var z1 = -p.X * sinY + p.Z * cosY;
        var y1 = p.Y;

        var (sinP, cosP) = (Math.Sin(pitch), Math.Cos(pitch));
        var y2 = y1 * cosP - z1 * sinP;
        var z2 = y1 * sinP + z1 * cosP;
        return new Vec3(x1, y2, z2);
    }

    // The 6 faces, corners ordered TL, TR, BR, BL as seen from outside along +normal, in a y-up model.
    private static (MediaFace Face, Vec3[] Corners, Vec3 Normal)[] BuildFaces(Vec3 e)
    {
        double x = e.X, y = e.Y, z = e.Z;
        return
        [
            (MediaFace.Front,  [new(-x, y, z), new(x, y, z), new(x, -y, z), new(-x, -y, z)], new(0, 0, 1)),
            (MediaFace.Back,   [new(x, y, -z), new(-x, y, -z), new(-x, -y, -z), new(x, -y, -z)], new(0, 0, -1)),
            (MediaFace.Right,  [new(x, y, z), new(x, y, -z), new(x, -y, -z), new(x, -y, z)], new(1, 0, 0)),
            (MediaFace.Left,   [new(-x, y, -z), new(-x, y, z), new(-x, -y, z), new(-x, -y, -z)], new(-1, 0, 0)),
            (MediaFace.Top,    [new(-x, y, -z), new(x, y, -z), new(x, y, z), new(-x, y, z)], new(0, 1, 0)),
            (MediaFace.Bottom, [new(-x, -y, z), new(x, -y, z), new(x, -y, -z), new(-x, -y, -z)], new(0, -1, 0)),
        ];
    }

    private static Vec3 Centroid(Vec3[] q) =>
        new((q[0].X + q[1].X + q[2].X + q[3].X) / 4,
            (q[0].Y + q[1].Y + q[2].Y + q[3].Y) / 4,
            (q[0].Z + q[1].Z + q[2].Z + q[3].Z) / 4);

    private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static Vec3 Normalize(Vec3 v)
    {
        var len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len <= 1e-9 ? v : new Vec3(v.X / len, v.Y / len, v.Z / len);
    }
}
