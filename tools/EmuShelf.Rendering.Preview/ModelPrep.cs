using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EmuShelf.Rendering.Models;
using StbImageSharp;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Produces a redistributable runtime derivative of a downloaded shell by flattening the artwork of
/// one named material and reducing every map to the portable runtime size.
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
        BaseColour = 1 << 0,
        Surface = 1 << 1,
        All = BaseColour | Surface,
    }

    public static void Prepare(
        string inputPath,
        string outputPath,
        string? neutralMaterial,
        string? neutralRect,
        string? neutralFill,
        string? neutralMaps,
        bool singleInstance,
        bool bakeVertexColours,
        int maxTextureSize)
    {
        var rect = ParseRect(neutralRect);
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

        if (singleInstance)
        {
            KeepOneInstance(root);
        }

        if (bakeVertexColours)
        {
            BakeConstantVertexColours(root, source, binStart);
        }

        var views = root["bufferViews"]!.AsArray();
        var images = root["images"]!.AsArray();
        var textures = root["textures"]?.AsArray();

        var neutralImages = neutralMaterial is null
            ? ResolveAllMaterialImages(root, textures, baseFill, maps)
            : ResolveNeutralImages(root, textures, neutralMaterial, baseFill, maps);
        if (neutralImages.Count == 0)
        {
            throw new InvalidDataException(
                $"Nothing to neutralize: no material named '{neutralMaterial}' with a base-colour texture.");
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

            var decoded = ImageResult.FromMemory(
                source.AsSpan(binStart + offset, length).ToArray(), ColorComponents.RedGreenBlueAlpha);
            var texture = new TextureImage
            {
                Width = decoded.Width,
                Height = decoded.Height,
                Rgba = decoded.Data,
            };

            if (neutralImages.TryGetValue(imageIndex, out var fill))
            {
                if (rect is { } island)
                {
                    FlattenRect(texture, fill, island);
                }
                else
                {
                    Flatten(texture, fill);
                }
            }

            var runtime = GlbLoader.Downsample(texture, maxTextureSize);
            replacements[viewIndex] = PngWriter.Encode(runtime.Width, runtime.Height, runtime.Rgba);
            image["mimeType"] = "image/png";
        }

        var flattened = neutralMaterial is null
            ? "The label region of the shared atlas is flattened"
            : $"The '{neutralMaterial}' material's artwork is flattened";
        var scope = maps == NeutralMaps.BaseColour
            ? " out of the base-colour map, leaving the model's own surface maps intact"
            : " to a blank label";
        WriteGlb(root, views, source, binStart, replacements, outputPath, inputPath, flattened + scope);
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
    private static Dictionary<int, (byte R, byte G, byte B, byte A)> ResolveNeutralImages(
        JsonObject root,
        JsonArray? textures,
        string materialName,
        (byte, byte, byte, byte) baseFill,
        NeutralMaps maps)
    {
        var result = new Dictionary<int, (byte, byte, byte, byte)>();
        foreach (var material in root["materials"]?.AsArray() ?? [])
        {
            var node = material!.AsObject();
            if (!string.Equals(node["name"]?.GetValue<string>(), materialName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pbr = node["pbrMetallicRoughness"]?.AsObject();
            AddMap(result, textures, maps, NeutralMaps.BaseColour, pbr?["baseColorTexture"], baseFill);
            AddMap(result, textures, maps, NeutralMaps.Surface, pbr?["metallicRoughnessTexture"], (255, 128, 0, 255));
            AddMap(result, textures, maps, NeutralMaps.Surface, node["normalTexture"], (128, 128, 255, 255));
        }

        return result;
    }

    private static void AddMap(
        Dictionary<int, (byte, byte, byte, byte)> result,
        JsonArray? textures,
        NeutralMaps maps,
        NeutralMaps required,
        JsonNode? reference,
        (byte, byte, byte, byte) fill)
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
        if (imageIndex is not null)
        {
            result[imageIndex.Value] = fill;
        }
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

    private static (float U0, float V0, float U1, float V1)? ParseRect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) throw new ArgumentException("--neutral-rect wants u0,v0,u1,v1.");
        var numbers = parts
            .Select(part => float.Parse(part, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        return (numbers[0], numbers[1], numbers[2], numbers[3]);
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
    private static Dictionary<int, (byte R, byte G, byte B, byte A)> ResolveAllMaterialImages(
        JsonObject root, JsonArray? textures, (byte, byte, byte, byte) baseFill, NeutralMaps maps)
    {
        var result = new Dictionary<int, (byte, byte, byte, byte)>();
        foreach (var material in root["materials"]?.AsArray() ?? [])
        {
            var node = material!.AsObject();
            var pbr = node["pbrMetallicRoughness"]?.AsObject();
            AddMap(result, textures, maps, NeutralMaps.BaseColour, pbr?["baseColorTexture"], baseFill);
            AddMap(result, textures, maps, NeutralMaps.Surface, pbr?["metallicRoughnessTexture"], (255, 128, 0, 255));
            AddMap(result, textures, maps, NeutralMaps.Surface, node["normalTexture"], (128, 128, 255, 255));
        }

        return result;
    }

    /// <summary>Which maps a neutralize pass touches; all three unless narrowed.</summary>
    private static NeutralMaps ParseMaps(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => NeutralMaps.All,
            "base" => NeutralMaps.BaseColour,
            _ => throw new ArgumentException("--neutral-maps wants 'all' or 'base'."),
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
