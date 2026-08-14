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

    public static void Prepare(
        string inputPath, string outputPath, string neutralMaterial, int maxTextureSize)
    {
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

        var views = root["bufferViews"]!.AsArray();
        var images = root["images"]!.AsArray();
        var textures = root["textures"]?.AsArray();

        var neutralImages = ResolveNeutralImages(root, textures, neutralMaterial);
        if (neutralImages.Count == 0)
        {
            throw new InvalidDataException(
                $"No material named '{neutralMaterial}' with a base-colour texture; nothing would be neutralized.");
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
                Flatten(texture, fill);
            }

            var runtime = GlbLoader.Downsample(texture, maxTextureSize);
            replacements[viewIndex] = PngWriter.Encode(runtime.Width, runtime.Height, runtime.Rgba);
            image["mimeType"] = "image/png";
        }

        WriteGlb(root, views, source, binStart, replacements, outputPath, inputPath, neutralMaterial);
    }

    /// <summary>
    /// The images that carry the named material's artwork, and the flat value each should take.
    /// </summary>
    /// <remarks>
    /// Base colour becomes a neutral paper grey; metallic/roughness keeps glTF's channel meaning
    /// (roughness in G, metalness in B) so a disabled panel still shades as a dielectric; normal
    /// becomes flat. Neutralizing all three matters — leaving the label's normal map behind would
    /// emboss the removed artwork into EmuShelf's own.
    /// </remarks>
    private static Dictionary<int, (byte R, byte G, byte B, byte A)> ResolveNeutralImages(
        JsonObject root, JsonArray? textures, string materialName)
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
            Add(pbr?["baseColorTexture"], (214, 212, 206, 255));
            Add(pbr?["metallicRoughnessTexture"], (255, 128, 0, 255));
            Add(node["normalTexture"], (128, 128, 255, 255));
        }

        return result;

        void Add(JsonNode? reference, (byte, byte, byte, byte) fill)
        {
            var textureIndex = reference?["index"]?.GetValue<int>();
            if (textureIndex is null || textures is null) return;
            var imageIndex = textures[textureIndex.Value]?["source"]?.GetValue<int>();
            if (imageIndex is not null) result[imageIndex.Value] = fill;
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
        Dictionary<int, byte[]> replacements, string outputPath, string inputPath, string neutralMaterial)
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
            $"The '{neutralMaterial}' material's artwork is flattened to a blank label; "
            + "maps reduced for the portable runtime; canonical orientation, metric scaling and the "
            + "game label are applied by EmuShelf.";

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
