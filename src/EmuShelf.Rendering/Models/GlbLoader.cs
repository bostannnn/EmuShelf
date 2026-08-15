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
    /// <param name="trimBelowHeightFraction">Fraction of the authored model's height, measured up
    /// from its own bottom, below which geometry is cut away before the model is normalised. Zero,
    /// which is every shell but one, keeps the whole object.</param>
    public static ModelAsset Load(
        byte[] glb,
        Matrix4x4 orientation,
        int maxTextureSize = 1024,
        float trimBelowHeightFraction = 0f)
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

        if (trimBelowHeightFraction > 0f)
        {
            TrimBelow(meshes, min.Y + (trimBelowHeightFraction * (max.Y - min.Y)), ref min, ref max);
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
            // "RoughnessFactor", not "ClearCoatRoughnessFactor": the parameter is named within its
            // channel, so the channel already says which roughness this is. Asking for the longer
            // name matched nothing and returned this fallback for every value a file could carry,
            // which is indistinguishable from a file that carries none.
            ClearcoatRoughness = ReadFactor(clearcoatRoughness, "RoughnessFactor", 0.04f),
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

    /// <summary>
    /// Cuts every mesh off at a horizontal plane, keeping only what stands above it.
    /// </summary>
    /// <remarks>
    /// This exists for the arcade cabinet, and it is a presentation decision rather than a repair:
    /// a full upright cabinet is 1.8 metres of mostly empty plywood, so beside a 190mm keep case it
    /// is nine times the height and shrinks every other medium in the view to nothing. Cutting it
    /// under the control panel leaves the part that identifies an arcade machine — marquee,
    /// speakers, screen, joysticks and buttons — as an object the size of a bartop cabinet.
    ///
    /// It is done at load rather than baked into the shipped .glb for the same reason the panel
    /// rectangles are: the height is a constant somebody has to look at a render to settle, and
    /// re-cutting a 60MB source every time that moves is not a loop anybody would run. The removed
    /// triangles cost a few thousand vertices in a file whose bulk is textures.
    ///
    /// Triangles straddling the plane are clipped rather than dropped, so the cut is a clean
    /// straight edge instead of a torn one, and a floor is laid across the opening the cut leaves —
    /// see <see cref="CreateCutCap"/>. Leaving it open was tried first, on the reasoning that the
    /// cut face is the face the cabinet stands on: it is not, once the medium turns to launch, and
    /// a machine you can see straight through is not a machine.
    /// </remarks>
    private static void TrimBelow(
        List<MeshGeometry> meshes, float planeY, ref Vector3 min, ref Vector3 max)
    {
        var trimmedMin = new Vector3(float.PositiveInfinity);
        var trimmedMax = new Vector3(float.NegativeInfinity);

        for (var index = meshes.Count - 1; index >= 0; index--)
        {
            var trimmed = TrimMeshBelow(meshes[index], planeY);
            if (trimmed is null)
            {
                meshes.RemoveAt(index);
                continue;
            }

            meshes[index] = trimmed;
            for (var offset = 0; offset < trimmed.Vertices.Length; offset += MeshGeometry.FloatsPerVertex)
            {
                var position = new Vector3(
                    trimmed.Vertices[offset], trimmed.Vertices[offset + 1], trimmed.Vertices[offset + 2]);
                trimmedMin = Vector3.Min(trimmedMin, position);
                trimmedMax = Vector3.Max(trimmedMax, position);
            }
        }

        if (meshes.Count == 0)
        {
            throw new InvalidDataException(
                $"Trimming below y={planeY} removed every triangle in the model.");
        }

        if (CreateCutCap(meshes, planeY) is { } cap)
        {
            meshes.Add(cap);
        }

        min = trimmedMin;
        max = trimmedMax;
    }

    /// <summary>
    /// Lays a floor across the opening the cut leaves, in the body's own material.
    /// </summary>
    /// <remarks>
    /// The convex hull of the cut vertices rather than their actual outline, and one fan rather
    /// than a cap per mesh. Both are deliberate simplifications of a job that is genuinely fiddly —
    /// chaining clipped segments into loops, deciding which loop is a wall's outside and which its
    /// inside, triangulating each — and the simplification is safe here because a floor cannot be
    /// seen except from below the object: any place the hull overshoots the real outline is a place
    /// already hidden behind the cabinet's own side. What matters is that it is closed, and a hull
    /// always is, where a chained outline fails on the first mesh that cuts into two pieces.
    ///
    /// It wears the material that contributed most of the cut — the cabinet body — at a single
    /// texel taken from that material's own cut vertices. A constant UV rather than a projection:
    /// the underside has no authored UV layout to be right about, and one texel of the body's
    /// plastic reads as the same material without inventing moulding that is not there.
    /// </remarks>
    private static MeshGeometry? CreateCutCap(List<MeshGeometry> meshes, float planeY)
    {
        const int stride = MeshGeometry.FloatsPerVertex;
        var points = new List<Vector2>();
        var uvByMaterial = new Dictionary<int, (Vector2 Sum, int Count)>();

        foreach (var mesh in meshes)
        {
            for (var offset = 0; offset < mesh.Vertices.Length; offset += stride)
            {
                if (MathF.Abs(mesh.Vertices[offset + 1] - planeY) > 1e-4f)
                {
                    continue;
                }

                points.Add(new Vector2(mesh.Vertices[offset], mesh.Vertices[offset + 2]));
                var uv = new Vector2(mesh.Vertices[offset + 6], mesh.Vertices[offset + 7]);
                (Vector2 Sum, int Count) existing =
                    uvByMaterial.GetValueOrDefault(mesh.MaterialIndex, (Vector2.Zero, 0));
                uvByMaterial[mesh.MaterialIndex] = (existing.Sum + uv, existing.Count + 1);
            }
        }

        var hull = ConvexHull(points);
        if (hull.Count < 3)
        {
            return null;
        }

        var body = uvByMaterial.OrderByDescending(entry => entry.Value.Count).First();
        var capUv = body.Value.Sum / body.Value.Count;
        var centre = Vector2.Zero;
        foreach (var point in hull)
        {
            centre += point;
        }
        centre /= hull.Count;

        var vertices = new List<float>((hull.Count + 1) * stride);
        void Add(Vector2 point)
        {
            vertices.AddRange(
            [
                point.X, planeY, point.Y,
                0f, -1f, 0f,
                capUv.X, capUv.Y,
            ]);
        }

        Add(centre);
        foreach (var point in hull)
        {
            Add(point);
        }

        var indices = new List<uint>(hull.Count * 3);
        for (var index = 0; index < hull.Count; index++)
        {
            var current = (uint)(index + 1);
            var next = (uint)(((index + 1) % hull.Count) + 1);
            // Wind so the triangle's geometric normal agrees with the authored -Y: the shader
            // flips normals on back faces, so a fan wound the other way would light the floor as
            // though it faced the ceiling.
            var edge = Vector3.Cross(
                new Vector3(hull[index].X - centre.X, 0f, hull[index].Y - centre.Y),
                new Vector3(
                    hull[(index + 1) % hull.Count].X - centre.X,
                    0f,
                    hull[(index + 1) % hull.Count].Y - centre.Y));
            indices.AddRange(edge.Y > 0f ? [0u, next, current] : [0u, current, next]);
        }

        return new MeshGeometry
        {
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            MaterialIndex = body.Key,
        };
    }

    /// <summary>Andrew's monotone chain: the hull of a point set, in order.</summary>
    /// <remarks>
    /// Collinear and duplicate points are popped rather than kept, which matters here because a
    /// clipped cabinet wall contributes dozens of cut vertices along one straight edge.
    /// </remarks>
    private static List<Vector2> ConvexHull(List<Vector2> points)
    {
        if (points.Count < 3)
        {
            return points;
        }

        var sorted = points
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();

        static float Cross(Vector2 origin, Vector2 a, Vector2 b) =>
            ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));

        static List<Vector2> Chain(IEnumerable<Vector2> walk)
        {
            var chain = new List<Vector2>();
            foreach (var point in walk)
            {
                while (chain.Count >= 2
                    && Cross(chain[^2], chain[^1], point) <= 0f)
                {
                    chain.RemoveAt(chain.Count - 1);
                }

                chain.Add(point);
            }

            // The last point of each chain is the first of the other.
            chain.RemoveAt(chain.Count - 1);
            return chain;
        }

        var hull = Chain(sorted);
        hull.AddRange(Chain(Enumerable.Reverse(sorted)));
        return hull;
    }

    /// <summary>Clips one primitive against <c>y >= planeY</c>, or null if nothing survives.</summary>
    private static MeshGeometry? TrimMeshBelow(MeshGeometry mesh, float planeY)
    {
        const int stride = MeshGeometry.FloatsPerVertex;
        var source = mesh.Vertices;
        var vertices = new List<float>(source.Length);
        var indices = new List<uint>(mesh.Indices.Length);
        // Vertices already above the plane keep their identity, so an untouched triangle costs no
        // duplication and the common case stays cheap. Clipped vertices are always new.
        var remapped = new Dictionary<uint, uint>();

        uint Keep(uint original)
        {
            if (remapped.TryGetValue(original, out var existing))
            {
                return existing;
            }

            var offset = (int)original * stride;
            var index = (uint)(vertices.Count / stride);
            vertices.AddRange(source.AsSpan(offset, stride));
            remapped[original] = index;
            return index;
        }

        uint Split(uint below, uint above)
        {
            var belowOffset = (int)below * stride;
            var aboveOffset = (int)above * stride;
            var span = source[aboveOffset + 1] - source[belowOffset + 1];
            var t = MathF.Abs(span) < 1e-9f ? 0f : (planeY - source[belowOffset + 1]) / span;
            t = Math.Clamp(t, 0f, 1f);

            var index = (uint)(vertices.Count / stride);
            for (var component = 0; component < stride; component++)
            {
                vertices.Add(float.Lerp(source[belowOffset + component], source[aboveOffset + component], t));
            }

            // Land the new vertex exactly on the plane rather than within float error of it, so the
            // cut edge is straight even where the two ends are far apart.
            vertices[(int)index * stride + 1] = planeY;
            return index;
        }

        // Four is the most a triangle can give: two corners above the plane plus the two crossings,
        // and a boundary can only cross a plane twice.
        Span<uint> polygon = stackalloc uint[4];
        for (var triangle = 0; triangle + 2 < mesh.Indices.Length; triangle += 3)
        {
            var a = mesh.Indices[triangle];
            var b = mesh.Indices[triangle + 1];
            var c = mesh.Indices[triangle + 2];
            var count = 0;

            // Sutherland-Hodgman against the single half-space, walking the triangle's edges in
            // order so the clipped polygon keeps the source winding.
            Span<uint> corners = [a, b, c];
            for (var edge = 0; edge < 3; edge++)
            {
                var current = corners[edge];
                var next = corners[(edge + 1) % 3];
                var currentAbove = source[(int)current * stride + 1] >= planeY;
                var nextAbove = source[(int)next * stride + 1] >= planeY;

                if (currentAbove)
                {
                    polygon[count++] = Keep(current);
                }

                if (currentAbove != nextAbove)
                {
                    polygon[count++] = currentAbove ? Split(next, current) : Split(current, next);
                }
            }

            for (var fan = 2; fan < count; fan++)
            {
                indices.Add(polygon[0]);
                indices.Add(polygon[fan - 1]);
                indices.Add(polygon[fan]);
            }
        }

        return indices.Count == 0
            ? null
            : new MeshGeometry
            {
                Vertices = vertices.ToArray(),
                Indices = indices.ToArray(),
                MaterialIndex = mesh.MaterialIndex,
            };
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
