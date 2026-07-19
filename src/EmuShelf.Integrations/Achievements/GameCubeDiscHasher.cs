using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EmuShelf.Integrations.Achievements;

internal static class GameCubeDiscHasher
{
    private const int BaseHeaderSize = 0x2440;
    private const int MaxHeaderSize = 1024 * 1024;
    private const int DolHeaderSize = 0xD8;
    private const int MaxChunkSize = 1024 * 1024;

    public static string Hash(string path, CancellationToken cancellationToken)
    {
        using var disc = NintendoDiscImageReader.TryOpen(path)
            ?? throw new UnsupportedDiscLayoutException(
                "This GameCube container does not have a verified logical-disc reader.");
        return Hash(disc, 0, 0, cancellationToken);
    }

    public static string Hash(
        NintendoDiscImageReader disc,
        long partitionOffset,
        int offsetShift,
        CancellationToken cancellationToken)
    {
        if (partitionOffset < 0 || offsetShift is < 0 or > 3)
            throw new InvalidDataException("The Nintendo disc partition is invalid.");

        if (offsetShift == 0)
        {
            Span<byte> magic = stackalloc byte[4];
            ReadExactlyAt(disc, partitionOffset + 0x1C, magic);
            if (!magic.SequenceEqual(new byte[] { 0xC2, 0x33, 0x9F, 0x3D }))
                throw new InvalidDataException("The image is not a GameCube disc.");
        }

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        AppendPartitionHash(md5, disc, partitionOffset, offsetShift, cancellationToken);
        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    public static void AppendPartitionHash(
        IncrementalHash md5,
        NintendoDiscImageReader disc,
        long partitionOffset,
        int offsetShift,
        CancellationToken cancellationToken)
    {
        if (partitionOffset < 0 || offsetShift is < 0 or > 3)
            throw new InvalidDataException("The Nintendo disc partition is invalid.");

        Span<byte> sizes = stackalloc byte[8];
        ReadExactlyAt(disc, partitionOffset + BaseHeaderSize + 0x14, sizes);
        var bodySize = BinaryPrimitives.ReadUInt32BigEndian(sizes[..4]);
        var trailerSize = BinaryPrimitives.ReadUInt32BigEndian(sizes[4..]);
        var requestedHeaderSize = (ulong)BaseHeaderSize + 0x20UL + bodySize + trailerSize;
        var headerSize = (int)Math.Min((ulong)MaxHeaderSize, requestedHeaderSize);

        var header = new byte[headerSize];
        ReadExactlyAt(disc, partitionOffset, header);
        var dolOffset = checked((long)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x420, 4)) << offsetShift);

        var dolHeader = new byte[DolHeaderSize];
        ReadExactlyAt(disc, partitionOffset + dolOffset, dolHeader);

        md5.AppendData(header);
        var buffer = new byte[MaxChunkSize];
        for (var index = 0; index < 18; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = BinaryPrimitives.ReadUInt32BigEndian(
                dolHeader.AsSpan(index * 4, 4));
            // Wii addresses (offsetShift == 2) are stored in 4-byte units, so a segment's SIZE is
            // scaled by the same shift as its offset — rcheevos applies wii_shift to dol_sizes too.
            // (GameCube uses offsetShift 0, leaving both unchanged.)
            var remaining = (long)BinaryPrimitives.ReadUInt32BigEndian(
                dolHeader.AsSpan(0x90 + index * 4, 4)) << offsetShift;
            if (remaining == 0)
                continue;

            var sectionOffset = checked((long)offset << offsetShift);
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buffer.Length, remaining);
                ReadExactlyAt(disc, partitionOffset + sectionOffset, buffer.AsSpan(0, count));
                md5.AppendData(buffer, 0, count);
                remaining -= count;
                sectionOffset += count;
            }
        }
    }

    private static void ReadExactlyAt(NintendoDiscImageReader disc, long offset, Span<byte> buffer)
    {
        if (!disc.ReadAt(offset, buffer))
            throw new InvalidDataException("The GameCube image ended unexpectedly.");
    }
}
