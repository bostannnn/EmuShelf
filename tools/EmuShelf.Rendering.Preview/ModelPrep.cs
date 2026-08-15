using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EmuShelf.Rendering.Models;
using StbImageSharp;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Produces a redistributable runtime derivative of a downloaded shell by flattening the artwork of
/// the named materials and reducing every map to the portable runtime size.
/// </summary>
/// <remarks>
/// The models that ship a cartridge also ship a specific game's label — Battletoads, Sonic 2,
/// Pokémon. The modeller's CC BY licence covers the model, not the publisher's artwork, so the
/// label has to go before the derivative can be committed.
///
/// Where a model keeps its label on its own material (NES names it <c>sticker</c>) this is far
/// safer than <see cref="SnesModelPrep"/>'s approach of masking a rectangle of a shared atlas:
/// there is no rectangle to get wrong, nothing else samples the image, and the label geometry
/// survives as a blank plate for EmuShelf's own artwork to be projected onto. No mesh, material or
/// image index moves, so nothing downstream has to be remapped.
/// </remarks>
internal static class ModelPrep
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;

    /// <summary>Which of a material's maps a neutralize pass flattens.</summary>
    /// <remarks>
    /// All three is the default and the safer choice: a label baked into a base colour is usually
    /// embossed into the normal map beside it, so leaving that behind stamps the removed artwork
    /// back into EmuShelf's own. The keep case is the exception that needs the distinction — its
    /// base colour is a scan of a whole retail sleeve, while its normal and metallic/roughness maps
    /// carry only the case's ribs, hinge, seams and scuffs. Flattening those would trade a licence
    /// problem for a featureless slab.
    /// </remarks>
    [Flags]
    private enum NeutralMaps
    {
        /// <summary>
        /// Flatten nothing; the pass only reduces the maps for the runtime.
        /// </summary>
        /// <remarks>
        /// The arcade cabinet is the first shell that needs this. Its baked screen picture and
        /// "RETRO ADVENTURE" marquee are the modeller's own invention rather than a publisher's
        /// artwork, so the CC BY licence that covers the model covers them too — there is nothing
        /// to remove, and removing it anyway would cost the cabinet its marquee for no reason.
        /// Still worth a pass: 38 maps at 2048² are 62MB of source that has to reach the runtime
        /// size before it can be embedded.
        /// </remarks>
        None = 0,

        BaseColour = 1 << 0,
        Surface = 1 << 1,
        All = BaseColour | Surface,
    }

    /// <summary>One image to flatten: to what colour, and over which parts of itself.</summary>
    /// <param name="Rects">The islands to mask, or empty to flatten the whole map. More than one
    /// because a scan's game-identifying marks are not always one island: the 3DS card carries its
    /// label on the front and the title's own product serial moulded into the back, both in the one
    /// atlas its single material samples.</param>
    private readonly record struct NeutralImage(
        (byte R, byte G, byte B, byte A) Fill,
        IReadOnlyList<(float U0, float V0, float U1, float V1)> Rects);

    public static void Prepare(
        string inputPath,
        string outputPath,
        string? neutralMaterial,
        string? neutralRect,
        string? neutralFill,
        string? neutralMaps,
        bool singleInstance,
        bool bakeVertexColours,
        string? dropMeshes,
        string? closeLid,
        bool stripTextures,
        int maxTextureSize)
    {
        var rects = ParseRects(neutralRect);
        var materials = neutralMaterial is null ? null : Split(neutralMaterial);
        // Several rectangles mean one of two things, and which one is decided by how many materials
        // were named. Several materials: one rectangle each, because a jewel case's lid, promo card
        // and tray inlay are three photographs and the print starts at a different column in each.
        // One material, or none: every rectangle masks the same atlas, because one material can
        // carry more than one thing worth removing.
        if (rects.Count > 1 && materials is { Length: > 1 } && rects.Count != materials.Length)
        {
            throw new ArgumentException(
                $"--neutral-rect has {rects.Count} rectangles but --neutral-material names "
                + $"{materials.Length} materials. Give one rectangle, one per material, or name a "
                + "single material to mask several islands of its atlas.");
        }

        var maps = ParseMaps(neutralMaps);
        // A masked rectangle is only ever perfectly covered by the art panel if the two were
        // derived from each other, and they are not — the rectangle is read off an atlas and the
        // panel off the geometry. Any mismatch shows as a ring, so the fill defaults to a paper
        // grey for a real label and can be set to the shell's own plastic where it would otherwise
        // halo against a dark cartridge.
        var baseFill = ParseFill(neutralFill) ?? (214, 212, 206, (byte)255);
        var source = File.ReadAllBytes(inputPath);
        if (source.Length < 28
            || BinaryPrimitives.ReadUInt32LittleEndian(source) != GlbMagic
            || BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4)) != 2)
        {
            throw new InvalidDataException($"{inputPath} is not a GLB 2.0 file.");
        }

        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12)));
        var json = Encoding.UTF8.GetString(source, 20, jsonLength).TrimEnd('\0', ' ');
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new InvalidDataException("Empty GLB JSON.");
        var binStart = 20 + jsonLength + 8;

        StripAnimations(root);

        if (singleInstance)
        {
            KeepOneInstance(root);
        }

        if (stripTextures)
        {
            StripTextures(root, source, binStart, outputPath, inputPath);
            return;
        }

        if (bakeVertexColours)
        {
            BakeConstantVertexColours(root, source, binStart);
        }

        // Geometry edits run before the images are touched, because both read vertex data and the
        // image pass rewrites buffer views underneath it.
        if (closeLid is not null)
        {
            CloseHingedLid(root, source, binStart, Split(closeLid));
        }

        if (dropMeshes is not null)
        {
            DropMeshes(root, Split(dropMeshes));
        }

        var views = root["bufferViews"]!.AsArray();
        var images = root["images"]!.AsArray();
        var textures = root["textures"]?.AsArray();

        // After the geometry edits, because that is what makes a map unreachable: dropping the disc
        // leaves its 1024px label in the file, a quarter of the shipped bytes drawn by nothing.
        var orphans = OrphanedImages(root, textures);

        var neutralImages = materials is null
            ? ResolveAllMaterialImages(root, textures, baseFill, maps, rects)
            : ResolveNeutralImages(root, textures, materials, baseFill, maps, rects);
        // Named materials that do not exist are caught by name in ResolveNeutralImages, which is the
        // more useful error. This is what is left: they exist and none of them samples a map — which
        // is only an error if flattening was asked for at all. NeutralMaps.None runs the pass for
        // the map reduction alone.
        if (neutralImages.Count == 0 && maps != NeutralMaps.None)
        {
            throw new InvalidDataException(
                $"Nothing to neutralize: {(neutralMaterial is null ? "this model has" : $"'{neutralMaterial}' has")} "
                + "no texture the requested --neutral-maps would flatten.");
        }

        var replacements = new Dictionary<int, byte[]>();
        for (var imageIndex = 0; imageIndex < images.Count; imageIndex++)
        {
            var image = images[imageIndex]!.AsObject();
            var viewIndex = image["bufferView"]?.GetValue<int>()
                ?? throw new InvalidDataException($"Image {imageIndex} is not embedded.");
            var view = views[viewIndex]!.AsObject();
            var offset = view["byteOffset"]?.GetValue<int>() ?? 0;
            var length = view["byteLength"]!.GetValue<int>();

            if (orphans.Contains(imageIndex))
            {
                replacements[viewIndex] = PngWriter.Encode(1, 1, [0, 0, 0, 255]);
                image["mimeType"] = "image/png";
                continue;
            }

            var decoded = ImageResult.FromMemory(
                source.AsSpan(binStart + offset, length).ToArray(), ColorComponents.RedGreenBlueAlpha);
            var texture = new TextureImage
            {
                Width = decoded.Width,
                Height = decoded.Height,
                Rgba = decoded.Data,
            };

            if (neutralImages.TryGetValue(imageIndex, out var neutral))
            {
                if (neutral.Rects.Count == 0)
                {
                    Flatten(texture, neutral.Fill);
                }
                else
                {
                    foreach (var island in neutral.Rects)
                    {
                        FlattenRect(texture, neutral.Fill, island);
                    }
                }
            }

            var runtime = GlbLoader.Downsample(texture, maxTextureSize);
            replacements[viewIndex] = PngWriter.Encode(runtime.Width, runtime.Height, runtime.Rgba);
            image["mimeType"] = "image/png";
        }

        var flattened = materials is null
            ? "The label region of the shared atlas is flattened"
            : $"The artwork of {string.Join(", ", materials.Select(name => $"'{name}'"))} is flattened";
        var scope = maps == NeutralMaps.BaseColour
            ? " out of the base-colour map, leaving the model's own surface maps intact"
            : " to a blank label";
        // A model with nothing to neutralise still passes through for its map reduction, and the
        // provenance note has to say so rather than claim a flatten that never happened.
        var modification = maps == NeutralMaps.None
            ? "The author's own maps are kept as authored"
            : flattened + scope;
        WriteGlb(root, views, source, binStart, replacements, outputPath, inputPath, modification);
    }

    /// <summary>
    /// The images that carry the named material's artwork, and the flat value each should take.
    /// </summary>
    /// <remarks>
    /// Base colour becomes a neutral paper grey unless the caller names a fill; metallic/roughness
    /// keeps glTF's channel meaning (roughness in G, metalness in B) so a disabled panel still
    /// shades as a dielectric; normal becomes flat. Neutralizing all three is usually what matters —
    /// leaving the label's normal map behind would emboss the removed artwork into EmuShelf's own —
    /// which is why <see cref="NeutralMaps.All"/> is the default and narrowing it is deliberate.
    /// </remarks>
    private static Dictionary<int, NeutralImage> ResolveNeutralImages(
        JsonObject root,
        JsonArray? textures,
        IReadOnlyList<string> materialNames,
        (byte, byte, byte, byte) baseFill,
        NeutralMaps maps,
        IReadOnlyList<(float, float, float, float)> rects)
    {
        var result = new Dictionary<int, NeutralImage>();
        for (var slot = 0; slot < materialNames.Count; slot++)
        {
            // One rectangle serves every material, each material carries its own, or one material
            // carries all of them. The middle case is the jewel case's — its lid, its promo card and
            // its tray inlay are three photographs of the same case, and the print starts at a
            // different column in each. The last is the 3DS card's: one material, one atlas, two
            // islands to remove.
            var slotRects = rects.Count <= 1 || materialNames.Count == 1
                ? rects
                : [rects[slot]];
            var found = false;
            foreach (var material in root["materials"]?.AsArray() ?? [])
            {
                var node = material!.AsObject();
                if (!string.Equals(
                    node["name"]?.GetValue<string>(), materialNames[slot], StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                found = true;
                var pbr = node["pbrMetallicRoughness"]?.AsObject();
                AddMap(result, textures, maps, NeutralMaps.BaseColour, pbr?["baseColorTexture"], baseFill, slotRects);
                AddMap(result, textures, maps, NeutralMaps.Surface, pbr?["metallicRoughnessTexture"], (255, 128, 0, 255), slotRects);
                AddMap(result, textures, maps, NeutralMaps.Surface, node["normalTexture"], (128, 128, 255, 255), slotRects);
            }

            // Loud, because the silent version is the whole failure mode this pass exists to stop:
            // a mistyped material name leaves the publisher's artwork in a shipped binary and the
            // command still reports success.
            if (!found)
            {
                throw new InvalidDataException(
                    $"--neutral-material names '{materialNames[slot]}', which this model does not have. "
                    + $"It has: {string.Join(", ", MaterialNames(root))}.");
            }
        }

        return result;
    }

    private static IEnumerable<string> MaterialNames(JsonObject root) =>
        (root["materials"]?.AsArray() ?? []).Select(
            material => material?["name"]?.GetValue<string>() ?? "(unnamed)");

    private static void AddMap(
        Dictionary<int, NeutralImage> result,
        JsonArray? textures,
        NeutralMaps maps,
        NeutralMaps required,
        JsonNode? reference,
        (byte, byte, byte, byte) fill,
        IReadOnlyList<(float, float, float, float)> rects)
    {
        if ((maps & required) == 0)
        {
            return;
        }

        var textureIndex = reference?["index"]?.GetValue<int>();
        if (textureIndex is null || textures is null)
        {
            return;
        }

        var imageIndex = textures[textureIndex.Value]?["source"]?.GetValue<int>();
        if (imageIndex is null)
        {
            return;
        }

        // Two named materials sampling one atlas is legitimate — this model's lid and tray share
        // theirs — but only if they agree on what to mask. Overwriting instead would leave the
        // first material's artwork in the build and report success, which is the one outcome this
        // whole pass exists to prevent.
        if (result.TryGetValue(imageIndex.Value, out var existing)
            && (!existing.Rects.SequenceEqual(rects) || existing.Fill != fill))
        {
            throw new InvalidDataException(
                $"Image {imageIndex} is shared by materials that ask for different masks. Give them "
                + "one rectangle and one fill, or the second would silently undo the first.");
        }

        result[imageIndex.Value] = new NeutralImage(fill, rects);
    }

    /// <summary>A mesh's first primitive, with the world transform its node chain gives it.</summary>
    private sealed record MeshPlacement(int PositionAccessor, Matrix4x4 Transform);

    /// <summary>
    /// Resolves every named mesh to its composed node transform.
    /// </summary>
    /// <remarks>
    /// Reading a downloaded model's accessor bounds without walking the node tree first is the
    /// single most repeated mistake in this area — it is what made the DS card look like it was
    /// lying on its side when its node matrices already stood it up.
    /// </remarks>
    private static Dictionary<string, MeshPlacement> MeshPlacements(JsonObject root)
    {
        var nodes = root["nodes"]?.AsArray() ?? [];
        var meshes = root["meshes"]?.AsArray() ?? [];
        var found = new Dictionary<string, MeshPlacement>(StringComparer.OrdinalIgnoreCase);

        void Walk(int index, Matrix4x4 parent)
        {
            var node = nodes[index]!.AsObject();
            var local = NodeTransform(node);
            var world = local * parent;
            if (node["mesh"]?.GetValue<int>() is { } meshIndex)
            {
                var mesh = meshes[meshIndex]!.AsObject();
                var name = mesh["name"]?.GetValue<string>();
                var position = mesh["primitives"]?[0]?["attributes"]?["POSITION"]?.GetValue<int>();
                if (name is not null && position is not null)
                {
                    found[name] = new MeshPlacement(position.Value, world);
                }
            }

            foreach (var child in node["children"]?.AsArray() ?? [])
            {
                Walk(child!.GetValue<int>(), world);
            }
        }

        var scene = root["scenes"]?[root["scene"]?.GetValue<int>() ?? 0]?["nodes"]?.AsArray();
        foreach (var entry in scene ?? [])
        {
            Walk(entry!.GetValue<int>(), Matrix4x4.Identity);
        }

        return found;
    }

    private static Matrix4x4 NodeTransform(JsonObject node)
    {
        if (node["matrix"]?.AsArray() is { } m)
        {
            var v = m.Select(entry => entry!.GetValue<float>()).ToArray();
            return new Matrix4x4(
                v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7],
                v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
        }

        var result = Matrix4x4.Identity;
        if (node["scale"]?.AsArray() is { } s)
        {
            result *= Matrix4x4.CreateScale(
                s[0]!.GetValue<float>(), s[1]!.GetValue<float>(), s[2]!.GetValue<float>());
        }

        if (node["rotation"]?.AsArray() is { } r)
        {
            result *= Matrix4x4.CreateFromQuaternion(new Quaternion(
                r[0]!.GetValue<float>(), r[1]!.GetValue<float>(),
                r[2]!.GetValue<float>(), r[3]!.GetValue<float>()));
        }

        if (node["translation"]?.AsArray() is { } t)
        {
            result *= Matrix4x4.CreateTranslation(
                t[0]!.GetValue<float>(), t[1]!.GetValue<float>(), t[2]!.GetValue<float>());
        }

        return result;
    }

    /// <summary>The normal of the plane a set of points best fits: its least-spread direction.</summary>
    private static Vector3 PlaneNormal(IReadOnlyList<Vector3> points, Vector3 centre)
    {
        // Inverse power iteration on the covariance, done the cheap way: find the two directions
        // the points spread along most, and take what is left.
        var first = PrincipalDirection(points, centre);
        var residual = points
            .Select(p => p - centre - (first * Vector3.Dot(p - centre, first)))
            .ToArray();
        var second = PrincipalDirection(residual, Vector3.Zero);
        var normal = Vector3.Cross(first, second);
        return normal.LengthSquared() < 1e-12f ? Vector3.UnitZ : Vector3.Normalize(normal);
    }

    /// <summary>The direction a set of points is most spread along, by power iteration.</summary>
    private static Vector3 PrincipalDirection(IReadOnlyList<Vector3> points, Vector3 centre)
    {
        var direction = Vector3.Normalize(points[^1] - points[0]);
        if (!float.IsFinite(direction.X))
        {
            return Vector3.UnitX;
        }

        for (var pass = 0; pass < 24; pass++)
        {
            var next = Vector3.Zero;
            foreach (var point in points)
            {
                var offset = point - centre;
                next += offset * Vector3.Dot(offset, direction);
            }

            if (next.LengthSquared() < 1e-20f)
            {
                return direction;
            }

            direction = Vector3.Normalize(next);
        }

        return direction;
    }

    private static Matrix4x4 Invert(Matrix4x4 value) =>
        Matrix4x4.Invert(value, out var inverse)
            ? inverse
            : throw new InvalidDataException("A node transform is singular and cannot be inverted.");

    /// <summary>Where an accessor's elements physically live, honouring interleaving.</summary>
    private static (int Start, int Stride, int Count) PositionLayout(
        JsonObject root, int binStart, int accessorIndex)
    {
        var accessor = root["accessors"]![accessorIndex]!.AsObject();
        if (accessor["componentType"]!.GetValue<int>() != 5126
            || accessor["type"]!.GetValue<string>() != "VEC3")
        {
            throw new InvalidDataException($"Accessor {accessorIndex} is not a float VEC3 position.");
        }

        var view = root["bufferViews"]![accessor["bufferView"]!.GetValue<int>()]!.AsObject();
        const int packed = 3 * sizeof(float);
        return (
            binStart + (view["byteOffset"]?.GetValue<int>() ?? 0)
                + (accessor["byteOffset"]?.GetValue<int>() ?? 0),
            view["byteStride"]?.GetValue<int>() ?? packed,
            accessor["count"]!.GetValue<int>());
    }

    private static Vector3[] ReadPositions(
        JsonObject root, byte[] source, int binStart, MeshPlacement mesh)
    {
        var (start, stride, count) = PositionLayout(root, binStart, mesh.PositionAccessor);
        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var at = start + (i * stride);
            result[i] = new Vector3(
                BitConverter.ToSingle(source, at),
                BitConverter.ToSingle(source, at + sizeof(float)),
                BitConverter.ToSingle(source, at + (2 * sizeof(float))));
        }

        return result;
    }

    /// <summary>
    /// Writes positions back where they came from, leaving every other attribute untouched.
    /// </summary>
    /// <remarks>
    /// In place rather than as a rebuilt buffer, because these views are interleaved and shared
    /// between position, normal and UV accessors. Rebuilding one would mean rebuilding all three
    /// and remapping the accessors onto them, which is the class of edit this file already avoids
    /// everywhere else for the same reason.
    /// </remarks>
    private static void WritePositions(
        JsonObject root, byte[] source, int binStart, MeshPlacement mesh, Vector3[] positions)
    {
        var (start, stride, count) = PositionLayout(root, binStart, mesh.PositionAccessor);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        for (var i = 0; i < count; i++)
        {
            var at = start + (i * stride);
            BitConverter.TryWriteBytes(source.AsSpan(at), positions[i].X);
            BitConverter.TryWriteBytes(source.AsSpan(at + sizeof(float)), positions[i].Y);
            BitConverter.TryWriteBytes(source.AsSpan(at + (2 * sizeof(float))), positions[i].Z);
            min = Vector3.Min(min, positions[i]);
            max = Vector3.Max(max, positions[i]);
        }

        // glTF requires a position accessor's bounds, and readers trust them over the data.
        var accessor = root["accessors"]![mesh.PositionAccessor]!.AsObject();
        accessor["min"] = new JsonArray(min.X, min.Y, min.Z);
        accessor["max"] = new JsonArray(max.X, max.Y, max.Z);
    }

    private static string[] Split(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Detaches named meshes, for a file that ships more than the shell in one scene.</summary>
    /// <remarks>
    /// Sibling of <see cref="KeepOneInstance"/> and the same non-destructive trick — the node loses
    /// its <c>mesh</c> reference and <c>GlbLoader</c> skips it — but selected by name rather than by
    /// ordinal. The PS1 download is a jewel case with its disc lying beside it, and only the case is
    /// the shell; "keep the first" cannot express that.
    /// </remarks>
    private static void DropMeshes(JsonObject root, IReadOnlyCollection<string> names)
    {
        var meshes = root["meshes"]?.AsArray() ?? [];
        var dropped = 0;
        foreach (var node in root["nodes"]?.AsArray() ?? [])
        {
            var entry = node!.AsObject();
            var index = entry["mesh"]?.GetValue<int>();
            if (index is null)
            {
                continue;
            }

            var name = meshes[index.Value]?["name"]?.GetValue<string>() ?? string.Empty;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            entry.Remove("mesh");
            dropped++;
        }

        if (dropped == 0)
        {
            throw new InvalidDataException(
                $"--drop-meshes matched nothing. Wanted: {string.Join(", ", names)}.");
        }

        Console.WriteLine($"  detached {dropped} mesh node(s): {string.Join(", ", names)}");
    }

    /// <summary>
    /// Swings a lid that was modelled ajar back down onto its tray.
    /// </summary>
    /// <remarks>
    /// Downloaded case models are frequently posed for a product shot rather than closed, and this
    /// one is: the PS1 jewel case's lid stands open 9.2 degrees, which is why it measures 29mm thick
    /// against a real case's 10mm. That cannot be left to the profile. The scene scales each axis of
    /// a shell onto its measured dimensions independently, so a profile carrying the true 10mm would
    /// not close the lid — it would squash the whole case to a third of its depth — and a profile
    /// carrying the asset's 29mm ships a case visibly ajar with its cover art projected onto a
    /// tilted plane.
    ///
    /// <para>The first argument names the lid, whose lowest edge is the hinge and whose tilt is the
    /// angle. The remaining names are the meshes that swing with it — the side walls and any inner
    /// card attached to the lid. Selection within those is by height, so a wall's tray-side edge
    /// stays put while its lid-side edge comes down, which is what a hinge does. Two rules that
    /// each looked sufficient and were not: selecting by the lid's own plane misses an inner card
    /// attached at a different angle, and selecting by height alone catches the tray's own rim,
    /// which rises to within a hair of the hinge and gets thrown through the floor.</para>
    /// </remarks>
    private static void CloseHingedLid(
        JsonObject root, byte[] source, int binStart, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            throw new ArgumentException("--close-lid wants <lidMesh>[,<mesh>...].");
        }

        var placements = MeshPlacements(root);
        if (!placements.TryGetValue(names[0], out var lid))
        {
            throw new InvalidDataException($"--close-lid: no mesh named '{names[0]}'.");
        }

        var lidPoints = ReadPositions(root, source, binStart, lid)
            .Select(p => Vector3.Transform(p, lid.Transform))
            .ToArray();

        // Which way is "up" off the tray is the model's business, not a convention: the PS1 case
        // this was written for lies with its thickness on Y, the next one had it on Z. A lid is a
        // flat panel however far it is swung, so its smallest extent is the axis it lifts along.
        var spread = lidPoints.Aggregate(
            (Min: lidPoints[0], Max: lidPoints[0]),
            (bounds, p) => (Vector3.Min(bounds.Min, p), Vector3.Max(bounds.Max, p)));
        var extent = spread.Max - spread.Min;
        var up = extent.X <= extent.Y && extent.X <= extent.Z ? Vector3.UnitX
            : extent.Y <= extent.Z ? Vector3.UnitY
            : Vector3.UnitZ;

        float Height(Vector3 point) => Vector3.Dot(point, up);
        var lowest = lidPoints.Min(Height);
        var highest = lidPoints.Max(Height);
        var hinge = lidPoints.Where(p => Height(p) <= lowest + (0.02f * (highest - lowest))).ToArray();
        if (hinge.Length < 2)
        {
            throw new InvalidDataException("--close-lid: the lid has no identifiable hinge edge.");
        }

        // Derived from the lid's plane rather than from which vertices happen to sit lowest. A lid
        // is a panel, so the rotation that shuts it is the one taking its normal onto the tray's:
        // the hinge runs perpendicular to both, and the angle between them is how far it stands
        // open. Picking a hinge edge out of the vertex cloud instead looked reasonable and failed
        // twice — once on a diagonal chord, once on a scattered low edge — because a lid with two
        // hundred vertices has no single lowest edge to find.
        var centre = lidPoints.Aggregate(Vector3.Zero, (sum, p) => sum + p) / lidPoints.Length;
        var lidNormal = PlaneNormal(lidPoints, centre);
        if (Vector3.Dot(lidNormal, up) < 0f)
        {
            lidNormal = -lidNormal;
        }

        var axis = Vector3.Cross(lidNormal, up);
        if (axis.LengthSquared() < 1e-12f)
        {
            Console.WriteLine($"  '{names[0]}' is already shut; nothing to swing.");
            return;
        }

        axis = Vector3.Normalize(axis);
        var angle = MathF.Acos(Math.Clamp(Vector3.Dot(lidNormal, up), -1f, 1f));
        var pivot = lidPoints.Where(p => Height(p) <= lowest + (0.05f * (highest - lowest)))
            .Aggregate(Vector3.Zero, (sum, p) => sum + p)
            / lidPoints.Count(p => Height(p) <= lowest + (0.05f * (highest - lowest)));

        var rotation = Matrix4x4.CreateFromAxisAngle(axis, angle);
        var alternative = Matrix4x4.CreateFromAxisAngle(axis, -angle);
        float Flatness(Matrix4x4 candidate)
        {
            var swung = lidPoints
                .Select(p => Height(Vector3.Transform(p - pivot, candidate) + pivot))
                .ToArray();
            return swung.Max() - swung.Min();
        }

        if (Flatness(alternative) < Flatness(rotation))
        {
            rotation = alternative;
        }

        Console.WriteLine(
            $"  closing '{names[0]}': lid stands {angle * 180f / MathF.PI:F2} deg open about "
            + $"{(up == Vector3.UnitX ? "X" : up == Vector3.UnitY ? "Y" : "Z")}");

        var moved = 0;
        foreach (var name in names)
        {
            if (!placements.TryGetValue(name, out var mesh))
            {
                throw new InvalidDataException($"--close-lid: no mesh named '{name}'.");
            }

            var inverse = Invert(mesh.Transform);
            var local = ReadPositions(root, source, binStart, mesh);
            for (var i = 0; i < local.Length; i++)
            {
                var world = Vector3.Transform(local[i], mesh.Transform);
                if (Height(world) <= lowest - (0.02f * (highest - lowest)))
                {
                    continue;
                }

                var swung = Vector3.Transform(world - pivot, rotation) + pivot;
                local[i] = Vector3.Transform(swung, inverse);
                moved++;
            }

            WritePositions(root, source, binStart, mesh, local);
        }

        Console.WriteLine($"  swung {moved} lid vertices onto the tray");
    }

    /// <summary>
    /// Leaves only the first mesh-bearing node drawable, for a file that ships several copies of the
    /// same object.
    /// </summary>
    /// <remarks>
    /// The DS download is four identical cards laid out in a row by node matrices, so loading it
    /// as-is draws four cartridges. The duplicates lose their <c>mesh</c> reference rather than
    /// being deleted: the loader walks every logical node and skips those without one, so this
    /// needs no index remapping anywhere — and index remapping across meshes, accessors and buffer
    /// views is precisely where a prep step goes quietly wrong. The orphaned vertex data stays in
    /// the buffer; it is a fraction of a file whose bulk is textures, and nothing references it.
    /// </remarks>
    /// <summary>
    /// Removes animation data, which a runtime shell has no use for and can be actively broken.
    /// </summary>
    /// <remarks>
    /// Unconditional, because every shell in this catalogue is a rigid prop: the renderer poses each
    /// one from its own choreography and has no sampler to play a clip through, so this can only
    /// ever be dead weight. It earns its place by being more than dead weight in practice — the
    /// sourced compact disc carries a spin clip whose output accessor sits on a buffer view with a
    /// <c>byteStride</c>, which the glTF schema forbids on animation data. SharpGLTF validates on
    /// read, so the whole model failed to load over a clip nothing was ever going to play.
    ///
    /// Only the animations array goes. The accessors and buffer views it referenced are left where
    /// they are and simply become unreferenced, for the same reason the duplicate instances below
    /// keep their vertex data: renumbering indices across accessors and buffer views is exactly
    /// where a prep step goes quietly wrong, and the bytes are noise beside the textures.
    /// </remarks>
    private static void StripAnimations(JsonObject root)
    {
        if (root["animations"] is not JsonArray animations || animations.Count == 0)
        {
            return;
        }

        root.Remove("animations");
        Console.WriteLine($"  stripped {animations.Count} animation(s)");
    }

    /// <summary>
    /// Takes a model down to its geometry and material factors, discarding every map.
    /// </summary>
    /// <remarks>
    /// The blunt instrument, and sometimes the right one. Masking a rectangle assumes a model's
    /// branding sits somewhere a rectangle can reach; the sourced compact disc's does not. Its two
    /// faces share one atlas with interleaved circular islands, so every square that covers the
    /// "SONY CD-R 700MB" trade dress on the label also clips the brushed data surface — and the same
    /// logo is embossed a second time into the metallic/roughness map, where flattening it would
    /// take the disc's mirror with it.
    ///
    /// What the model was actually wanted for is its shape: the hub, the stacking ring and the
    /// rounded rim that a generated annulus does not have. Those are in the vertices. Dropping the
    /// maps keeps all of it, ships no third-party artwork whatsoever, and turns a 3.4MB download
    /// into a small one — and the surface the maps described is then supplied by the material
    /// factors and the game's own scraped disc art, which is what belongs on that face anyway.
    ///
    /// The image buffer views are emptied rather than deleted, and that distinction is the whole
    /// reason this is not two lines. Removing entries would renumber every view after them — the
    /// trap this file avoids everywhere else — while merely dropping the <c>images</c> array would
    /// leave <see cref="WriteGlb"/> faithfully copying 3.4MB of unreferenced PNG into the output,
    /// trade dress included. Emptying their payloads keeps every index where it was and takes the
    /// bytes out of the file, which is what actually matters here.
    /// </remarks>
    private static void StripTextures(
        JsonObject root,
        byte[] source,
        int binStart,
        string outputPath,
        string inputPath)
    {
        // Collected before the images array goes, since that is what names the views to empty.
        var imageViews = new Dictionary<int, byte[]>();
        foreach (var image in root["images"]?.AsArray() ?? [])
        {
            if (image?["bufferView"]?.GetValue<int>() is { } view)
            {
                // One byte, not none: glTF requires a buffer view to be at least a byte long, and
                // an empty one fails validation on read as surely as the malformed animation did.
                imageViews[view] = [0];
            }
        }

        var dropped = 0;
        foreach (var material in root["materials"]?.AsArray() ?? [])
        {
            var node = material!.AsObject();
            var pbr = node["pbrMetallicRoughness"]?.AsObject();
            foreach (var slot in new[] { "baseColorTexture", "metallicRoughnessTexture" })
            {
                if (pbr?.Remove(slot) == true)
                {
                    dropped++;
                }
            }

            foreach (var slot in new[] { "normalTexture", "occlusionTexture", "emissiveTexture" })
            {
                if (node.Remove(slot))
                {
                    dropped++;
                }
            }

            // Extension materials carry their own maps, and a clearcoat normal left pointing at a
            // texture index that no longer exists is a load failure rather than a missing detail.
            node.Remove("extensions");
        }

        root.Remove("textures");
        root.Remove("images");
        root.Remove("samplers");
        Console.WriteLine(
            $"  stripped {dropped} texture reference(s) and emptied {imageViews.Count} image(s)");

        WriteGlb(
            root, root["bufferViews"]!.AsArray(), source, binStart, imageViews,
            outputPath, inputPath,
            "Every texture map is removed, leaving the model's geometry and material factors");
    }

    private static void KeepOneInstance(JsonObject root)
    {
        var kept = false;
        var dropped = 0;
        foreach (var node in root["nodes"]?.AsArray() ?? [])
        {
            var entry = node!.AsObject();
            if (!entry.ContainsKey("mesh"))
            {
                continue;
            }

            if (!kept)
            {
                kept = true;
                continue;
            }

            entry.Remove("mesh");
            dropped++;
        }

        Console.WriteLine($"  kept one instance, detached {dropped} duplicate node(s)");
    }

    /// <summary>
    /// Moves a primitive's constant <c>COLOR_0</c> onto its material's base-colour factor.
    /// </summary>
    /// <remarks>
    /// Some exporters — OpenSceneGraph's, which is what Sketchfab used for older uploads — carry a
    /// model's entire colour scheme in per-vertex attributes and leave every material an untinted
    /// white dielectric. <see cref="MeshGeometry.FloatsPerVertex"/> is position, normal and UV only,
    /// so <c>GlbLoader</c> drops <c>COLOR_0</c> and such a model loads as a white blob with its
    /// geometry intact. Widening the vertex layout and the shaders to carry a colour channel is the
    /// general fix; this is the cheap one, and where the colours are constant per primitive it is
    /// not an approximation but an exact rewrite of the same information into a form the renderer
    /// already reads.
    ///
    /// Constancy is verified rather than sampled, and a material whose primitives disagree is an
    /// error: silently taking the first would tint parts of a shell wrongly with no way to see it
    /// short of a render.
    /// </remarks>
    private static void BakeConstantVertexColours(JsonObject root, byte[] source, int binStart)
    {
        var accessors = root["accessors"]?.AsArray() ?? [];
        var views = root["bufferViews"]?.AsArray() ?? [];
        var materials = root["materials"]?.AsArray() ?? [];
        var baked = new Dictionary<int, float[]>();

        foreach (var mesh in root["meshes"]?.AsArray() ?? [])
        {
            foreach (var primitive in mesh!["primitives"]!.AsArray())
            {
                var node = primitive!.AsObject();
                var colourIndex = node["attributes"]?["COLOR_0"]?.GetValue<int>();
                var materialIndex = node["material"]?.GetValue<int>();
                if (colourIndex is null || materialIndex is null)
                {
                    continue;
                }

                var colour = ConstantColour(accessors, views, source, binStart, colourIndex.Value);
                if (baked.TryGetValue(materialIndex.Value, out var existing))
                {
                    if (!existing.SequenceEqual(colour))
                    {
                        throw new InvalidDataException(
                            $"Material {materialIndex} is shared by primitives with different constant "
                            + "vertex colours, so it cannot carry one base-colour factor.");
                    }

                    continue;
                }

                baked[materialIndex.Value] = colour;
            }
        }

        foreach (var (index, colour) in baked)
        {
            var material = materials[index]!.AsObject();
            var pbr = material["pbrMetallicRoughness"]?.AsObject();
            if (pbr is null)
            {
                pbr = [];
                material["pbrMetallicRoughness"] = pbr;
            }

            // Respect a factor the author already set; glTF multiplies the two, so this keeps the
            // rewrite equivalent rather than merely plausible.
            var existing = pbr["baseColorFactor"]?.AsArray();
            var scaled = new JsonArray();
            for (var channel = 0; channel < 4; channel++)
            {
                var author = existing is not null && channel < existing.Count
                    ? existing[channel]!.GetValue<float>()
                    : 1f;
                scaled.Add(colour[channel] * author);
            }

            pbr["baseColorFactor"] = scaled;
        }

        Console.WriteLine($"  baked constant vertex colours onto {baked.Count} material(s)");
    }

    /// <summary>The colour every vertex of an accessor shares, or an error if they differ.</summary>
    private static float[] ConstantColour(
        JsonArray accessors, JsonArray views, byte[] source, int binStart, int accessorIndex)
    {
        var accessor = accessors[accessorIndex]!.AsObject();
        var view = views[accessor["bufferView"]!.GetValue<int>()]!.AsObject();
        var componentType = accessor["componentType"]!.GetValue<int>();
        var type = accessor["type"]!.GetValue<string>();
        var components = type switch
        {
            "VEC3" => 3,
            "VEC4" => 4,
            _ => throw new InvalidDataException($"COLOR_0 accessor {accessorIndex} is {type}."),
        };

        // glTF allows colours as float, or as normalized unsigned bytes/shorts.
        var (componentBytes, scale) = componentType switch
        {
            5121 => (1, 1f / byte.MaxValue),
            5123 => (2, 1f / ushort.MaxValue),
            5126 => (4, 1f),
            _ => throw new InvalidDataException(
                $"COLOR_0 accessor {accessorIndex} uses component type {componentType}."),
        };

        var packed = componentBytes * components;
        var stride = view["byteStride"]?.GetValue<int>() ?? packed;
        var start = binStart + (view["byteOffset"]?.GetValue<int>() ?? 0)
            + (accessor["byteOffset"]?.GetValue<int>() ?? 0);
        var count = accessor["count"]!.GetValue<int>();

        var first = ReadColour(source, start, componentType, components, scale);
        for (var vertex = 1; vertex < count; vertex++)
        {
            var candidate = ReadColour(
                source, start + (vertex * stride), componentType, components, scale);
            if (!candidate.SequenceEqual(first))
            {
                throw new InvalidDataException(
                    $"COLOR_0 accessor {accessorIndex} varies at vertex {vertex}; it cannot be baked "
                    + "into a material factor. This model needs a vertex-colour channel in the "
                    + "renderer instead.");
            }
        }

        return first;
    }

    private static float[] ReadColour(
        byte[] source, int offset, int componentType, int components, float scale)
    {
        var colour = new float[] { 1f, 1f, 1f, 1f };
        for (var channel = 0; channel < components; channel++)
        {
            colour[channel] = componentType switch
            {
                5121 => source[offset + channel] * scale,
                5123 => BinaryPrimitives.ReadUInt16LittleEndian(
                    source.AsSpan(offset + (channel * 2))) * scale,
                _ => BitConverter.ToSingle(source, offset + (channel * 4)),
            };
        }

        return colour;
    }

    /// <summary>One masked island per rectangle, semicolon-separated.</summary>
    private static IReadOnlyList<(float U0, float V0, float U1, float V1)> ParseRects(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        return value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(entry =>
            {
                var parts = entry.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length != 4)
                {
                    throw new ArgumentException("--neutral-rect wants u0,v0,u1,v1[;u0,v0,u1,v1...].");
                }

                var numbers = parts
                    .Select(part => float.Parse(part, System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray();
                // Checked rather than clamped. FlattenRect turns an inverted or out-of-range
                // rectangle into a loop that runs zero times, so the artwork stays in the build and
                // the command reports success — the same silent failure a mistyped material name
                // used to give.
                if (numbers.Any(number => number is < 0f or > 1f)
                    || numbers[0] >= numbers[2] || numbers[1] >= numbers[3])
                {
                    throw new ArgumentException(
                        $"--neutral-rect '{entry}' is not a rectangle in 0..1 with u0 < u1 and v0 < v1.");
                }

                return (numbers[0], numbers[1], numbers[2], numbers[3]);
            })
            .ToArray();
    }

    /// <summary>The colour a masked region takes, as RRGGBB.</summary>
    private static (byte, byte, byte, byte)? ParseFill(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hex = value.TrimStart('#');
        return (
            Convert.ToByte(hex.Substring(0, 2), 16),
            Convert.ToByte(hex.Substring(2, 2), 16),
            Convert.ToByte(hex.Substring(4, 2), 16),
            (byte)255);
    }

    /// <summary>Every material's maps, for a model that keeps its label on a shared atlas.</summary>
    private static Dictionary<int, NeutralImage> ResolveAllMaterialImages(
        JsonObject root,
        JsonArray? textures,
        (byte, byte, byte, byte) baseFill,
        NeutralMaps maps,
        IReadOnlyList<(float, float, float, float)> rects)
    {
        var result = new Dictionary<int, NeutralImage>();
        foreach (var material in root["materials"]?.AsArray() ?? [])
        {
            var node = material!.AsObject();
            var pbr = node["pbrMetallicRoughness"]?.AsObject();
            AddMap(result, textures, maps, NeutralMaps.BaseColour, pbr?["baseColorTexture"], baseFill, rects);
            AddMap(result, textures, maps, NeutralMaps.Surface, pbr?["metallicRoughnessTexture"], (255, 128, 0, 255), rects);
            AddMap(result, textures, maps, NeutralMaps.Surface, node["normalTexture"], (128, 128, 255, 255), rects);
        }

        return result;
    }

    /// <summary>
    /// Images no drawable primitive can reach, so that a dropped mesh takes its maps with it.
    /// </summary>
    /// <remarks>
    /// Conservative by construction: an image counts as orphaned only if a dead material reaches it
    /// and no live one does. The reference walk is generic — anything under a <c>*Texture</c> key,
    /// extensions included — so a map held by an extension this tool does not model is still seen.
    /// Getting it wrong in the safe direction leaves a texture in the file; getting it wrong the
    /// other way blanks a drawn surface, which a render would catch but only if someone looks.
    /// </remarks>
    private static HashSet<int> OrphanedImages(JsonObject root, JsonArray? textures)
    {
        var meshes = root["meshes"]?.AsArray() ?? [];
        var materials = root["materials"]?.AsArray() ?? [];
        var live = new HashSet<int>();
        foreach (var node in root["nodes"]?.AsArray() ?? [])
        {
            if (node!["mesh"]?.GetValue<int>() is not { } meshIndex)
            {
                continue;
            }

            foreach (var primitive in meshes[meshIndex]?["primitives"]?.AsArray() ?? [])
            {
                if (primitive!["material"]?.GetValue<int>() is { } material)
                {
                    live.Add(material);
                }
            }
        }

        var drawn = new HashSet<int>();
        var undrawn = new HashSet<int>();
        for (var index = 0; index < materials.Count; index++)
        {
            foreach (var image in MaterialImages(materials[index]!.AsObject(), textures))
            {
                (live.Contains(index) ? drawn : undrawn).Add(image);
            }
        }

        undrawn.ExceptWith(drawn);
        if (undrawn.Count > 0)
        {
            Console.WriteLine(
                $"  dropped {undrawn.Count} image(s) no drawable mesh reaches: "
                + string.Join(", ", undrawn.Order()));
        }

        return undrawn;
    }

    /// <summary>Every image a material samples, however deeply its texture reference is nested.</summary>
    private static IEnumerable<int> MaterialImages(JsonObject material, JsonArray? textures)
    {
        if (textures is null)
        {
            yield break;
        }

        foreach (var index in TextureReferences(material))
        {
            if (index < textures.Count && textures[index]?["source"]?.GetValue<int>() is { } image)
            {
                yield return image;
            }
        }
    }

    private static IEnumerable<int> TextureReferences(JsonNode? node)
    {
        if (node is not JsonObject value)
        {
            yield break;
        }

        foreach (var (name, child) in value)
        {
            // Matched as an object, not merely by key: indexing a JsonValue throws, and glTF is not
            // required to keep every "*Texture" key an object in an extension this does not model.
            if (child is JsonObject candidate
                && name.EndsWith("Texture", StringComparison.OrdinalIgnoreCase)
                && candidate["index"]?.GetValue<int>() is { } index)
            {
                yield return index;
            }

            foreach (var nested in TextureReferences(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Which maps a neutralize pass touches; all of them unless narrowed.</summary>
    private static NeutralMaps ParseMaps(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => NeutralMaps.All,
            "base" => NeutralMaps.BaseColour,
            "none" => NeutralMaps.None,
            _ => throw new ArgumentException("--neutral-maps wants 'all', 'base' or 'none'."),
        };

    /// <summary>
    /// Flattens one rectangle of the atlas, for a shell whose label shares a map with its body.
    /// </summary>
    /// <remarks>
    /// The rectangle is the fallback, not the preference: it has to be read off a dump by eye and a
    /// wrong one either leaves the artwork in the build or erases moulding. Prefer
    /// <see cref="ResolveNeutralImages"/> wherever a model keeps its label on its own material.
    ///
    /// The fill is grown a few texels beyond the requested rectangle rather than eroded inside it.
    /// The first version eroded, reasoning that mip generation would otherwise smear the fill into
    /// neighbouring islands — true, but it left a ring of the publisher's original artwork around
    /// every masked label, at source resolution, in a shipped binary. Removing the artwork is the
    /// whole purpose; a few texels of the moulding around a label is an acceptable price, and the
    /// caller is expected to draw the rectangle tight for that reason.
    /// </remarks>
    private static void FlattenRect(
        TextureImage image,
        (byte R, byte G, byte B, byte A) fill,
        (float U0, float V0, float U1, float V1) rect)
    {
        // Three texels at the source resolution, which survives the halving down to the runtime
        // size with room to spare, so the requested rectangle is covered to its very edge.
        const int bleed = 3;
        var x0 = Math.Clamp((int)MathF.Floor(rect.U0 * image.Width) - bleed, 0, image.Width);
        var x1 = Math.Clamp((int)MathF.Ceiling(rect.U1 * image.Width) + bleed, 0, image.Width);
        var y0 = Math.Clamp((int)MathF.Floor(rect.V0 * image.Height) - bleed, 0, image.Height);
        var y1 = Math.Clamp((int)MathF.Ceiling(rect.V1 * image.Height) + bleed, 0, image.Height);

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var offset = ((y * image.Width) + x) * 4;
                image.Rgba[offset] = fill.R;
                image.Rgba[offset + 1] = fill.G;
                image.Rgba[offset + 2] = fill.B;
                image.Rgba[offset + 3] = fill.A;
            }
        }
    }

    private static void Flatten(TextureImage image, (byte R, byte G, byte B, byte A) fill)
    {
        for (var offset = 0; offset < image.Rgba.Length; offset += 4)
        {
            image.Rgba[offset] = fill.R;
            image.Rgba[offset + 1] = fill.G;
            image.Rgba[offset + 2] = fill.B;
            image.Rgba[offset + 3] = fill.A;
        }
    }

    private static void WriteGlb(
        JsonObject root, JsonArray views, byte[] source, int binStart,
        Dictionary<int, byte[]> replacements, string outputPath, string inputPath, string flattened)
    {
        using var rebuilt = new MemoryStream();
        for (var index = 0; index < views.Count; index++)
        {
            while (rebuilt.Position % 4 != 0) rebuilt.WriteByte(0);
            var view = views[index]!.AsObject();
            var originalOffset = view["byteOffset"]?.GetValue<int>() ?? 0;
            var originalLength = view["byteLength"]!.GetValue<int>();
            var payload = replacements.TryGetValue(index, out var replacement)
                ? replacement
                : source.AsSpan(binStart + originalOffset, originalLength).ToArray();
            view["byteOffset"] = checked((int)rebuilt.Position);
            view["byteLength"] = payload.Length;
            rebuilt.Write(payload);
        }
        while (rebuilt.Position % 4 != 0) rebuilt.WriteByte(0);

        root["buffers"]!.AsArray()[0]!["byteLength"] = checked((int)rebuilt.Length);
        var asset = root["asset"]!.AsObject();
        var extras = asset["extras"]?.AsObject() ?? new JsonObject();
        asset["extras"] = extras;
        extras["modifiedBy"] = "EmuShelf contributors";
        extras["modifications"] =
            flattened
            + "; maps reduced for the portable runtime; canonical orientation, metric scaling and "
            + "the game label are applied by EmuShelf.";

        var jsonBytes = Encoding.UTF8.GetBytes(
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        var paddedJson = ((jsonBytes.Length + 3) / 4) * 4;
        var paddedBin = checked((int)rebuilt.Length);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var output = File.Create(outputPath);
        Write(output, GlbMagic);
        Write(output, 2);
        Write(output, checked((uint)(12 + 8 + paddedJson + 8 + paddedBin)));
        Write(output, checked((uint)paddedJson));
        Write(output, JsonChunk);
        output.Write(jsonBytes);
        for (var i = jsonBytes.Length; i < paddedJson; i++) output.WriteByte(0x20);
        Write(output, checked((uint)paddedBin));
        Write(output, BinChunk);
        rebuilt.Position = 0;
        rebuilt.CopyTo(output);
        Console.WriteLine(
            $"Prepared {outputPath} from {Path.GetFileName(inputPath)} "
            + $"({new FileInfo(outputPath).Length:N0} bytes)");
    }

    private static void Write(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
