using System.Buffers.Binary;

namespace EmuShelf.Infrastructure.Tests.Metadata;

/// <summary>
/// Wraps a cooked 2048-byte-sector ISO image in a DVD-geometry CHD v5 container so tests can
/// exercise the CHD read path without a chdman install. Every hunk is stored uncompressed
/// (COMPRESSION_NONE), which is a shape chdman itself emits for incompressible hunks, so the
/// header, the Huffman-coded hunk map, and its CRC-16 self-check are all real and are decoded by
/// the production reader exactly as they are for a chdman-produced file. The committed
/// chdman fixtures in <c>Fixtures/Chd</c> remain the byte-exactness proof for the codec paths.
/// </summary>
internal static class ChdImageBuilder
{
    private const int SectorSize = 2048;
    private const int HeaderBytes = 124;
    private const int HunkBytes = SectorSize * 2;
    private const int MapEntryBytes = 12;
    private const byte TypeNone = 4;

    private static readonly ushort[] Crc16Table = BuildCrc16Table();

    public static byte[] BuildDvdChd(byte[] iso)
    {
        var hunkCount = (iso.Length + HunkBytes - 1) / HunkBytes;
        var hunkData = new byte[hunkCount * HunkBytes];
        iso.CopyTo(hunkData, 0);

        var rawMap = new byte[hunkCount * MapEntryBytes];
        for (var hunk = 0; hunk < hunkCount; hunk++)
        {
            var entry = rawMap.AsSpan(hunk * MapEntryBytes, MapEntryBytes);
            entry[0] = TypeNone;
            entry[1] = (byte)(HunkBytes >> 16);
            entry[2] = (byte)(HunkBytes >> 8);
            entry[3] = unchecked((byte)HunkBytes);
            WriteUInt48BigEndian(entry[4..10], (ulong)(HeaderBytes + (long)hunk * HunkBytes));
            // The reader keeps the per-hunk CRC in the map but never verifies it against the
            // hunk, so leaving it zero still produces a map whose own CRC-16 checks out.
        }

        var map = EncodeMap(hunkCount);
        var mapOffset = HeaderBytes + hunkData.Length;
        var file = new byte[mapOffset + 16 + map.Length];

        "MComprHD"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8, 4), HeaderBytes);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(12, 4), 5);
        // Hunks are stored, but a v5 CHD is only treated as compressed when a codec is declared.
        "zlib"u8.CopyTo(file.AsSpan(16, 4));
        BinaryPrimitives.WriteUInt64BigEndian(file.AsSpan(32, 8), (ulong)hunkData.Length);
        BinaryPrimitives.WriteUInt64BigEndian(file.AsSpan(40, 8), (ulong)mapOffset);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(56, 4), HunkBytes);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(60, 4), SectorSize);

        hunkData.CopyTo(file, HeaderBytes);

        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(mapOffset, 4), (uint)map.Length);
        WriteUInt48BigEndian(file.AsSpan(mapOffset + 4, 6), HeaderBytes);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(mapOffset + 10, 2), Crc16(rawMap));
        map.CopyTo(file, mapOffset + 16);
        return file;
    }

    /// <summary>
    /// Emits the map bitstream in the reader's two passes: a 16-symbol Huffman tree where every
    /// symbol is four bits wide (so symbol <c>n</c> encodes as the nibble <c>n</c>), then one
    /// compression-type symbol per hunk, then the 16-bit CRC each COMPRESSION_NONE entry carries.
    /// The passes are sequential, not interleaved.
    /// </summary>
    private static byte[] EncodeMap(int hunkCount)
    {
        var bits = new BitWriter();
        for (var symbol = 0; symbol < 16; symbol++)
            bits.Write(4, 4); // node bit length, in the tree's 4-bit RLE encoding
        for (var hunk = 0; hunk < hunkCount; hunk++)
            bits.Write(TypeNone, 4);
        for (var hunk = 0; hunk < hunkCount; hunk++)
            bits.Write(0, 16); // COMPRESSION_NONE carries only a CRC; length and offset are implied
        return bits.ToArray();
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private uint _buffer;
        private int _bits;

        public void Write(uint value, int numBits)
        {
            _buffer = (_buffer << numBits) | (value & ((1u << numBits) - 1));
            _bits += numBits;
            while (_bits >= 8)
            {
                _bits -= 8;
                _bytes.Add((byte)(_buffer >> _bits));
            }
        }

        public byte[] ToArray()
        {
            if (_bits > 0)
                Write(0, 8 - _bits);
            // The MSB-first reader fills its 32-bit window ahead of the bits it consumes, so
            // pad the stream to keep the final symbols away from its overflow check.
            return [.. _bytes, 0, 0, 0, 0];
        }
    }

    private static void WriteUInt48BigEndian(Span<byte> destination, ulong value)
    {
        for (var i = 0; i < 6; i++)
            destination[i] = (byte)(value >> (8 * (5 - i)));
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xffff;
        foreach (var b in data)
            crc = (ushort)((crc << 8) ^ Crc16Table[(crc >> 8) ^ b]);
        return crc;
    }

    private static ushort[] BuildCrc16Table()
    {
        var table = new ushort[256];
        for (var i = 0; i < 256; i++)
        {
            var crc = (ushort)(i << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
            table[i] = crc;
        }
        return table;
    }
}
