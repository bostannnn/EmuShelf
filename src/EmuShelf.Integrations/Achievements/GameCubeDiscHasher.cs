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
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        Span<byte> magic = stackalloc byte[4];
        ReadExactlyAt(stream, 0x1C, magic);
        if (!magic.SequenceEqual(new byte[] { 0xC2, 0x33, 0x9F, 0x3D }))
            throw new InvalidDataException("The image is not a GameCube disc.");

        Span<byte> sizes = stackalloc byte[8];
        ReadExactlyAt(stream, BaseHeaderSize + 0x14, sizes);
        var bodySize = BinaryPrimitives.ReadUInt32BigEndian(sizes[..4]);
        var trailerSize = BinaryPrimitives.ReadUInt32BigEndian(sizes[4..]);
        var requestedHeaderSize = (ulong)BaseHeaderSize + 0x20UL + bodySize + trailerSize;
        var headerSize = (int)Math.Min((ulong)MaxHeaderSize, requestedHeaderSize);

        var header = new byte[headerSize];
        ReadExactlyAt(stream, 0, header);
        var dolOffset = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0x420, 4));

        var dolHeader = new byte[DolHeaderSize];
        ReadExactlyAt(stream, dolOffset, dolHeader);

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(header);
        var buffer = new byte[MaxChunkSize];
        for (var index = 0; index < 18; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = BinaryPrimitives.ReadUInt32BigEndian(
                dolHeader.AsSpan(index * 4, 4));
            var remaining = BinaryPrimitives.ReadUInt32BigEndian(
                dolHeader.AsSpan(0x90 + index * 4, 4));
            if (remaining == 0)
                continue;

            stream.Position = offset;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min((uint)buffer.Length, remaining);
                stream.ReadExactly(buffer.AsSpan(0, count));
                md5.AppendData(buffer, 0, count);
                remaining -= (uint)count;
            }
        }

        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    private static void ReadExactlyAt(FileStream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > stream.Length)
            throw new InvalidDataException("The GameCube image ended unexpectedly.");
        stream.Position = offset;
        stream.ReadExactly(buffer);
    }
}
