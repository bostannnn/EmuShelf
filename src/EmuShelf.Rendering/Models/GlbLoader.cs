using System.Numerics;
using SharpGLTF.Schema2;
using StbImageSharp;

namespace EmuShelf.Rendering.Models;

/// <summary>
/// Reads a binary glTF (.glb) into a <see cref="ModelAsset"/> in canonical shell space.
/// </summary>
/// <remarks>
/// The node hierarchy is flattened at load: these shells are rigid props with no animation, so
/// baking each node's world matrix into its vertices costs nothing at runtime and lets the renderer
/// treat a model as a flat list of primitives. On top of that the loader applies the shell's
/// orientation matrix (the authored models variously lie on their backs or stand on edge) and then
/// normalises to one unit tall, so every medium arrives framed the same way.
/// </remarks>
public static class GlbLoader
{
    /// <param name="glb">The complete .glb file.</param>
    /// <param name="orientation">Rotates the authored model into canonical space: Y up, X right,
    /// +Z out of the face that carries the cover art.</param>
    /// <param name="maxTextureSize">Longest edge any decoded texture is allowed to keep. The
    /// authored models ship 2048² maps whose detail is invisible at the size the hero is drawn;
    /// downsampling here bounds both the upload and the resident GPU memory.</param>
    public static ModelAsset Load(byte[] glb, Matrix4x4 orientation, int maxTextureSize = 1024)
    {
        ArgumentNullException.ThrowIfNull(glb);

        var root = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));

        var textures = new List<TextureImage>();
        var textureBySourceImage = new Dictionary<int, int>();
        var materials = new List<ModelMaterial>();
        var materialByLogicalIndex = new Dictionary<int, int>();
        var meshes = new List<MeshGeometry>();

        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);

        foreach (var node in root.LogicalNodes)
        {
            if (node.Mesh is null)
            {
                continue;
            }

            // System.Numerics multiplies row vectors on the left, so "world then orientation" is
            // world * orientation, not the other way round.
            var toCanonical = node.WorldMatrix * orientation;
            // Normals need the inverse transpose to survive non-uniform scale, which these models
            // have in abundance (the GBA cart is assembled from three differently squashed cubes).
            var normalMatrix = Matrix4x4.Invert(toCanonical, out var inverted)
                ? Matrix4x4.Transpose(inverted)
                : Matrix4x4.Identity;

            foreach (var primitive in node.Mesh.Primitives)
            {
                var geometry = ReadPrimitive(
                    primitive, toCanonical, normalMatrix, root, textures, textureBySourceImage,
                    materials, materialByLogicalIndex, maxTextureSize, ref min, ref max);
                if (geometry is not null)
                {
                    meshes.Add(geometry);
                }
            }
        }

        if (meshes.Count == 0)
        {
            throw new InvalidDataException("The model contains no triangulated primitives.");
        }

        Normalise(meshes, ref min, ref max);

        return new ModelAsset
        {
            Meshes = meshes,
            Materials = materials,
            Textures = textures,
            BoundsMin = min,
            BoundsMax = max,
        };
    }

    private static MeshGeometry? ReadPrimitive(
        MeshPrimitive primitive,
        Matrix4x4 toCanonical,
        Matrix4x4 normalMatrix,
        ModelRoot root,
        List<TextureImage> textures,
        Dictionary<int, int> textureBySourceImage,
        List<ModelMaterial> materials,
        Dictionary<int, int> materialByLogicalIndex,
        int maxTextureSize,
        ref Vector3 min,
        ref Vector3 max)
    {
        // Points and lines have no place on a physical-media shell, and skipping them keeps the
        // draw path a single glDrawElements(GL_TRIANGLES).
        if (primitive.DrawPrimitiveType is not (PrimitiveType.TRIANGLES or PrimitiveType.TRIANGLE_STRIP
            or PrimitiveType.TRIANGLE_FAN))
        {
            return null;
        }

        var positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor is null)
        {
            return null;
        }

        var positions = positionAccessor.AsVector3Array();
        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();

        var vertexCount = positions.Count;
        var vertices = new float[vertexCount * MeshGeometry.FloatsPerVertex];

        for (var i = 0; i < vertexCount; i++)
        {
            var position = Vector3.Transform(positions[i], toCanonical);
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);

            var normal = normals is null
                ? Vector3.UnitZ
                : Vector3.TransformNormal(normals[i], normalMatrix);
            normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;

            var uv = uvs is null ? Vector2.Zero : uvs[i];

            var o = i * MeshGeometry.FloatsPerVertex;
            vertices[o + 0] = position.X;
            vertices[o + 1] = position.Y;
            vertices[o + 2] = position.Z;
            vertices[o + 3] = normal.X;
            vertices[o + 4] = normal.Y;
            vertices[o + 5] = normal.Z;
            vertices[o + 6] = uv.X;
            vertices[o + 7] = uv.Y;
        }

        // GetTriangleIndices() fans/unstrips for us, so strip and fan primitives arrive as plain
        // triangles and the renderer never has to know the difference.
        var indices = new List<uint>(vertexCount * 3);
        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            indices.Add((uint)a);
            indices.Add((uint)b);
            indices.Add((uint)c);
        }

        if (indices.Count == 0)
        {
            return null;
        }

        return new MeshGeometry
        {
            Vertices = vertices,
            Indices = indices.ToArray(),
            MaterialIndex = ResolveMaterial(
                primitive.Material, root, textures, textureBySourceImage, materials,
                materialByLogicalIndex, maxTextureSize),
        };
    }

    private static int ResolveMaterial(
        Material? material,
        ModelRoot root,
        List<TextureImage> textures,
        Dictionary<int, int> textureBySourceImage,
        List<ModelMaterial> materials,
        Dictionary<int, int> materialByLogicalIndex,
        int maxTextureSize)
    {
        if (material is null)
        {
            return -1;
        }

        if (materialByLogicalIndex.TryGetValue(material.LogicalIndex, out var existing))
        {
            return existing;
        }

        var baseColour = material.FindChannel("BaseColor");
        var metallicRoughness = material.FindChannel("MetallicRoughness");
        var normal = material.FindChannel("Normal");
        var occlusion = material.FindChannel("Occlusion");
        var clearcoat = material.FindChannel("ClearCoat");
        var clearcoatRoughness = material.FindChannel("ClearCoatRoughness");

        var resolved = new ModelMaterial
        {
            Name = material.Name ?? $"material{material.LogicalIndex}",
            BaseColorFactor = ReadColour(baseColour, Vector4.One),
            MetallicFactor = ReadFactor(metallicRoughness, "Metallic", 1f),
            RoughnessFactor = ReadFactor(metallicRoughness, "Roughness", 1f),
            BaseColorTexture = ResolveTexture(
                baseColour, root, textures, textureBySourceImage, maxTextureSize),
            MetallicRoughnessTexture = ResolveTexture(
                metallicRoughness, root, textures, textureBySourceImage, maxTextureSize),
            NormalTexture = ResolveTexture(
                normal, root, textures, textureBySourceImage, maxTextureSize),
            OcclusionTexture = ResolveTexture(
                occlusion, root, textures, textureBySourceImage, maxTextureSize),
            ClearcoatFactor = ReadFactor(clearcoat, "ClearCoatFactor", 0f),
            ClearcoatRoughness = ReadFactor(clearcoatRoughness, "ClearCoatRoughnessFactor", 0.04f),
        };

        materials.Add(resolved);
        var index = materials.Count - 1;
        materialByLogicalIndex[material.LogicalIndex] = index;
        return index;
    }

    private static Vector4 ReadColour(MaterialChannel? channel, Vector4 fallback)
    {
        if (channel is not { } value)
        {
            return fallback;
        }

        foreach (var parameter in value.Parameters)
        {
            if (parameter.Value is Vector4 colour)
            {
                return colour;
            }
        }

        return fallback;
    }

    // The metallic and roughness scalars live in the channel's parameter list rather than on a typed
    // property, and the key spelling has moved between SharpGLTF versions ("MetallicFactor" vs
    // "Metallic"), so match on the stem and fall back to the glTF default.
    private static float ReadFactor(MaterialChannel? channel, string stem, float fallback)
    {
        if (channel is not { } value)
        {
            return fallback;
        }

        foreach (var parameter in value.Parameters)
        {
            if (parameter.Name.Contains(stem, StringComparison.OrdinalIgnoreCase)
                && parameter.Value is float scalar)
            {
                return scalar;
            }
        }

        return fallback;
    }

    private static int ResolveTexture(
        MaterialChannel? channel,
        ModelRoot root,
        List<TextureImage> textures,
        Dictionary<int, int> textureBySourceImage,
        int maxTextureSize)
    {
        var image = channel?.Texture?.PrimaryImage;
        if (image is null)
        {
            return -1;
        }

        // Several materials can share one image (the GBA cart reuses a 605-byte roughness map
        // across two of its plastics); decode it once.
        if (textureBySourceImage.TryGetValue(image.LogicalIndex, out var existing))
        {
            return existing;
        }

        var content = image.Content.Content;
        if (content.Length == 0)
        {
            return -1;
        }

        var decoded = ImageResult.FromMemory(content.ToArray(), ColorComponents.RedGreenBlueAlpha);
        var texture = new TextureImage
        {
            Width = decoded.Width,
            Height = decoded.Height,
            Rgba = decoded.Data,
        };

        textures.Add(Downsample(texture, maxTextureSize));
        var index = textures.Count - 1;
        textureBySourceImage[image.LogicalIndex] = index;
        return index;
    }

    /// <summary>Box-averages an image down until its longest edge fits <paramref name="maxSize"/>.</summary>
    /// <remarks>Averaging whole source blocks rather than point-sampling matters here: these are
    /// photographic scans of plastic, and a nearest-neighbour halving would alias the moulding
    /// speckle into visible crawl once the model rotates.</remarks>
    internal static TextureImage Downsample(TextureImage source, int maxSize)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (maxSize <= 0 || longest <= maxSize)
        {
            return source;
        }

        var scale = (double)maxSize / longest;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var destination = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var y0 = y * source.Height / height;
            var y1 = Math.Max(y0 + 1, (y + 1) * source.Height / height);

            for (var x = 0; x < width; x++)
            {
                var x0 = x * source.Width / width;
                var x1 = Math.Max(x0 + 1, (x + 1) * source.Width / width);

                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var sy = y0; sy < y1; sy++)
                {
                    var row = sy * source.Width * 4;
                    for (var sx = x0; sx < x1; sx++)
                    {
                        var o = row + (sx * 4);
                        r += source.Rgba[o];
                        g += source.Rgba[o + 1];
                        b += source.Rgba[o + 2];
                        a += source.Rgba[o + 3];
                        n++;
                    }
                }

                var d = ((y * width) + x) * 4;
                destination[d] = (byte)(r / n);
                destination[d + 1] = (byte)(g / n);
                destination[d + 2] = (byte)(b / n);
                destination[d + 3] = (byte)(a / n);
            }
        }

        return new TextureImage { Width = width, Height = height, Rgba = destination };
    }

    /// <summary>Centres the model on the origin and scales it to exactly one unit tall.</summary>
    private static void Normalise(List<MeshGeometry> meshes, ref Vector3 min, ref Vector3 max)
    {
        var centre = (min + max) * 0.5f;
        var height = max.Y - min.Y;
        var scale = height > 1e-6f ? 1f / height : 1f;

        foreach (var mesh in meshes)
        {
            var vertices = mesh.Vertices;
            for (var o = 0; o < vertices.Length; o += MeshGeometry.FloatsPerVertex)
            {
                vertices[o + 0] = (vertices[o + 0] - centre.X) * scale;
                vertices[o + 1] = (vertices[o + 1] - centre.Y) * scale;
                vertices[o + 2] = (vertices[o + 2] - centre.Z) * scale;
            }
        }

        min = (min - centre) * scale;
        max = (max - centre) * scale;
    }
}
