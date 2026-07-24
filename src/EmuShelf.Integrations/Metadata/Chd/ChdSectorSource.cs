using System.Buffers.Binary;
using System.IO.Compression;
using EmuShelf.Integrations.Achievements;
using Shamisen.Codecs.Flac;
using Shamisen.Data;

namespace EmuShelf.Integrations.Metadata.Chd;

/// <summary>
/// Reads logical 2048-byte sectors from a compressed CHD (v5) image by decoding its
/// Huffman-coded hunk map and decompressing only the hunks that back the requested
/// sectors. Ported from MAME/libchdr and verified against chdman-produced vectors.
/// Supports DVD-geometry images (2048-byte units: zlib/LZMA) and CD-geometry images
/// (2352/2448-byte frames: cdzl/cdlz/cdfl); other codecs and parents fall back to the caller.
/// </summary>
internal sealed class ChdSectorSource : ILogicalSectorReader, IDisposable
{
    private const int SectorSize = 2048;
    private const int CdSectorData = 2352;
    private const int CdSubcodeData = 96;
    private const int MapEntryBytes = 12;
    private const int MaxSelfDepth = 8;
    private static ReadOnlySpan<byte> RawCdSyncPattern =>
        [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    private const byte TypeNone = 4;
    private const byte TypeSelf = 5;
    private const byte TypeParent = 6;

    private static readonly ushort[] Crc16Table = BuildCrc16Table();

    private readonly FileStream _stream;
    private readonly string[] _compressors;
    private readonly long _logicalBytes;
    private readonly uint _hunkBytes;
    private readonly uint _unitBytes;
    private readonly bool _isCd;
    private readonly int _hunkCount;
    private readonly byte[] _rawMap;
    private readonly ChdLzmaDecoder _lzma;

    private long _cachedHunkIndex = -1;
    private byte[]? _cachedHunk;

    public int FirstTrackSector => 0;

    private ChdSectorSource(
        FileStream stream,
        string[] compressors,
        long logicalBytes,
        uint hunkBytes,
        uint unitBytes,
        int hunkCount,
        byte[] rawMap)
    {
        _stream = stream;
        _compressors = compressors;
        _logicalBytes = logicalBytes;
        _hunkBytes = hunkBytes;
        _unitBytes = unitBytes;
        _isCd = unitBytes != SectorSize;
        _hunkCount = hunkCount;
        _rawMap = rawMap;
        _lzma = new ChdLzmaDecoder(hunkBytes);
    }

    public static ChdSectorSource? TryOpen(string path)
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess);

            Span<byte> header = stackalloc byte[124];
            if (stream.Length < header.Length)
                return DisposeAndNull(stream);
            stream.Position = 0;
            stream.ReadExactly(header);

            if (!header[..8].SequenceEqual("MComprHD"u8) ||
                BinaryPrimitives.ReadUInt32BigEndian(header.Slice(12, 4)) != 5)
                return DisposeAndNull(stream);

            var compressors = new string[4];
            for (var i = 0; i < 4; i++)
                compressors[i] = System.Text.Encoding.ASCII.GetString(header.Slice(16 + i * 4, 4));

            var logicalBytes = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(32, 8));
            var mapOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(40, 8));
            var hunkBytes = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(56, 4));
            var unitBytes = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(60, 4));

            // Cooked DVD sectors (2048) or CD frames (2352 raw / 2448 with subcode).
            var validUnit = unitBytes is SectorSize or CdSectorData or CdSectorData + CdSubcodeData;
            if (compressors[0] == "\0\0\0\0" || logicalBytes <= 0 ||
                hunkBytes == 0 || !validUnit || hunkBytes % unitBytes != 0)
                return DisposeAndNull(stream);

            var hunkCount = (int)((logicalBytes + hunkBytes - 1) / hunkBytes);
            var rawMap = TryDecodeMap(stream, mapOffset, hunkCount, hunkBytes);
            if (rawMap is null)
                return DisposeAndNull(stream);

            return new ChdSectorSource(
                stream, compressors, logicalBytes, hunkBytes, unitBytes, hunkCount, rawMap);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or EndOfStreamException)
        {
            return stream is null ? null : DisposeAndNull(stream);
        }
    }

    public int ReadSector(uint sector, Span<byte> destination)
    {
        if (destination.Length > SectorSize)
            return 0;

        if (!_isCd)
        {
            var imageOffset = (long)sector * SectorSize;
            if (imageOffset + destination.Length > _logicalBytes)
                return 0;
            var hunk = ReadHunk((int)(imageOffset / _hunkBytes), 0);
            if (hunk is null)
                return 0;
            hunk.AsSpan((int)(imageOffset % _hunkBytes), destination.Length).CopyTo(destination);
            return destination.Length;
        }

        // CD: cdzl/cdlz chunks can hold full raw frames or cooked user data padded to a CD-frame
        // unit. Only raw frames have the 16/24-byte header; treating a cooked frame as raw drops
        // the beginning of SYSTEM.CNF (including BOOT2) and makes valid PS2 CD images look invalid.
        var frameOffset = (long)sector * _unitBytes;
        if (frameOffset + CdSectorData > _logicalBytes)
            return 0;
        var cdHunk = ReadHunk((int)(frameOffset / _hunkBytes), 0);
        if (cdHunk is null)
            return 0;
        var frameByte = (int)(frameOffset % _hunkBytes);
        if (frameByte < 0 || frameByte + CdSectorData > cdHunk.Length)
            return 0;

        var frame = cdHunk.AsSpan(frameByte, CdSectorData);
        var start = frameByte + GetCdUserDataOffset(frame);
        if (start + destination.Length > cdHunk.Length)
            return 0;
        cdHunk.AsSpan(start, destination.Length).CopyTo(destination);
        return destination.Length;
    }

    internal static int GetCdUserDataOffset(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 24)
            return 0;

        if (frame[..RawCdSyncPattern.Length].SequenceEqual(RawCdSyncPattern))
            return frame[15] == 2 ? 24 : 16;

        if (!frame[..12].SequenceEqual(stackalloc byte[12]) ||
            frame[15] != 2 ||
            !IsBcd(frame[12], 99) ||
            !IsBcd(frame[13], 59) ||
            !IsBcd(frame[14], 74) ||
            !frame.Slice(16, 4).SequenceEqual(frame.Slice(20, 4)))
        {
            return 0;
        }

        return 24;
    }

    private static bool IsBcd(byte value, int maximum) =>
        (value >> 4) <= 9 && (value & 0x0F) <= 9 &&
        ((value >> 4) * 10 + (value & 0x0F)) <= maximum;

    private byte[]? ReadHunk(int hunkIndex, int depth)
    {
        if (hunkIndex < 0 || hunkIndex >= _hunkCount || depth > MaxSelfDepth)
            return null;
        if (hunkIndex == _cachedHunkIndex)
            return _cachedHunk;

        var entry = _rawMap.AsSpan(hunkIndex * MapEntryBytes, MapEntryBytes);
        var type = entry[0];
        var length = (uint)((entry[1] << 16) | (entry[2] << 8) | entry[3]);
        var offset = (long)ReadUInt48BigEndian(entry[4..10]);

        byte[]? hunk;
        switch (type)
        {
            case 0 or 1 or 2 or 3:
                hunk = DecodeCompressedHunk(_compressors[type], offset, length);
                break;
            case TypeNone:
                hunk = ReadRaw(offset, (int)_hunkBytes);
                break;
            case TypeSelf:
                return ReadHunk((int)offset, depth + 1);
            case TypeParent:
            default:
                return null;
        }

        if (hunk is null)
            return null;
        _cachedHunkIndex = hunkIndex;
        _cachedHunk = hunk;
        return hunk;
    }

    private byte[]? DecodeCompressedHunk(string codec, long offset, uint length)
    {
        var input = ReadRaw(offset, (int)length);
        if (input is null)
            return null;

        return codec switch
        {
            "zlib" => Inflate(input, 0, input.Length, (int)_hunkBytes),
            "lzma" => DecodeLzma(input, 0, input.Length, (int)_hunkBytes),
            "cdzl" or "cdlz" => DecodeCdHunk(codec, input),
            "cdfl" => DecodeCdFlacHunk(input),
            _ => null,
        };
    }

    private byte[]? DecodeCdFlacHunk(byte[] input)
    {
        try
        {
            var frames = checked((int)(_hunkBytes / _unitBytes));
            var samplesPerChannel = checked(frames * CdSectorData / 4);
            var samples = new int[checked(samplesPerChannel * 2)];
            using var source = new StreamDataSource(new MemoryStream(
                BuildCdFlacStream(input, samplesPerChannel), writable: false));
            using var parser = new FlacParser(source);
            // FlacParser normalizes decoded PCM samples to 32-bit integers, even for the
            // 16-bit FLAC stream written by chdman. The encoded bit depth is therefore
            // not represented by the output format here.
            if (parser.Format is not { SampleRate: 44100, Channels: 2 })
                return null;

            var written = 0;
            while (written < samples.Length)
            {
                var read = parser.Read(samples.AsSpan(written)).Length;
                if (read <= 0)
                    return null;
                written += read;
            }

            var decoded = new byte[checked(frames * CdSectorData)];
            for (var index = 0; index < samples.Length; index++)
            {
                if (samples[index] is < short.MinValue or > short.MaxValue)
                    return null;
                // libchdr asks its FLAC decoder to byte-swap samples on little-endian
                // hosts before placing them back into the CD frame. Shamisen exposes
                // the decoded sample value, so restore the CHD's on-disk byte order.
                BinaryPrimitives.WriteInt16BigEndian(
                    decoded.AsSpan(index * sizeof(short), sizeof(short)),
                    (short)samples[index]);
            }

            return ReassembleCdFrames(decoded, frames, subcode: null);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or
                                   EndOfStreamException or IOException or OverflowException or
                                   FlacException)
        {
            return null;
        }
    }

    private byte[]? DecodeCdHunk(string codec, byte[] input)
    {
        var frames = (int)(_hunkBytes / _unitBytes);
        var subcodeSize = (int)_unitBytes - CdSectorData; // 0 or 96
        var complenBytes = _hunkBytes < 65536 ? 2 : 3;
        var eccBytes = (frames + 7) / 8;
        var headerBytes = eccBytes + complenBytes;
        if (input.Length < headerBytes)
            return null;

        var complenBase = 0;
        for (var i = 0; i < complenBytes; i++)
            complenBase = (complenBase << 8) | input[eccBytes + i];
        if (complenBase < 0 || headerBytes + complenBase > input.Length)
            return null;

        var sectors = codec == "cdlz"
            ? DecodeLzma(input, headerBytes, complenBase, frames * CdSectorData)
            : Inflate(input, headerBytes, complenBase, frames * CdSectorData);
        if (sectors is null)
            return null;

        byte[]? subcode = null;
        if (subcodeSize > 0)
        {
            var subOffset = headerBytes + complenBase;
            subcode = Inflate(input, subOffset, input.Length - subOffset, frames * subcodeSize);
            if (subcode is null)
                return null;
        }

        return ReassembleCdFrames(sectors, frames, subcode);
    }

    // Sync/ECC are intentionally not regenerated: the 2048 user bytes this reader returns are
    // unaffected by them. cdfl's trailing compressed subcode is not needed for logical sectors.
    private byte[] ReassembleCdFrames(byte[] sectors, int frames, byte[]? subcode)
    {
        var subcodeSize = checked((int)_unitBytes - CdSectorData);
        var hunk = new byte[_hunkBytes];
        for (var frame = 0; frame < frames; frame++)
        {
            Array.Copy(sectors, frame * CdSectorData, hunk, frame * _unitBytes, CdSectorData);
            if (subcode is not null)
                Array.Copy(
                    subcode, frame * subcodeSize,
                    hunk, frame * (int)_unitBytes + CdSectorData, subcodeSize);
        }
        return hunk;
    }

    private static byte[] BuildCdFlacStream(byte[] compressed, int samplesPerChannel)
    {
        const int StreamInfoSize = 34;
        var blockSize = GetCdFlacBlockSize(samplesPerChannel * 4);
        var stream = new byte[checked(4 + 4 + StreamInfoSize + compressed.Length)];
        "fLaC"u8.CopyTo(stream);
        stream[4] = 0x80; // Final metadata block: STREAMINFO.
        stream[7] = StreamInfoSize;
        BinaryPrimitives.WriteUInt16BigEndian(stream.AsSpan(8, 2), checked((ushort)blockSize));
        BinaryPrimitives.WriteUInt16BigEndian(stream.AsSpan(10, 2), checked((ushort)blockSize));
        var streamInfo = ((ulong)44100 << 44) | ((ulong)1 << 41) |
                         ((ulong)15 << 36) | (uint)samplesPerChannel;
        BinaryPrimitives.WriteUInt64BigEndian(stream.AsSpan(18, 8), streamInfo);
        compressed.CopyTo(stream, 4 + 4 + StreamInfoSize);
        return stream;
    }

    private static int GetCdFlacBlockSize(int byteCount)
    {
        var blockSize = byteCount / 4;
        while (blockSize > 2048)
            blockSize /= 2;
        return blockSize;
    }

    private static byte[]? Inflate(byte[] input, int offset, int length, int outputSize)
    {
        try
        {
            var output = new byte[outputSize];
            using var deflate = new DeflateStream(
                new MemoryStream(input, offset, length, writable: false),
                CompressionMode.Decompress);
            deflate.ReadExactly(output, 0, outputSize);
            return output;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or
                                   ArgumentException or IOException)
        {
            return null;
        }
    }

    private byte[]? DecodeLzma(byte[] input, int offset, int length, int outputSize)
    {
        if (offset < 0 || length < 0 || offset + length > input.Length)
            return null;
        var slice = new byte[length];
        Array.Copy(input, offset, slice, 0, length);
        var output = new byte[outputSize];
        return _lzma.TryDecompress(slice, output) ? output : null;
    }

    private byte[]? ReadRaw(long offset, int length)
    {
        if (offset < 0 || length < 0 || offset + length > _stream.Length)
            return null;
        var buffer = new byte[length];
        _stream.Position = offset;
        _stream.ReadExactly(buffer);
        return buffer;
    }

    private static byte[]? TryDecodeMap(FileStream stream, long mapOffset, int hunkCount, uint hunkBytes)
    {
        Span<byte> mapHeader = stackalloc byte[16];
        if (mapOffset < 0 || mapOffset + mapHeader.Length > stream.Length)
            return null;
        stream.Position = mapOffset;
        stream.ReadExactly(mapHeader);

        var mapBytes = BinaryPrimitives.ReadUInt32BigEndian(mapHeader[..4]);
        var firstOffset = ReadUInt48BigEndian(mapHeader.Slice(4, 6));
        var mapCrc = BinaryPrimitives.ReadUInt16BigEndian(mapHeader.Slice(10, 2));
        int lengthBits = mapHeader[12];
        int selfBits = mapHeader[13];
        int parentBits = mapHeader[14];

        if (mapOffset + 16 + mapBytes > stream.Length)
            return null;
        var compressed = new byte[mapBytes];
        stream.Position = mapOffset + 16;
        stream.ReadExactly(compressed);

        var bits = new ChdBitReader(compressed);
        var decoder = new ChdHuffmanDecoder(16, 8);
        if (!decoder.TryImportTreeRle(bits))
            return null;

        var rawMap = new byte[hunkCount * MapEntryBytes];

        // Pass 1: compression types, RLE-expanded.
        var repeat = 0;
        byte lastComp = 0;
        for (var hunk = 0; hunk < hunkCount; hunk++)
        {
            if (repeat > 0)
            {
                rawMap[hunk * MapEntryBytes] = lastComp;
                repeat--;
                continue;
            }
            if (bits.Overflow)
                return null;
            var value = decoder.DecodeOne(bits);
            switch (value)
            {
                case 7: // COMPRESSION_RLE_SMALL
                    rawMap[hunk * MapEntryBytes] = lastComp;
                    repeat = 2 + (int)decoder.DecodeOne(bits);
                    break;
                case 8: // COMPRESSION_RLE_LARGE
                    rawMap[hunk * MapEntryBytes] = lastComp;
                    repeat = 2 + 16 + ((int)decoder.DecodeOne(bits) << 4);
                    repeat += (int)decoder.DecodeOne(bits);
                    break;
                default:
                    rawMap[hunk * MapEntryBytes] = lastComp = (byte)value;
                    break;
            }
        }

        // Pass 2: per-hunk length/offset/crc.
        var currentOffset = firstOffset;
        uint lastSelf = 0;
        ulong lastParent = 0;
        for (var hunk = 0; hunk < hunkCount; hunk++)
        {
            var entry = rawMap.AsSpan(hunk * MapEntryBytes, MapEntryBytes);
            var offset = currentOffset;
            uint length = 0;
            ushort crc = 0;
            switch (entry[0])
            {
                case 0 or 1 or 2 or 3:
                    length = bits.Read(lengthBits);
                    currentOffset += length;
                    crc = (ushort)bits.Read(16);
                    break;
                case TypeNone:
                    length = hunkBytes;
                    currentOffset += length;
                    crc = (ushort)bits.Read(16);
                    break;
                case TypeSelf:
                    offset = lastSelf = bits.Read(selfBits);
                    break;
                case TypeParent:
                    offset = bits.Read(parentBits);
                    lastParent = offset;
                    break;
                case 10: // COMPRESSION_SELF_1
                    lastSelf++;
                    goto case 9;
                case 9:  // COMPRESSION_SELF_0
                    entry[0] = TypeSelf;
                    offset = lastSelf;
                    break;
                case 11: // COMPRESSION_PARENT_SELF
                    entry[0] = TypeParent;
                    lastParent = offset = (ulong)hunk * hunkBytes / SectorSize;
                    break;
                case 13: // COMPRESSION_PARENT_1
                    lastParent += hunkBytes / SectorSize;
                    goto case 12;
                case 12: // COMPRESSION_PARENT_0
                    entry[0] = TypeParent;
                    offset = lastParent;
                    break;
            }
            entry[1] = (byte)(length >> 16);
            entry[2] = (byte)(length >> 8);
            entry[3] = (byte)length;
            WriteUInt48BigEndian(entry[4..10], offset);
            BinaryPrimitives.WriteUInt16BigEndian(entry.Slice(10, 2), crc);
        }

        return Crc16(rawMap) == mapCrc ? rawMap : null;
    }

    private static ChdSectorSource? DisposeAndNull(FileStream stream)
    {
        stream.Dispose();
        return null;
    }

    private static ulong ReadUInt48BigEndian(ReadOnlySpan<byte> value)
    {
        ulong result = 0;
        for (var i = 0; i < 6; i++)
            result = (result << 8) | value[i];
        return result;
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

    public void Dispose() => _stream.Dispose();
}
