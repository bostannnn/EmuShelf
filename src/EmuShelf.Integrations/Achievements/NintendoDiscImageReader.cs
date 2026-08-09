using System.Buffers.Binary;
using System.Security.Cryptography;
using ZstdSharp;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// A read-only view of the bytes in a Nintendo disc image. Containers expose the same logical
/// bytes as a raw ISO; no source image is extracted, rewritten, or otherwise modified.
/// </summary>
internal abstract class NintendoDiscImageReader : IDisposable
{
    private const int CisoHeaderSize = 0x8000;
    private const int CisoMapSize = CisoHeaderSize - 8;
    private const int WbfsHeaderSize = 512;
    private const int WbfsDiscHeaderSize = 256;
    private const int WiiSectorSize = 0x8000;
    private const long WiiSectorCount = 143432L * 2;
    private const int RvzHeader1Size = 0x48;
    private const int RvzHeader2MinimumSize = 0xD5;
    private const int MaximumRvzHeaderBytes = 1024 * 1024;
    private const int MaximumRvzEntries = 1_000_000;
    private const int MaximumChunkBytes = 32 * 1024 * 1024;
    private const int MaximumExceptionsPerList = 3_328;
    private const int MaximumExceptionListBytes = MaximumExceptionsPerList * 22 + 2;

    public abstract long Length { get; }

    public abstract bool ReadAt(long offset, Span<byte> destination);

    public abstract void Dispose();

    public static NintendoDiscImageReader? TryOpen(string path)
    {
        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension switch
            {
                ".iso" or ".gcm" => RawNintendoDiscImageReader.TryOpenRaw(path),
                ".ciso" => CisoNintendoDiscImageReader.TryOpenCiso(path),
                ".wbfs" => WbfsNintendoDiscImageReader.TryOpenWbfs(path),
                ".rvz" => RvzNintendoDiscImageReader.TryOpenRvz(path),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or EndOfStreamException or
                                   InvalidDataException or CryptographicException or OverflowException or
                                   ZstdException)
        {
            return null;
        }
    }

    protected static FileStream OpenReadOnly(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        65536,
        FileOptions.RandomAccess);

    protected static bool TryReadExactlyAt(FileStream stream, long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > stream.Length - destination.Length)
            return false;

        stream.Position = offset;
        stream.ReadExactly(destination);
        return true;
    }

    protected static bool IsRangeWithin(long offset, int count, long length) =>
        offset >= 0 && count >= 0 && offset <= length - count;

    private sealed class RawNintendoDiscImageReader : NintendoDiscImageReader
    {
        private readonly FileStream _stream;

        private RawNintendoDiscImageReader(FileStream stream) => _stream = stream;

        public override long Length => _stream.Length;

        public static RawNintendoDiscImageReader? TryOpenRaw(string path)
        {
            var stream = OpenReadOnly(path);
            if (stream.Length == 0)
            {
                stream.Dispose();
                return null;
            }

            return new RawNintendoDiscImageReader(stream);
        }

        public override bool ReadAt(long offset, Span<byte> destination) =>
            TryReadExactlyAt(_stream, offset, destination);

        public override void Dispose() => _stream.Dispose();
    }

    private sealed class CisoNintendoDiscImageReader : NintendoDiscImageReader
    {
        private readonly FileStream _stream;
        private readonly uint _blockSize;
        private readonly ushort[] _blockMap;

        private CisoNintendoDiscImageReader(FileStream stream, uint blockSize, ushort[] blockMap)
        {
            _stream = stream;
            _blockSize = blockSize;
            _blockMap = blockMap;
        }

        public override long Length => (long)CisoMapSize * _blockSize;

        public static CisoNintendoDiscImageReader? TryOpenCiso(string path)
        {
            FileStream? stream = null;
            try
            {
                stream = OpenReadOnly(path);
                if (stream.Length < CisoHeaderSize)
                    return null;

                var header = new byte[CisoHeaderSize];
                if (!TryReadExactlyAt(stream, 0, header))
                    return null;
                if (!header.AsSpan(0, 4).SequenceEqual("CISO"u8))
                    return null;

                var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
                if (blockSize < WiiSectorSize ||
                    blockSize > MaximumChunkBytes ||
                    (blockSize & (blockSize - 1)) != 0)
                {
                    return null;
                }

                var map = new ushort[CisoMapSize];
                ushort nextUsedBlock = 0;
                for (var index = 0; index < map.Length; index++)
                {
                    var value = header[8 + index];
                    if (value == 0)
                    {
                        map[index] = ushort.MaxValue;
                    }
                    else if (value == 1)
                    {
                        map[index] = nextUsedBlock++;
                    }
                    else
                    {
                        return null;
                    }
                }

                var expectedMinimumLength = (long)CisoHeaderSize + (long)nextUsedBlock * blockSize;
                if (expectedMinimumLength > stream.Length)
                    return null;

                var reader = new CisoNintendoDiscImageReader(stream, blockSize, map);
                stream = null;
                return reader;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        public override bool ReadAt(long offset, Span<byte> destination)
        {
            if (!IsRangeWithin(offset, destination.Length, Length))
                return false;

            while (!destination.IsEmpty)
            {
                var blockIndex = (int)(offset / _blockSize);
                var offsetInBlock = (int)(offset % _blockSize);
                var count = Math.Min(destination.Length, (int)_blockSize - offsetInBlock);
                var target = destination[..count];
                var mappedBlock = _blockMap[blockIndex];
                if (mappedBlock == ushort.MaxValue)
                {
                    target.Clear();
                }
                else if (!TryReadExactlyAt(
                             _stream,
                             CisoHeaderSize + (long)mappedBlock * _blockSize + offsetInBlock,
                             target))
                {
                    return false;
                }

                offset += count;
                destination = destination[count..];
            }

            return true;
        }

        public override void Dispose() => _stream.Dispose();
    }

    private sealed class WbfsNintendoDiscImageReader : NintendoDiscImageReader
    {
        private readonly IReadOnlyList<FileStream> _files;
        private readonly long[] _fileStarts;
        private readonly long _physicalLength;
        private readonly long _wbfsSectorSize;
        private readonly ushort[] _blockMap;

        private WbfsNintendoDiscImageReader(
            IReadOnlyList<FileStream> files,
            long[] fileStarts,
            long physicalLength,
            long wbfsSectorSize,
            ushort[] blockMap)
        {
            _files = files;
            _fileStarts = fileStarts;
            _physicalLength = physicalLength;
            _wbfsSectorSize = wbfsSectorSize;
            _blockMap = blockMap;
        }

        public override long Length => WiiSectorCount * WiiSectorSize;

        public static WbfsNintendoDiscImageReader? TryOpenWbfs(string path)
        {
            var files = new List<FileStream>();
            try
            {
                files.Add(OpenReadOnly(path));
                for (var part = 1; part <= 9; part++)
                {
                    var splitPath = path[..^1] + part;
                    if (!File.Exists(splitPath))
                        break;
                    files.Add(OpenReadOnly(splitPath));
                }

                var first = files[0];
                if (first.Length < WbfsHeaderSize)
                    return null;

                Span<byte> header = stackalloc byte[WbfsHeaderSize];
                if (!TryReadExactlyAt(first, 0, header) || !header[..4].SequenceEqual("WBFS"u8))
                    return null;

                var hardDiskSectorCount = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4));
                var hardDiskSectorShift = header[8];
                var wbfsSectorShift = header[9];
                if (hardDiskSectorCount == 0 ||
                    hardDiskSectorShift is < 9 or > 31 ||
                    wbfsSectorShift is < 15 or > 31 ||
                    header[12] == 0)
                {
                    return null;
                }

                var hardDiskSectorSize = 1L << hardDiskSectorShift;
                var wbfsSectorSize = 1L << wbfsSectorShift;
                if (wbfsSectorSize < WiiSectorSize || wbfsSectorSize > int.MaxValue)
                    return null;

                long physicalLength = 0;
                var fileStarts = new long[files.Count];
                for (var index = 0; index < files.Count; index++)
                {
                    fileStarts[index] = physicalLength;
                    physicalLength = checked(physicalLength + files[index].Length);
                }

                if (physicalLength != checked((long)hardDiskSectorCount * hardDiskSectorSize))
                    return null;

                var blocksPerDisc = (LengthForWiiDisc() + wbfsSectorSize - 1) / wbfsSectorSize;
                if (blocksPerDisc > ushort.MaxValue ||
                    hardDiskSectorSize + WbfsDiscHeaderSize + blocksPerDisc * sizeof(ushort) > physicalLength)
                {
                    return null;
                }

                var map = new ushort[blocksPerDisc];
                var mapBytes = new byte[map.Length * sizeof(ushort)];
                if (!TryReadAcrossFiles(files, fileStarts, physicalLength,
                                        hardDiskSectorSize + WbfsDiscHeaderSize, mapBytes))
                {
                    return null;
                }
                for (var index = 0; index < map.Length; index++)
                    map[index] = BinaryPrimitives.ReadUInt16BigEndian(mapBytes.AsSpan(index * 2, 2));

                var reader = new WbfsNintendoDiscImageReader(
                    files, fileStarts, physicalLength, wbfsSectorSize, map);
                files = [];
                return reader;
            }
            finally
            {
                foreach (var file in files)
                    file.Dispose();
            }
        }

        public override bool ReadAt(long offset, Span<byte> destination)
        {
            if (!IsRangeWithin(offset, destination.Length, Length))
                return false;

            while (!destination.IsEmpty)
            {
                var blockIndex = (int)(offset / _wbfsSectorSize);
                var offsetInBlock = (int)(offset % _wbfsSectorSize);
                var count = Math.Min(destination.Length, (int)_wbfsSectorSize - offsetInBlock);
                var target = destination[..count];
                var mappedBlock = _blockMap[blockIndex];
                if (mappedBlock == 0)
                {
                    // WBFS scrubbers omit unused logical blocks. Their logical value is zero.
                    target.Clear();
                }
                else if (!TryReadAcrossFiles(
                             _files,
                             _fileStarts,
                             _physicalLength,
                             (long)mappedBlock * _wbfsSectorSize + offsetInBlock,
                             target))
                {
                    return false;
                }

                offset += count;
                destination = destination[count..];
            }

            return true;
        }

        public override void Dispose()
        {
            foreach (var file in _files)
                file.Dispose();
        }

        private static long LengthForWiiDisc() => WiiSectorCount * WiiSectorSize;

        private static bool TryReadAcrossFiles(
            IReadOnlyList<FileStream> files,
            IReadOnlyList<long> fileStarts,
            long totalLength,
            long offset,
            Span<byte> destination)
        {
            if (!IsRangeWithin(offset, destination.Length, totalLength))
                return false;

            while (!destination.IsEmpty)
            {
                var fileIndex = FindFileIndex(fileStarts, offset);
                if (fileIndex < 0 || fileIndex >= files.Count)
                    return false;

                var file = files[fileIndex];
                var relativeOffset = offset - fileStarts[fileIndex];
                var count = (int)Math.Min((long)destination.Length, file.Length - relativeOffset);
                if (count <= 0 || !TryReadExactlyAt(file, relativeOffset, destination[..count]))
                    return false;

                offset += count;
                destination = destination[count..];
            }

            return true;
        }

        private static int FindFileIndex(IReadOnlyList<long> fileStarts, long offset)
        {
            var low = 0;
            var high = fileStarts.Count - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                if (fileStarts[middle] <= offset)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return high;
        }
    }

    private sealed class RvzNintendoDiscImageReader : NintendoDiscImageReader
    {
        private const uint RvzCompressionNone = 0;
        private const uint RvzCompressionZstd = 5;

        private readonly FileStream _stream;
        private readonly byte[] _discHeader;
        private readonly uint _compressionType;
        private readonly uint _chunkSize;
        private readonly RawDataEntry[] _rawDataEntries;
        private readonly WiiPartitionDataEntry[] _partitionDataEntries;
        private readonly RvzGroupEntry[] _groupEntries;
        private readonly long _logicalLength;
        private readonly Dictionary<int, byte[]> _cachedChunks = new();
        private readonly Dictionary<int, PartitionChunk> _cachedPartitionChunks = new();
        private byte[]? _cachedWiiGroup;
        private WiiPartition? _cachedWiiPartition;
        private long _cachedWiiGroupStart = -1;

        private RvzNintendoDiscImageReader(
            FileStream stream,
            byte[] discHeader,
            uint compressionType,
            uint chunkSize,
            RawDataEntry[] rawDataEntries,
            WiiPartitionDataEntry[] partitionDataEntries,
            RvzGroupEntry[] groupEntries,
            long logicalLength)
        {
            _stream = stream;
            _discHeader = discHeader;
            _compressionType = compressionType;
            _chunkSize = chunkSize;
            _rawDataEntries = rawDataEntries;
            _partitionDataEntries = partitionDataEntries;
            _groupEntries = groupEntries;
            _logicalLength = logicalLength;
        }

        public override long Length => _logicalLength;

        public static RvzNintendoDiscImageReader? TryOpenRvz(string path)
        {
            FileStream? stream = null;
            try
            {
                stream = OpenReadOnly(path);
                if (stream.Length < RvzHeader1Size + RvzHeader2MinimumSize)
                    return null;

                Span<byte> header1 = stackalloc byte[RvzHeader1Size];
                if (!TryReadExactlyAt(stream, 0, header1) || !header1[..4].SequenceEqual("RVZ\x01"u8))
                    return null;

                var header2Size = BinaryPrimitives.ReadUInt32BigEndian(header1.Slice(0x0C, 4));
                var logicalLength = BinaryPrimitives.ReadUInt64BigEndian(header1.Slice(0x24, 8));
                var declaredFileLength = BinaryPrimitives.ReadUInt64BigEndian(header1.Slice(0x2C, 8));
                if (header2Size is < RvzHeader2MinimumSize or > MaximumRvzHeaderBytes ||
                    logicalLength > long.MaxValue ||
                    declaredFileLength != (ulong)stream.Length)
                {
                    return null;
                }

                var header1Hash = SHA1.HashData(header1[..0x34]);
                if (!header1.Slice(0x34, header1Hash.Length).SequenceEqual(header1Hash))
                    return null;

                var header2 = new byte[header2Size];
                if (!TryReadExactlyAt(stream, RvzHeader1Size, header2) ||
                    !header1.Slice(0x10, 20).SequenceEqual(SHA1.HashData(header2)))
                {
                    return null;
                }
                if (header2.Length < RvzHeader2MinimumSize)
                    return null;

                var compressionType = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(4, 4));
                var chunkSize = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0x0C, 4));
                var discType = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0, 4));
                if (compressionType is not (RvzCompressionNone or RvzCompressionZstd) ||
                    chunkSize is < WiiSectorSize or > MaximumChunkBytes ||
                    (chunkSize & (chunkSize - 1)) != 0)
                {
                    return null;
                }
                // RVZ stores Wii partition payloads decrypted. Its reader reconstructs an
                // encrypted raw disc for the canonical encrypted-disc algorithm. A source
                // marked as an already-decrypted Wii image needs a distinct logical layout, so
                // it remains outside this verified container gate rather than being misread.
                if (discType == 2 && header2[0x10 + 0x61] != 0)
                    return null;

                var partitionCount = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0x90, 4));
                var partitionEntrySize = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0x94, 4));
                var partitionOffset = BinaryPrimitives.ReadUInt64BigEndian(header2.AsSpan(0x98, 8));
                if (partitionCount > MaximumRvzEntries || partitionOffset > long.MaxValue)
                    return null;

                var rawCount = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0xB4, 4));
                var rawOffset = BinaryPrimitives.ReadUInt64BigEndian(header2.AsSpan(0xB8, 8));
                var rawSize = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0xC0, 4));
                var groupCount = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0xC4, 4));
                var groupOffset = BinaryPrimitives.ReadUInt64BigEndian(header2.AsSpan(0xC8, 8));
                var groupSize = BinaryPrimitives.ReadUInt32BigEndian(header2.AsSpan(0xD0, 4));
                if (rawCount > MaximumRvzEntries || groupCount > MaximumRvzEntries ||
                    rawOffset > long.MaxValue || groupOffset > long.MaxValue)
                {
                    return null;
                }

                var rawTable = ReadTable(
                    stream,
                    (long)rawOffset,
                    rawSize,
                    checked((int)rawCount * 24),
                    compressionType);
                var groupTable = ReadTable(
                    stream,
                    (long)groupOffset,
                    groupSize,
                    checked((int)groupCount * 12),
                    compressionType);
                if (rawTable is null || groupTable is null)
                    return null;

                var rawEntries = ParseRawDataEntries(rawTable, groupCount);
                var groupEntries = ParseGroupEntries(groupTable);
                if (rawEntries is null || groupEntries is null)
                    return null;

                var partitionEntries = ParsePartitionDataEntries(
                    stream,
                    (long)partitionOffset,
                    partitionCount,
                    partitionEntrySize,
                    header2.AsSpan(0xA0, 20),
                    groupCount);
                if (partitionEntries is null)
                    return null;

                var reader = new RvzNintendoDiscImageReader(
                    stream,
                    header2.AsSpan(0x10, 0x80).ToArray(),
                    compressionType,
                    chunkSize,
                    rawEntries,
                    partitionEntries,
                    groupEntries,
                    (long)logicalLength);
                if (!reader.HasOnlyValidRanges())
                {
                    reader.Dispose();
                    return null;
                }

                stream = null;
                return reader;
            }
            catch (OverflowException)
            {
                return null;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        public override bool ReadAt(long offset, Span<byte> destination)
        {
            if (!IsRangeWithin(offset, destination.Length, Length))
                return false;

            while (!destination.IsEmpty)
            {
                if (offset < _discHeader.Length)
                {
                    var headerCount = Math.Min(destination.Length, _discHeader.Length - (int)offset);
                    _discHeader.AsSpan((int)offset, headerCount).CopyTo(destination);
                    offset += headerCount;
                    destination = destination[headerCount..];
                    continue;
                }

                var partitionEntryIndex = FindPartitionDataEntry(offset);
                if (partitionEntryIndex >= 0)
                {
                    var partitionDataEntry = _partitionDataEntries[partitionEntryIndex];
                    var partitionCount = (int)Math.Min(destination.Length, partitionDataEntry.End - offset);
                    if (!ReadWiiPartitionData(partitionDataEntry, offset, destination[..partitionCount]))
                        return false;

                    offset += partitionCount;
                    destination = destination[partitionCount..];
                    continue;
                }

                var entryIndex = FindRawDataEntry(offset);
                if (entryIndex < 0)
                    return false;
                var entry = _rawDataEntries[entryIndex];
                var count = (int)Math.Min(destination.Length, entry.End - offset);
                if (!ReadRawData(entryIndex, offset, destination[..count]))
                    return false;

                offset += count;
                destination = destination[count..];
            }

            return true;
        }

        public override void Dispose() => _stream.Dispose();

        private static byte[]? ReadTable(
            FileStream stream,
            long offset,
            uint storedSize,
            int expectedSize,
            uint compressionType)
        {
            if (expectedSize == 0)
                return [];
            if (storedSize == 0 || storedSize > MaximumChunkBytes ||
                !IsRangeWithin(offset, checked((int)storedSize), stream.Length))
            {
                return null;
            }

            var stored = new byte[storedSize];
            if (!TryReadExactlyAt(stream, offset, stored))
                return null;

            var result = Decompress(stored, expectedSize, compressionType);
            return result is { Length: var length } && length == expectedSize ? result : null;
        }

        private static RawDataEntry[]? ParseRawDataEntries(byte[] table, uint groupCount)
        {
            var entries = new RawDataEntry[table.Length / 24];
            long previousEnd = 0;
            for (var index = 0; index < entries.Length; index++)
            {
                var data = table.AsSpan(index * 24, 24);
                var start = BinaryPrimitives.ReadUInt64BigEndian(data[..8]);
                var size = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(8, 8));
                var groupIndex = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4));
                var groupCountForEntry = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4));
                if (start > long.MaxValue || size > long.MaxValue ||
                    start + size > long.MaxValue || start < (ulong)previousEnd ||
                    groupIndex > groupCount || groupCountForEntry > groupCount - groupIndex)
                {
                    return null;
                }

                entries[index] = new RawDataEntry((long)start, (long)size, groupIndex, groupCountForEntry);
                previousEnd = checked((long)(start + size));
            }

            return entries;
        }

        private static WiiPartitionDataEntry[]? ParsePartitionDataEntries(
            FileStream stream,
            long offset,
            uint count,
            uint entrySize,
            ReadOnlySpan<byte> expectedHash,
            uint groupCount)
        {
            if (count == 0)
                return [];
            if (entrySize < 0x30 || entrySize > MaximumChunkBytes ||
                count > int.MaxValue / entrySize)
            {
                return null;
            }

            var tableSize = checked((int)(count * entrySize));
            if (!IsRangeWithin(offset, tableSize, stream.Length))
                return null;
            var table = new byte[tableSize];
            if (!TryReadExactlyAt(stream, offset, table) || !SHA1.HashData(table).AsSpan().SequenceEqual(expectedHash))
                return null;

            var results = new List<WiiPartitionDataEntry>(checked((int)count * 2));
            for (var index = 0; index < count; index++)
            {
                var entry = table.AsSpan(checked((int)(index * entrySize)), (int)entrySize);
                var partition = new WiiPartition(entry[..16].ToArray());
                var firstSector = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(16, 4));
                var firstCount = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(20, 4));
                var secondSector = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(32, 4));
                var secondCount = BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(36, 4));
                if (secondCount != 0 && secondSector < firstSector)
                    return null;
                var totalSectors = secondCount == 0
                    ? (ulong)firstCount
                    : (ulong)secondSector - firstSector + secondCount;
                if (totalSectors > long.MaxValue / 0x7C00)
                    return null;
                partition.FirstPhysicalOffset = checked((long)firstSector * WiiSectorSize);
                partition.DecryptedSize = checked((long)totalSectors * 0x7C00);

                if (!AddPartitionDataEntry(
                        results, partition, firstSector, firstCount,
                        BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(24, 4)),
                        BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(28, 4)), groupCount))
                {
                    return null;
                }
                if (!AddPartitionDataEntry(
                        results, partition, secondSector, secondCount,
                        BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(40, 4)),
                        BinaryPrimitives.ReadUInt32BigEndian(entry.Slice(44, 4)), groupCount))
                {
                    return null;
                }
            }

            return results.OrderBy(entry => entry.Start).ToArray();
        }

        private static bool AddPartitionDataEntry(
            ICollection<WiiPartitionDataEntry> entries,
            WiiPartition partition,
            uint firstSector,
            uint sectorCount,
            uint groupIndex,
            uint groupCountForEntry,
            uint totalGroupCount)
        {
            if (sectorCount == 0)
                return groupCountForEntry == 0;
            if (groupIndex > totalGroupCount || groupCountForEntry > totalGroupCount - groupIndex)
                return false;
            try
            {
                var start = checked((long)firstSector * WiiSectorSize);
                var physicalSize = checked((long)sectorCount * WiiSectorSize);
                var decryptedStart = checked(((long)firstSector * WiiSectorSize - partition.FirstPhysicalOffset) /
                                             WiiSectorSize * 0x7C00);
                var decryptedSize = checked((long)sectorCount * 0x7C00);
                entries.Add(new WiiPartitionDataEntry(
                    partition, start, physicalSize, decryptedStart, decryptedSize,
                    groupIndex, groupCountForEntry));
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static RvzGroupEntry[]? ParseGroupEntries(byte[] table)
        {
            var entries = new RvzGroupEntry[table.Length / 12];
            for (var index = 0; index < entries.Length; index++)
            {
                var data = table.AsSpan(index * 12, 12);
                var dataOffset = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                var dataSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                var packedSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4));
                entries[index] = new RvzGroupEntry(dataOffset, dataSize, packedSize);
            }

            return entries;
        }

        private bool HasOnlyValidRanges()
        {
            foreach (var entry in _rawDataEntries)
            {
                var skippedBytes = entry.Start % WiiSectorSize;
                var start = entry.Start - skippedBytes;
                var size = checked(entry.Size + skippedBytes);
                var expectedGroups = (size + _chunkSize - 1) / _chunkSize;
                if (expectedGroups != entry.GroupCount || entry.GroupIndex + entry.GroupCount > _groupEntries.Length)
                    return false;
            }

            var previousEnd = 0L;
            foreach (var entry in _partitionDataEntries)
            {
                var expectedGroups = (entry.PhysicalSize + _chunkSize - 1) / _chunkSize;
                if (entry.Start < previousEnd || expectedGroups != entry.GroupCount ||
                    entry.GroupIndex + entry.GroupCount > _groupEntries.Length)
                {
                    return false;
                }

                previousEnd = entry.End;
            }

            foreach (var rawEntry in _rawDataEntries)
            {
                foreach (var partitionEntry in _partitionDataEntries)
                {
                    if (rawEntry.Start < partitionEntry.End && partitionEntry.Start < rawEntry.End)
                        return false;
                }
            }

            return _rawDataEntries.Length > 0;
        }

        private int FindRawDataEntry(long offset)
        {
            for (var index = 0; index < _rawDataEntries.Length; index++)
            {
                var entry = _rawDataEntries[index];
                if (offset >= entry.Start && offset < entry.End)
                    return index;
            }

            return -1;
        }

        private int FindPartitionDataEntry(long offset)
        {
            for (var index = 0; index < _partitionDataEntries.Length; index++)
            {
                var entry = _partitionDataEntries[index];
                if (offset >= entry.Start && offset < entry.End)
                    return index;
            }

            return -1;
        }

        private bool ReadRawData(int entryIndex, long offset, Span<byte> destination)
        {
            var entry = _rawDataEntries[entryIndex];
            var skippedBytes = entry.Start % WiiSectorSize;
            var adjustedStart = entry.Start - skippedBytes;
            var adjustedSize = checked(entry.Size + skippedBytes);
            while (!destination.IsEmpty)
            {
                var relativeOffset = offset - adjustedStart;
                var groupWithinEntry = (int)(relativeOffset / _chunkSize);
                if (groupWithinEntry < 0 || groupWithinEntry >= entry.GroupCount)
                    return false;

                var groupOffset = checked((long)groupWithinEntry * _chunkSize);
                var groupLength = (int)Math.Min(_chunkSize, adjustedSize - groupOffset);
                var offsetInGroup = (int)(relativeOffset - groupOffset);
                var count = Math.Min(destination.Length, groupLength - offsetInGroup);
                if (count <= 0)
                    return false;

                var chunk = GetRawChunk(checked((int)entry.GroupIndex + groupWithinEntry), groupLength,
                                        adjustedStart + groupOffset);
                if (chunk is null)
                    return false;
                chunk.AsSpan(offsetInGroup, count).CopyTo(destination);

                offset += count;
                destination = destination[count..];
            }

            return true;
        }

        private bool ReadWiiPartitionData(
            WiiPartitionDataEntry entry,
            long offset,
            Span<byte> destination)
        {
            while (!destination.IsEmpty)
            {
                var groupStart = entry.Partition.FirstPhysicalOffset +
                                 ((offset - entry.Partition.FirstPhysicalOffset) / 0x200000) * 0x200000;
                if (_cachedWiiGroup is null || !ReferenceEquals(_cachedWiiPartition, entry.Partition) ||
                    _cachedWiiGroupStart != groupStart)
                {
                    var group = BuildEncryptedWiiGroup(entry.Partition, groupStart);
                    if (group is null)
                        return false;
                    _cachedWiiGroup = group;
                    _cachedWiiPartition = entry.Partition;
                    _cachedWiiGroupStart = groupStart;
                }

                var offsetInGroup = (int)(offset - groupStart);
                var count = Math.Min(destination.Length, _cachedWiiGroup.Length - offsetInGroup);
                if (count <= 0)
                    return false;
                _cachedWiiGroup.AsSpan(offsetInGroup, count).CopyTo(destination);
                offset += count;
                destination = destination[count..];
            }

            return true;
        }

        private byte[]? BuildEncryptedWiiGroup(WiiPartition partition, long groupStart)
        {
            const int clustersPerGroup = 64;
            const int clusterDataSize = 0x7C00;
            const int clusterHeaderSize = 0x400;
            const int fullGroupDataSize = clustersPerGroup * clusterDataSize;
            var groupDataStart = ((groupStart - partition.FirstPhysicalOffset) / WiiSectorSize) * clusterDataSize;
            if (groupDataStart < 0 || groupDataStart >= partition.DecryptedSize)
                return null;

            var data = new byte[fullGroupDataSize];
            var exceptions = new List<HashException>();
            foreach (var entry in _partitionDataEntries)
            {
                if (!ReferenceEquals(entry.Partition, partition))
                    continue;

                var overlapStart = Math.Max(groupDataStart, entry.DecryptedStart);
                var overlapEnd = Math.Min(groupDataStart + fullGroupDataSize, entry.DecryptedEnd);
                if (overlapStart >= overlapEnd)
                    continue;

                var relative = overlapStart - entry.DecryptedStart;
                while (relative < overlapEnd - entry.DecryptedStart)
                {
                    var chunkIndex = (int)(relative / PartitionChunkSize);
                    var chunkOffset = (int)(relative % PartitionChunkSize);
                    var chunk = GetPartitionChunk(entry, chunkIndex);
                    if (chunk is null || chunkOffset >= chunk.Data.Length)
                        return null;

                    var count = (int)Math.Min(
                        overlapEnd - (entry.DecryptedStart + relative),
                        chunk.Data.Length - chunkOffset);
                    chunk.Data.AsSpan(chunkOffset, count).CopyTo(
                        data.AsSpan((int)(entry.DecryptedStart + relative - groupDataStart), count));
                    exceptions.AddRange(chunk.Exceptions);
                    relative += count;
                }
            }

            var headers = BuildWiiHashHeaders(data);
            foreach (var exception in exceptions)
            {
                if (exception.Offset > headers.Length - exception.Hash.Length)
                    return null;
                exception.Hash.CopyTo(headers.AsSpan(exception.Offset));
            }

            var output = new byte[0x200000];
            var zeroIv = new byte[16];
            for (var cluster = 0; cluster < clustersPerGroup; cluster++)
            {
                var outputOffset = cluster * WiiSectorSize;
                var header = headers.AsSpan(cluster * clusterHeaderSize, clusterHeaderSize);
                EncryptCbc(partition.Key, zeroIv, header, output.AsSpan(outputOffset, clusterHeaderSize));
                EncryptCbc(
                    partition.Key,
                    output.AsSpan(outputOffset + 0x3D0, 16),
                    data.AsSpan(cluster * clusterDataSize, clusterDataSize),
                    output.AsSpan(outputOffset + clusterHeaderSize, clusterDataSize));
            }

            return output;
        }

        private int PartitionChunkSize => checked((int)(_chunkSize / WiiSectorSize * 0x7C00));

        private PartitionChunk? GetPartitionChunk(WiiPartitionDataEntry entry, int chunkIndex)
        {
            var groupIndex = checked((int)entry.GroupIndex + chunkIndex);
            if (_cachedPartitionChunks.TryGetValue(groupIndex, out var cached))
                return cached;
            if (chunkIndex < 0 || chunkIndex >= entry.GroupCount ||
                (uint)groupIndex >= (uint)_groupEntries.Length)
            {
                return null;
            }

            var group = _groupEntries[groupIndex];
            var expectedDataSize = (int)Math.Min(
                PartitionChunkSize,
                entry.DecryptedSize - (long)chunkIndex * PartitionChunkSize);
            var exceptionListCount = Math.Max(1, (int)(_chunkSize / 0x200000));
            var storedSize = group.DataSize & 0x7FFFFFFFu;
            byte[]? stored;
            if (storedSize == 0)
            {
                return new PartitionChunk(new byte[expectedDataSize], []);
            }
            else
            {
                var fileOffset = checked((long)group.DataOffset * 4);
                if (storedSize > MaximumChunkBytes ||
                    !IsRangeWithin(fileOffset, (int)storedSize, _stream.Length))
                {
                    return null;
                }
                var compressed = new byte[storedSize];
                if (!TryReadExactlyAt(_stream, fileOffset, compressed))
                    return null;
                var compressionType = (group.DataSize & 0x80000000u) != 0
                    ? _compressionType
                    : RvzCompressionNone;
                stored = DecompressWithLimit(
                    compressed,
                    checked(expectedDataSize + exceptionListCount * MaximumExceptionListBytes),
                    compressionType);
                if (stored is null)
                    return null;
            }

            if (!TryParseExceptions(stored, exceptionListCount, (group.DataSize & 0x80000000u) == 0,
                                    out var dataOffset, out var exceptions))
            {
                return null;
            }

            var data = stored[dataOffset..];
            if (group.PackedSize != 0)
            {
                data = TryUnpackRvz(data, expectedDataSize, (long)chunkIndex * PartitionChunkSize);
                if (data is null)
                    return null;
            }
            if (data.Length != expectedDataSize)
                return null;

            var additionalOffset = (int)(((long)chunkIndex * PartitionChunkSize % 0x1F0000) /
                                          0x7C00 * 0x400);
            foreach (var exception in exceptions)
            {
                exception.Offset = checked(exception.Offset + additionalOffset);
            }

            var result = new PartitionChunk(data, exceptions);
            if (_cachedPartitionChunks.Count >= 32)
                _cachedPartitionChunks.Clear();
            _cachedPartitionChunks[groupIndex] = result;
            return result;
        }

        private static bool TryParseExceptions(
            ReadOnlySpan<byte> source,
            int listCount,
            bool alignLastList,
            out int dataOffset,
            out List<HashException> exceptions)
        {
            dataOffset = 0;
            exceptions = [];
            for (var list = 0; list < listCount; list++)
            {
                if (source.Length - dataOffset < 2)
                    return false;
                var count = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(dataOffset, 2));
                var size = checked(2 + count * 22);
                if (source.Length - dataOffset < size || count > MaximumExceptionsPerList)
                    return false;
                for (var index = 0; index < count; index++)
                {
                    var item = source.Slice(dataOffset + 2 + index * 22, 22);
                    exceptions.Add(new HashException(
                        BinaryPrimitives.ReadUInt16BigEndian(item[..2]), item[2..].ToArray()));
                }
                dataOffset += size;
                if (alignLastList && list == listCount - 1)
                    dataOffset = (dataOffset + 3) & ~3;
            }

            return dataOffset <= source.Length;
        }

        private static byte[] BuildWiiHashHeaders(byte[] data)
        {
            const int clusters = 64;
            const int headerSize = 0x400;
            var headers = new byte[clusters * headerSize];
            for (var cluster = 0; cluster < clusters; cluster++)
            {
                var header = headers.AsSpan(cluster * headerSize, headerSize);
                var block = data.AsSpan(cluster * 0x7C00, 0x7C00);
                for (var index = 0; index < 31; index++)
                    SHA1.HashData(block.Slice(index * 0x400, 0x400)).CopyTo(header.Slice(index * 20, 20));
            }

            for (var group = 0; group < 8; group++)
            {
                var first = group * 8;
                var h1 = new byte[8 * 20];
                for (var index = 0; index < 8; index++)
                {
                    SHA1.HashData(headers.AsSpan((first + index) * headerSize, 31 * 20))
                        .CopyTo(h1.AsSpan(index * 20, 20));
                }
                for (var index = 0; index < 8; index++)
                    h1.CopyTo(headers.AsSpan((first + index) * headerSize + 0x280, h1.Length));
                SHA1.HashData(h1).CopyTo(headers.AsSpan(0x340 + group * 20, 20));
            }

            var h2 = headers.AsSpan(0x340, 8 * 20).ToArray();
            for (var cluster = 0; cluster < clusters; cluster++)
                h2.CopyTo(headers.AsSpan(cluster * headerSize + 0x340, h2.Length));
            return headers;
        }

        private static void EncryptCbc(
            byte[] key,
            ReadOnlySpan<byte> iv,
            ReadOnlySpan<byte> source,
            Span<byte> destination)
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv.ToArray();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            using var encryptor = aes.CreateEncryptor();
            var encrypted = new byte[source.Length];
            var written = encryptor.TransformBlock(source.ToArray(), 0, source.Length, encrypted, 0);
            if (written != destination.Length)
                throw new CryptographicException("The Wii sector could not be encrypted.");
            encrypted.CopyTo(destination);
        }

        private byte[]? GetRawChunk(int groupIndex, int expectedSize, long dataOffset)
        {
            if (_cachedChunks.TryGetValue(groupIndex, out var cached))
                return cached.Length == expectedSize ? cached : null;

            if ((uint)groupIndex >= (uint)_groupEntries.Length)
                return null;
            var group = _groupEntries[groupIndex];
            var storedSize = group.DataSize & 0x7FFFFFFFu;
            byte[]? unpacked;
            if (storedSize == 0)
            {
                unpacked = new byte[expectedSize];
            }
            else
            {
                var fileOffset = checked((long)group.DataOffset * 4);
                if (storedSize > MaximumChunkBytes ||
                    !IsRangeWithin(fileOffset, (int)storedSize, _stream.Length))
                {
                    return null;
                }

                var stored = new byte[storedSize];
                if (!TryReadExactlyAt(_stream, fileOffset, stored))
                    return null;

                var compressionType = (group.DataSize & 0x80000000u) != 0
                    ? _compressionType
                    : RvzCompressionNone;
                unpacked = Decompress(stored, group.PackedSize == 0 ? expectedSize : (int)group.PackedSize,
                                      compressionType);
                if (unpacked is null)
                    return null;
                if (group.PackedSize != 0)
                    unpacked = TryUnpackRvz(unpacked, expectedSize, dataOffset);
                if (unpacked is null || unpacked.Length != expectedSize)
                    return null;
            }

            if (_cachedChunks.Count >= 8)
                _cachedChunks.Clear();
            _cachedChunks[groupIndex] = unpacked;
            return unpacked;
        }

        private static byte[]? Decompress(byte[] stored, int expectedSize, uint compressionType)
        {
            try
            {
                return compressionType switch
                {
                    RvzCompressionNone => stored.Length == expectedSize ? stored : null,
                    RvzCompressionZstd => DecompressZstd(stored, expectedSize),
                    _ => null,
                };
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or
                                       CryptographicException or OverflowException or ZstdException)
            {
                return null;
            }
        }

        private static byte[]? DecompressZstd(byte[] stored, int expectedSize)
        {
            using var decompressor = new Decompressor();
            var decompressed = new byte[expectedSize];
            var written = decompressor.Unwrap(stored, decompressed);
            return written == expectedSize ? decompressed : null;
        }

        private static byte[]? DecompressWithLimit(byte[] stored, int maximumSize, uint compressionType)
        {
            try
            {
                if (compressionType == RvzCompressionNone)
                    return stored.Length <= maximumSize ? stored : null;
                if (compressionType != RvzCompressionZstd)
                    return null;

                using var decompressor = new Decompressor();
                var decompressed = new byte[maximumSize];
                var written = decompressor.Unwrap(stored, decompressed);
                return decompressed[..written];
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or
                                       CryptographicException or OverflowException or ZstdException)
            {
                return null;
            }
        }

        private static byte[]? TryUnpackRvz(byte[] packed, int expectedSize, long dataOffset)
        {
            try
            {
                var result = new byte[expectedSize];
                var sourceOffset = 0;
                var destinationOffset = 0;
                while (sourceOffset < packed.Length)
                {
                    if (packed.Length - sourceOffset < sizeof(uint))
                        return null;
                    var size = BinaryPrimitives.ReadUInt32BigEndian(packed.AsSpan(sourceOffset, 4));
                    sourceOffset += sizeof(uint);
                    var isJunk = (size & 0x80000000) != 0;
                    var count = checked((int)(size & 0x7FFFFFFF));
                    if (count > result.Length - destinationOffset)
                        return null;

                    if (isJunk)
                    {
                        const int seedBytes = 17 * sizeof(uint);
                        if (packed.Length - sourceOffset < seedBytes)
                            return null;
                        var generator = new RvzLaggedFibonacciGenerator(
                            packed.AsSpan(sourceOffset, seedBytes));
                        generator.Skip(checked((int)((dataOffset + destinationOffset) % WiiSectorSize)));
                        generator.WriteBytes(result.AsSpan(destinationOffset, count));
                        sourceOffset += seedBytes;
                    }
                    else
                    {
                        if (packed.Length - sourceOffset < count)
                            return null;
                        packed.AsSpan(sourceOffset, count).CopyTo(result.AsSpan(destinationOffset));
                        sourceOffset += count;
                    }

                    destinationOffset += count;
                }

                return sourceOffset == packed.Length && destinationOffset == result.Length ? result : null;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        private readonly record struct RawDataEntry(long Start, long Size, uint GroupIndex, uint GroupCount)
        {
            public long End => checked(Start + Size);
        }

        private readonly record struct RvzGroupEntry(uint DataOffset, uint DataSize, uint PackedSize);

        private sealed class WiiPartition(byte[] key)
        {
            public byte[] Key { get; } = key;
            public long FirstPhysicalOffset { get; set; }
            public long DecryptedSize { get; set; }
        }

        private readonly record struct WiiPartitionDataEntry(
            WiiPartition Partition,
            long Start,
            long PhysicalSize,
            long DecryptedStart,
            long DecryptedSize,
            uint GroupIndex,
            uint GroupCount)
        {
            public long End => checked(Start + PhysicalSize);
            public long DecryptedEnd => checked(DecryptedStart + DecryptedSize);
        }

        private sealed class PartitionChunk(byte[] data, List<HashException> exceptions)
        {
            public byte[] Data { get; } = data;
            public List<HashException> Exceptions { get; } = exceptions;
        }

        private sealed class HashException(int offset, byte[] hash)
        {
            public int Offset { get; set; } = offset;
            public byte[] Hash { get; } = hash;
        }
    }

    private sealed class RvzLaggedFibonacciGenerator
    {
        private const int SeedWords = 17;
        private const int WordCount = 521;
        private const int Lag = 32;
        private readonly uint[] _buffer = new uint[WordCount];
        private int _position;

        public RvzLaggedFibonacciGenerator(ReadOnlySpan<byte> seed)
        {
            if (seed.Length != SeedWords * sizeof(uint))
                throw new ArgumentException("An RVZ padding seed must contain 17 words.", nameof(seed));

            for (var index = 0; index < SeedWords; index++)
                _buffer[index] = BinaryPrimitives.ReadUInt32BigEndian(seed.Slice(index * 4, 4));
            for (var index = SeedWords; index < WordCount; index++)
                _buffer[index] = (_buffer[index - 17] << 23) ^ (_buffer[index - 16] >> 9) ^ _buffer[index - 1];
            // rcheevos/Dolphin apply this per-word transform to the whole buffer after the seed
            // extension and before the warm-up rounds. Omitting it (or reading words big-endian
            // below) corrupts every regenerated junk byte and breaks RVZ Wii/GameCube hashing.
            for (var index = 0; index < WordCount; index++)
            {
                var value = _buffer[index];
                _buffer[index] = BinaryPrimitives.ReverseEndianness(
                    (value & 0xFF00FFFFu) | ((value >> 2) & 0x00FF0000u));
            }
            for (var round = 0; round < 4; round++)
                Advance();
        }

        public void Skip(int count)
        {
            while (count > 0)
            {
                var available = Math.Min(count, WordCount * 4 - _position);
                _position += available;
                count -= available;
                if (_position == WordCount * 4)
                {
                    Advance();
                    _position = 0;
                }
            }
        }

        public void WriteBytes(Span<byte> destination)
        {
            while (!destination.IsEmpty)
            {
                var wordIndex = _position / 4;
                var byteIndex = _position % 4;
                var word = _buffer[wordIndex];
                var count = Math.Min(destination.Length, 4 - byteIndex);
                for (var index = 0; index < count; index++)
                {
                    // Bytes are read from the buffer in little-endian (host) order, matching
                    // Dolphin's reinterpret_cast<u8*>(m_buffer) access.
                    destination[index] = (byte)(word >> (8 * (byteIndex + index)));
                }

                _position += count;
                destination = destination[count..];
                if (_position == WordCount * 4)
                {
                    Advance();
                    _position = 0;
                }
            }
        }

        private void Advance()
        {
            for (var index = 0; index < Lag; index++)
                _buffer[index] ^= _buffer[index + WordCount - Lag];
            for (var index = Lag; index < WordCount; index++)
                _buffer[index] ^= _buffer[index - Lag];
        }
    }
}
