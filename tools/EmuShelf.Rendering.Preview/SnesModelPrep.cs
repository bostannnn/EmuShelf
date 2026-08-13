using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EmuShelf.Rendering.Models;
using StbImageSharp;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// Produces the redistributable runtime derivative of SomeKevin's CC BY PAL cartridge.
/// The downloaded source remains in models/; only this deterministic output is embedded.
/// </summary>
internal static class SnesModelPrep
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinChunk = 0x004E4942;

    public static void Prepare(string inputPath, string outputPath)
    {
        var source = File.ReadAllBytes(inputPath);
        if (source.Length < 28
            || BinaryPrimitives.ReadUInt32LittleEndian(source) != GlbMagic
            || BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(4)) != 2)
        {
            throw new InvalidDataException("SNES source is not a GLB 2.0 file.");
        }

        var jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12)));
        if (BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(16)) != JsonChunk)
        {
            throw new InvalidDataException("SNES GLB does not begin with a JSON chunk.");
        }

        var json = Encoding.UTF8.GetString(source, 20, jsonLength).TrimEnd('\0', ' ');
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("SNES GLB JSON is empty.");
        var binHeader = 20 + jsonLength;
        var binLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(binHeader)));
        if (BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(binHeader + 4)) != BinChunk)
        {
            throw new InvalidDataException("SNES GLB has no binary chunk.");
        }

        var binStart = binHeader + 8;
        if (binStart + binLength > source.Length)
        {
            throw new InvalidDataException("SNES GLB binary chunk extends past the file.");
        }

        var views = root["bufferViews"]?.AsArray()
            ?? throw new InvalidDataException("SNES GLB has no buffer views.");
        var images = root["images"]?.AsArray()
            ?? throw new InvalidDataException("SNES GLB has no images.");
        if (images.Count != 3)
        {
            throw new InvalidDataException(
                $"Expected the reviewed base-colour, metallic/roughness and normal maps; found {images.Count}.");
        }

        var replacements = new Dictionary<int, byte[]>();
        RepairTriangleIndices(root, views, source, binStart, replacements);
        for (var imageIndex = 0; imageIndex < images.Count; imageIndex++)
        {
            var image = images[imageIndex]?.AsObject()
                ?? throw new InvalidDataException($"Image {imageIndex} is missing.");
            var viewIndex = image["bufferView"]?.GetValue<int>()
                ?? throw new InvalidDataException($"Image {imageIndex} is not embedded.");
            var view = views[viewIndex]?.AsObject()
                ?? throw new InvalidDataException($"Image {imageIndex}'s buffer view is missing.");
            var offset = view["byteOffset"]?.GetValue<int>() ?? 0;
            var length = view["byteLength"]?.GetValue<int>()
                ?? throw new InvalidDataException($"Image {imageIndex}'s length is missing.");

            var decoded = ImageResult.FromMemory(
                source.AsSpan(binStart + offset, length).ToArray(),
                ColorComponents.RedGreenBlueAlpha);
            var texture = new TextureImage
            {
                Width = decoded.Width,
                Height = decoded.Height,
                Rgba = decoded.Data,
            };

            RemoveSourcePlaceholder(texture, imageIndex);
            var runtime = GlbLoader.Downsample(texture, maxSize: 1024);
            replacements[viewIndex] = PngWriter.Encode(runtime.Width, runtime.Height, runtime.Rgba);
            image["mimeType"] = "image/png";
        }

        using var rebuiltBin = new MemoryStream();
        for (var index = 0; index < views.Count; index++)
        {
            Pad(rebuiltBin, 4, 0);
            var view = views[index]?.AsObject()
                ?? throw new InvalidDataException($"Buffer view {index} is missing.");
            var originalOffset = view["byteOffset"]?.GetValue<int>() ?? 0;
            var originalLength = view["byteLength"]?.GetValue<int>()
                ?? throw new InvalidDataException($"Buffer view {index}'s length is missing.");
            var payload = replacements.TryGetValue(index, out var replacement)
                ? replacement
                : source.AsSpan(binStart + originalOffset, originalLength).ToArray();

            view["byteOffset"] = checked((int)rebuiltBin.Position);
            view["byteLength"] = payload.Length;
            rebuiltBin.Write(payload);
        }
        Pad(rebuiltBin, 4, 0);

        var buffers = root["buffers"]?.AsArray()
            ?? throw new InvalidDataException("SNES GLB has no buffer declaration.");
        buffers[0]!["byteLength"] = checked((int)rebuiltBin.Length);

        var asset = root["asset"]?.AsObject()
            ?? throw new InvalidDataException("SNES GLB has no asset metadata.");
        var extras = asset["extras"]?.AsObject() ?? new JsonObject();
        asset["extras"] = extras;
        extras["modifiedBy"] = "EmuShelf contributors";
        extras["modifications"] =
            "Placeholder label removed; degenerate triangles removed and inconsistent winding repaired; "
            + "PBR maps reduced to 1024px for the portable runtime; "
            + "canonical orientation, metric scaling and game label are applied by EmuShelf.";

        var jsonBytes = Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false,
        }));
        var paddedJsonLength = Align(jsonBytes.Length, 4);
        var paddedBinLength = checked((int)rebuiltBin.Length);
        var totalLength = checked(12 + 8 + paddedJsonLength + 8 + paddedBinLength);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var output = File.Create(outputPath);
        WriteUInt32(output, GlbMagic);
        WriteUInt32(output, 2);
        WriteUInt32(output, checked((uint)totalLength));
        WriteUInt32(output, checked((uint)paddedJsonLength));
        WriteUInt32(output, JsonChunk);
        output.Write(jsonBytes);
        for (var index = jsonBytes.Length; index < paddedJsonLength; index++)
        {
            output.WriteByte(0x20);
        }
        WriteUInt32(output, checked((uint)paddedBinLength));
        WriteUInt32(output, BinChunk);
        rebuiltBin.Position = 0;
        rebuiltBin.CopyTo(output);
    }

    private static void RemoveSourcePlaceholder(TextureImage image, int imageIndex)
    {
        // Fixed UV-island rectangle in the reviewed SomeKevin source atlas. Work in top-origin
        // image coordinates: it contains only the authored placeholder label. The body, contacts
        // and screws remain driven by the source PBR maps.
        var x0 = (int)MathF.Floor(image.Width * 0.003f);
        var x1 = (int)MathF.Ceiling(image.Width * 0.500f);
        var y0 = (int)MathF.Floor(image.Height * 0.073f);
        var y1 = (int)MathF.Ceiling(image.Height * 0.242f);
        var neutral = imageIndex switch
        {
            0 => (R: (byte)132, G: (byte)132, B: (byte)128, A: (byte)255),
            // glTF packs roughness in G and metallic in B. The label shader overrides roughness,
            // but this neutral dielectric remains safe if the panel is ever disabled.
            1 => (R: (byte)255, G: (byte)128, B: (byte)0, A: (byte)255),
            2 => (R: (byte)128, G: (byte)128, B: (byte)255, A: (byte)255),
            _ => throw new ArgumentOutOfRangeException(nameof(imageIndex)),
        };

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var offset = ((y * image.Width) + x) * 4;
                image.Rgba[offset] = neutral.R;
                image.Rgba[offset + 1] = neutral.G;
                image.Rgba[offset + 2] = neutral.B;
                image.Rgba[offset + 3] = neutral.A;
            }
        }
    }

    private static void RepairTriangleIndices(
        JsonObject root,
        JsonArray views,
        byte[] source,
        int binStart,
        IDictionary<int, byte[]> replacements)
    {
        var accessors = root["accessors"]?.AsArray()
            ?? throw new InvalidDataException("SNES GLB has no accessors.");
        var primitive = root["meshes"]?[0]?["primitives"]?[0]?.AsObject()
            ?? throw new InvalidDataException("SNES GLB has no first mesh primitive.");
        if ((primitive["mode"]?.GetValue<int>() ?? 4) != 4)
        {
            throw new InvalidDataException("SNES source primitive is not a triangle list.");
        }

        var attributes = primitive["attributes"]?.AsObject()
            ?? throw new InvalidDataException("SNES source primitive has no attributes.");
        var position = Accessor(accessors, views, attributes["POSITION"]!.GetValue<int>(), binStart);
        var normal = Accessor(accessors, views, attributes["NORMAL"]!.GetValue<int>(), binStart);
        var indices = Accessor(accessors, views, primitive["indices"]!.GetValue<int>(), binStart);
        if (position.ComponentType != 5126 || position.Type != "VEC3"
            || normal.ComponentType != 5126 || normal.Type != "VEC3"
            || indices.ComponentType != 5125 || indices.Type != "SCALAR")
        {
            throw new InvalidDataException("SNES source uses an unexpected vertex/index encoding.");
        }
        if (indices.AccessorOffset != 0 || indices.Stride != sizeof(uint)
            || indices.ViewLength != indices.Count * sizeof(uint))
        {
            throw new InvalidDataException("SNES index buffer is not an exclusive packed uint view.");
        }

        using var repaired = new MemoryStream(indices.ViewLength);
        for (var offset = 0; offset < indices.Count; offset += 3)
        {
            var a = ReadUInt32(source, indices.Start + (offset * sizeof(uint)));
            var b = ReadUInt32(source, indices.Start + ((offset + 1) * sizeof(uint)));
            var c = ReadUInt32(source, indices.Start + ((offset + 2) * sizeof(uint)));
            var pa = ReadVector3(source, position, a);
            var pb = ReadVector3(source, position, b);
            var pc = ReadVector3(source, position, c);
            var face = Vector3.Cross(pb - pa, pc - pa);
            // The reviewed source's six collapsed faces are below 5e-15 area²; its smallest real
            // bevel begins at 9e-14. Keep the threshold between those groups so detail survives.
            if (face.LengthSquared() < 1e-14f)
            {
                continue;
            }

            var averagedNormal = ReadVector3(source, normal, a)
                + ReadVector3(source, normal, b)
                + ReadVector3(source, normal, c);
            if (Vector3.Dot(face, averagedNormal) < 0f)
            {
                (b, c) = (c, b);
            }

            WriteUInt32(repaired, a);
            WriteUInt32(repaired, b);
            WriteUInt32(repaired, c);
        }

        replacements[indices.ViewIndex] = repaired.ToArray();
        indices.Node["count"] = checked((int)(repaired.Length / sizeof(uint)));
    }

    private static AccessorInfo Accessor(JsonArray accessors, JsonArray views, int index, int binStart)
    {
        var node = accessors[index]?.AsObject()
            ?? throw new InvalidDataException($"Accessor {index} is missing.");
        var viewIndex = node["bufferView"]?.GetValue<int>()
            ?? throw new InvalidDataException($"Accessor {index} has no buffer view.");
        var view = views[viewIndex]?.AsObject()
            ?? throw new InvalidDataException($"Accessor {index}'s buffer view is missing.");
        var viewOffset = view["byteOffset"]?.GetValue<int>() ?? 0;
        var accessorOffset = node["byteOffset"]?.GetValue<int>() ?? 0;
        var componentType = node["componentType"]!.GetValue<int>();
        var type = node["type"]!.GetValue<string>();
        var elementBytes = componentType switch
        {
            5125 or 5126 => sizeof(uint),
            _ => throw new InvalidDataException($"Accessor {index} uses unsupported components."),
        } * (type switch
        {
            "SCALAR" => 1,
            "VEC3" => 3,
            _ => throw new InvalidDataException($"Accessor {index} uses unsupported type {type}."),
        });

        return new AccessorInfo(
            node,
            viewIndex,
            checked(binStart + viewOffset + accessorOffset),
            accessorOffset,
            view["byteStride"]?.GetValue<int>() ?? elementBytes,
            view["byteLength"]!.GetValue<int>(),
            node["count"]!.GetValue<int>(),
            componentType,
            type);
    }

    private static Vector3 ReadVector3(byte[] bytes, AccessorInfo accessor, uint index)
    {
        var offset = checked(accessor.Start + ((int)index * accessor.Stride));
        return new Vector3(
            BitConverter.ToSingle(bytes, offset),
            BitConverter.ToSingle(bytes, offset + sizeof(float)),
            BitConverter.ToSingle(bytes, offset + (2 * sizeof(float))));
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) / alignment * alignment);

    private static void Pad(Stream stream, int alignment, byte value)
    {
        while (stream.Position % alignment != 0)
        {
            stream.WriteByte(value);
        }
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private sealed record AccessorInfo(
        JsonObject Node,
        int ViewIndex,
        int Start,
        int AccessorOffset,
        int Stride,
        int ViewLength,
        int Count,
        int ComponentType,
        string Type);
}
