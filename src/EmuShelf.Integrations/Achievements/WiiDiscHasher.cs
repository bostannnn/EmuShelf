using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Read-only port of rcheevos' Wii disc hash. Encrypted discs hash the same 1024 encrypted
/// cluster payloads as upstream; decrypted discs use the shared Nintendo partition hash.
/// </summary>
internal static class WiiDiscHasher
{
    private const int MainHeaderSize = 0x80;
    private const long RegionCodeAddress = 0x4E000;
    private const int ClusterSize = 0x7C00;
    private const int FullClusterSize = 0x8000;
    private const int MaximumClusterCount = 1024;
    private const int MaximumPartitionCount = 4096;

    public static string Hash(string path, CancellationToken cancellationToken)
    {
        using var disc = NintendoDiscImageReader.TryOpen(path)
            ?? throw new UnsupportedDiscLayoutException(
                "This Wii container does not have a verified logical-disc reader.");

        Span<byte> magic = stackalloc byte[4];
        ReadExactlyAt(disc, 0x18, magic);
        if (!magic.SequenceEqual(new byte[] { 0x5D, 0x1C, 0x9E, 0xA3 }))
            throw new InvalidDataException("The image is not a Wii disc.");

        Span<byte> encryptionByte = stackalloc byte[1];
        ReadExactlyAt(disc, 0x61, encryptionByte);
        var encrypted = encryptionByte[0] == 0;

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var header = new byte[MainHeaderSize];
        ReadExactlyAt(disc, 0, header);
        md5.AppendData(header);

        Span<byte> region = stackalloc byte[4];
        ReadExactlyAt(disc, RegionCodeAddress, region);
        md5.AppendData(region);

        var partitionEntries = ReadPartitionEntries(disc);
        if (partitionEntries.Count == 0)
            throw new InvalidDataException("The Wii image has no partitions.");

        var buffer = new byte[ClusterSize];
        Span<byte> partitionHeader = stackalloc byte[8];
        foreach (var entry in partitionEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Type == 1) // Update partitions are not part of the canonical hash.
                continue;

            ReadExactlyAt(disc, entry.Offset + 0x2A4, partitionHeader);
            var tmdSize = BinaryPrimitives.ReadUInt32BigEndian(partitionHeader[..4]);
            var tmdOffset = checked((long)BinaryPrimitives.ReadUInt32BigEndian(partitionHeader[4..]) << 2);
            if (tmdSize > ClusterSize)
                tmdSize = ClusterSize;

            var tmd = new byte[tmdSize];
            ReadExactlyAt(disc, checked(entry.Offset + tmdOffset), tmd);
            md5.AppendData(tmd);

            ReadExactlyAt(disc, entry.Offset + 0x2B8, partitionHeader);
            var dataOffset = checked((long)BinaryPrimitives.ReadUInt32BigEndian(partitionHeader[..4]) << 2);
            var dataSize = checked((long)BinaryPrimitives.ReadUInt32BigEndian(partitionHeader[4..]) << 2);
            if (encrypted)
            {
                var clusterCount = (int)Math.Min(dataSize / FullClusterSize, MaximumClusterCount);
                for (var index = 0; index < clusterCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ReadExactlyAt(disc, checked(dataOffset + (long)index * FullClusterSize + 0x400), buffer);
                    md5.AppendData(buffer);
                }
            }
            else
            {
                GameCubeDiscHasher.AppendPartitionHash(
                    md5,
                    disc,
                    dataOffset,
                    offsetShift: 2,
                    cancellationToken);
            }
        }

        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    private static List<PartitionEntry> ReadPartitionEntries(NintendoDiscImageReader disc)
    {
        Span<byte> infoTable = stackalloc byte[32];
        ReadExactlyAt(disc, 0x40000, infoTable);

        var totalCount = 0;
        for (var index = 0; index < 4; index++)
        {
            totalCount = checked(totalCount + (int)BinaryPrimitives.ReadUInt32BigEndian(infoTable.Slice(index * 8, 4)));
            if (totalCount > MaximumPartitionCount)
                throw new InvalidDataException("The Wii image has too many partitions.");
        }

        var result = new List<PartitionEntry>(totalCount);
        Span<byte> partitionEntry = stackalloc byte[8];
        for (var index = 0; index < 4; index++)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(infoTable.Slice(index * 8, 4));
            var tableOffset = checked((long)BinaryPrimitives.ReadUInt32BigEndian(infoTable.Slice(index * 8 + 4, 4)) << 2);
            for (var partition = 0; partition < count; partition++)
            {
                ReadExactlyAt(disc, checked(tableOffset + (long)partition * 8), partitionEntry);
                result.Add(new PartitionEntry(
                    checked((long)BinaryPrimitives.ReadUInt32BigEndian(partitionEntry[..4]) << 2),
                    BinaryPrimitives.ReadUInt32BigEndian(partitionEntry[4..])));
            }
        }

        return result;
    }

    private static void ReadExactlyAt(NintendoDiscImageReader disc, long offset, Span<byte> destination)
    {
        if (!disc.ReadAt(offset, destination))
            throw new InvalidDataException("The Wii image ended unexpectedly.");
    }

    private readonly record struct PartitionEntry(long Offset, uint Type);
}
