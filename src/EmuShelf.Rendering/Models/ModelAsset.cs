using System.Numerics;

namespace EmuShelf.Rendering.Models;

/// <summary>
/// One drawable primitive: a triangle list in the model's canonical space, already flattened out of
/// the glTF node hierarchy. Vertices are interleaved position(3) / normal(3) / texcoord(2).
/// </summary>
/// <remarks>
/// Tangents are deliberately absent. Two of the three shipped shells carry a TANGENT attribute and
/// one does not, so the shader derives a cotangent frame from screen-space derivatives instead —
/// one code path for every model, and one less attribute to upload.
/// </remarks>
public sealed class MeshGeometry
{
    /// <summary>Floats per vertex in <see cref="Vertices"/>: 3 position + 3 normal + 2 uv.</summary>
    public const int FloatsPerVertex = 8;

    public required float[] Vertices { get; init; }

    public required uint[] Indices { get; init; }

    /// <summary>Index into <see cref="ModelAsset.Materials"/>, or -1 for the default material.</summary>
    public required int MaterialIndex { get; init; }

    public int TriangleCount => Indices.Length / 3;
}

/// <summary>A glTF metallic-roughness material, reduced to what the PBR shader consumes.</summary>
public sealed class ModelMaterial
{
    public required string Name { get; init; }

    public Vector4 BaseColorFactor { get; init; } = Vector4.One;

    public float MetallicFactor { get; init; } = 1f;

    public float RoughnessFactor { get; init; } = 1f;

    /// <summary>Index into <see cref="ModelAsset.Textures"/>, or -1 when the slot is unused.</summary>
    public int BaseColorTexture { get; init; } = -1;

    /// <inheritdoc cref="BaseColorTexture"/>
    public int MetallicRoughnessTexture { get; init; } = -1;

    /// <inheritdoc cref="BaseColorTexture"/>
    public int NormalTexture { get; init; } = -1;
}

/// <summary>A decoded, tightly packed RGBA8 image ready for <c>glTexImage2D</c>.</summary>
public sealed class TextureImage
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>Row-major RGBA8, <see cref="Width"/> * <see cref="Height"/> * 4 bytes.</summary>
    public required byte[] Rgba { get; init; }
}

/// <summary>
/// A loaded model in canonical shell space: Y up, X right, +Z out of the front face, centred on the
/// origin and scaled so the medium is exactly one unit tall. Every shell arrives in that space so a
/// single camera framing works for all of them.
/// </summary>
public sealed class ModelAsset
{
    public required IReadOnlyList<MeshGeometry> Meshes { get; init; }

    public required IReadOnlyList<ModelMaterial> Materials { get; init; }

    public required IReadOnlyList<TextureImage> Textures { get; init; }

    /// <summary>Bounds in canonical space. Y always spans -0.5..0.5; X and Z follow the real medium's
    /// proportions, so a case is wide and thin and a cartridge is chunky.</summary>
    public required Vector3 BoundsMin { get; init; }

    /// <inheritdoc cref="BoundsMin"/>
    public required Vector3 BoundsMax { get; init; }

    public Vector3 Size => BoundsMax - BoundsMin;
}
