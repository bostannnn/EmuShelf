using System.Buffers.Binary;
using System.IO.Compression;
using EmuShelf.Integrations.Achievements;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Exposes the logical 2048-byte sectors of a CSO (deflate) or ZSO (lz4) compressed ISO
/// so the shared ISO9660 reader can locate SYSTEM.CNF. Only the blocks that back the
/// requested sectors are decompressed; the rest of the image is never touched.
/// </summary>
internal sealed class CompressedIsoSectorSource : ILogicalSectorReader, IDisposable
{
    private const int SectorSize = 2048;
    private const int HeaderSize = 0x18;
    private const long MaximumBlockBytes = 4 * 1024 * 1024;

    private readonly FileStream _stream;
    private readonly bool _isLz4;
    private readonly uint _blockSize;
    private readonly int _indexShift;
    private readonly long _uncompressedSize;
    private readonly byte[] _blockBuffer;
    private long _cachedBlockIndex = -1;

    public int FirstTrackSector => 0;

    private CompressedIsoSectorSource(
        FileStream stream,
        bool isLz4,
        uint blockSize,
        int indexShift,
        long uncompressedSize)
    {
        _stream = stream;
        _isLz4 = isLz4;
        _blockSize = blockSize;
        _indexShift = indexShift;
        _uncompressedSize = uncompressedSize;
        _blockBuffer = new byte[blockSize];
    }

    public static CompressedIsoSectorSource? TryOpen(string path)
    {
        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                65536,
                FileOptions.RandomAccess);
            if (stream.Length < HeaderSize)
            {
                stream.Dispose();
                return null;
            }

            Span<byte> header = stackalloc byte[HeaderSize];
            stream.ReadExactly(header);
            var isCiso = header[..4].SequenceEqual("CISO"u8);
            var isZiso = header[..4].SequenceEqual("ZISO"u8);
            if (!isCiso && !isZiso)
            {
                stream.Dispose();
                return null;
            }

            var uncompressedSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(0x08, 8));
            var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0x10, 4));
            var version = header[0x14];
            var indexShift = header[0x15];

            // v1 is the interchange format both tools emit; larger block sizes are always
            // sector multiples, which keeps a 2048-byte read within a single block.
            if (uncompressedSize <= 0 || version > 1 ||
                blockSize == 0 || blockSize % SectorSize != 0 || blockSize > MaximumBlockBytes)
            {
                stream.Dispose();
                return null;
            }

            return new CompressedIsoSectorSource(
                stream, isZiso, blockSize, indexShift, uncompressedSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or EndOfStreamException)
        {
            stream?.Dispose();
            return null;
        }
    }

    public int ReadSector(uint sector, Span<byte> destination)
    {
        if (destination.Length > SectorSize)
            return 0;

        var imageOffset = (long)sector * SectorSize;
        if (imageOffset + destination.Length > _uncompressedSize)
            return 0;

        var blockIndex = imageOffset / _blockSize;
        var offsetInBlock = (int)(imageOffset % _blockSize);
        if (!TryLoadBlock(blockIndex))
            return 0;

        _blockBuffer.AsSpan(offsetInBlock, destination.Length).CopyTo(destination);
        return destination.Length;
    }

    private bool TryLoadBlock(long blockIndex)
    {
        if (blockIndex == _cachedBlockIndex)
            return true;

        try
        {
            Span<byte> entries = stackalloc byte[8];
            var indexPosition = HeaderSize + blockIndex * 4;
            if (indexPosition + entries.Length > _stream.Length)
                return false;
            _stream.Position = indexPosition;
            _stream.ReadExactly(entries);

            var index0 = BinaryPrimitives.ReadUInt32LittleEndian(entries[..4]);
            var index1 = BinaryPrimitives.ReadUInt32LittleEndian(entries.Slice(4, 4));
            var uncompressed = (index0 & 0x80000000u) != 0;
            var position = (long)(index0 & 0x7FFFFFFFu) << _indexShift;
            var nextPosition = (long)(index1 & 0x7FFFFFFFu) << _indexShift;
            var compressedSize = nextPosition - position;
            if (position < 0 || compressedSize <= 0 ||
                compressedSize > _blockSize + 4096 ||
                position + compressedSize > _stream.Length)
                return false;

            var input = new byte[compressedSize];
            _stream.Position = position;
            _stream.ReadExactly(input);

            if (uncompressed)
            {
                if (compressedSize < _blockSize)
                    return false;
                Array.Copy(input, _blockBuffer, (int)_blockSize);
            }
            else if (_isLz4)
            {
                if (!Lz4BlockDecoder.TryDecompress(input, _blockBuffer))
                    return false;
            }
            else
            {
                using var deflate = new DeflateStream(
                    new MemoryStream(input, writable: false),
                    CompressionMode.Decompress);
                deflate.ReadExactly(_blockBuffer, 0, (int)_blockSize);
            }

            _cachedBlockIndex = blockIndex;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or
                                   EndOfStreamException or NotSupportedException)
        {
            _cachedBlockIndex = -1;
            return false;
        }
    }

    public void Dispose() => _stream.Dispose();
}
