using System.Buffers.Binary;
using System.IO.Compression;

namespace EmuShelf.Infrastructure.Tests.Metadata;

/// <summary>
/// Wraps a cooked 2048-byte-sector ISO image in a CSO (deflate) or ZSO (lz4) container so
/// tests can exercise the compressed-image serial read end to end.
/// </summary>
internal static class CompressedIsoBuilder
{
    private const int BlockSize = 2048;
    private const int HeaderSize = 0x18;

    public static byte[] BuildCso(byte[] iso) => Build(iso, "CISO"u8, RawDeflate);

    public static byte[] BuildZso(byte[] iso) => Build(iso, "ZISO"u8, EncodeLz4Literals);

    /// <summary>A valid LZ4 block that stores <paramref name="data"/> as a single literal run.</summary>
    public static byte[] EncodeLz4Literals(byte[] data)
    {
        var output = new List<byte> { (byte)((data.Length >= 15 ? 15 : data.Length) << 4) };
        if (data.Length >= 15)
        {
            var remaining = data.Length - 15;
            while (remaining >= 255)
            {
                output.Add(255);
                remaining -= 255;
            }
            output.Add((byte)remaining);
        }
        output.AddRange(data);
        return output.ToArray();
    }

    private static byte[] Build(byte[] iso, ReadOnlySpan<byte> magic, Func<byte[], byte[]> compress)
    {
        var blocks = iso.Length / BlockSize;
        var index = new uint[blocks + 1];
        using var body = new MemoryStream();
        long position = HeaderSize + (blocks + 1) * 4;

        for (var block = 0; block < blocks; block++)
        {
            var blockData = iso[(block * BlockSize)..((block + 1) * BlockSize)];
            var compressed = compress(blockData);
            if (compressed.Length < BlockSize)
            {
                index[block] = (uint)position;
                body.Write(compressed);
                position += compressed.Length;
            }
            else
            {
                index[block] = (uint)position | 0x80000000u;
                body.Write(blockData);
                position += BlockSize;
            }
        }
        index[blocks] = (uint)position;

        var result = new byte[position];
        magic.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x04), HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(result.AsSpan(0x08), (ulong)iso.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0x10), BlockSize);
        result[0x14] = 1; // version
        result[0x15] = 0; // index_shift
        for (var entry = 0; entry <= blocks; entry++)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(HeaderSize + entry * 4), index[entry]);
        body.ToArray().CopyTo(result, HeaderSize + (blocks + 1) * 4);
        return result;
    }

    private static byte[] RawDeflate(byte[] data)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return buffer.ToArray();
    }
}
