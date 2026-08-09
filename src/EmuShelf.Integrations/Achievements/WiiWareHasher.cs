using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Read-only port of rcheevos' <c>rc_hash_wiiware</c> (src/rhash/hash_disc.c), the algorithm
/// RetroAchievements uses for Wii installable titles (WiiWare, Virtual Console, channels) loaded as a
/// <c>.wad</c>. The content sections of a WAD are stored encrypted, and rcheevos hashes those
/// encrypted bytes verbatim — the MD5 covers the TMD followed by the leading bytes of each content —
/// so no title-key or Wii common-key decryption is involved. A WAD is read as a plain file, not a
/// disc image, so this never goes through <see cref="NintendoDiscImageReader"/>.
/// </summary>
internal static class WiiWareHasher
{
    // rcheevos MAX_BUFFER_SIZE: both the TMD read and each content read are capped at 64 MiB.
    private const long MaxBufferSize = 64 * 1024 * 1024;
    private const long Alignment = 0x40;
    // Offsets within an RSA-2048-signed TMD (the form every Wii WAD uses).
    private const int TmdContentCountOffset = 0x1DE;
    private const int TmdContentRecordsOffset = 0x1E4;
    private const int ContentRecordSize = 0x24;
    private const int ContentRecordSizeFieldOffset = 0x08;

    public static string Hash(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // rc_hash_wii routes to the WAD algorithm when bytes 0x04..0x08 are "Is\0\0" (installable
        // type + version 0). A .wad that is not an installable WAD has no verified hash.
        Span<byte> magic = stackalloc byte[4];
        ReadExactlyAt(stream, 0x04, magic);
        if (magic[0] != (byte)'I' || magic[1] != (byte)'s' || magic[2] != 0 || magic[3] != 0)
            throw new UnsupportedDiscLayoutException("This file is not a supported Wii WAD.");

        var certChainSize = AlignUp(ReadUInt32BigEndianAt(stream, 0x08));
        var ticketSize = AlignUp(ReadUInt32BigEndianAt(stream, 0x10));
        var tmdSize = Math.Min(AlignUp(ReadUInt32BigEndianAt(stream, 0x14)), MaxBufferSize);
        // rcheevos assumes the reserved (crl) size is zero and does not include it in this offset.
        var tmdStart = 0x40 + certChainSize + ticketSize;

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        // Hash the TMD.
        AppendRange(md5, stream, tmdStart, tmdSize, cancellationToken);

        // Hash the leading (encrypted) bytes of each content section.
        var contentCount = ReadUInt16BigEndianAt(stream, tmdStart + TmdContentCountOffset);
        var contentAddr = tmdStart + tmdSize;
        for (var index = 0; index < contentCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sizeFieldOffset = tmdStart + TmdContentRecordsOffset +
                                  ContentRecordSizeFieldOffset + (long)index * ContentRecordSize;
            long contentSize;
            if (ReadUInt32BigEndianAt(stream, sizeFieldOffset) == 0)
            {
                // Size < 4 GiB: the low word is the real size, rounded up to an AES block because the
                // stored content is encrypted.
                contentSize = (ReadUInt32BigEndianAt(stream, sizeFieldOffset + 4) + 0x0FL) & ~0x0FL;
            }
            else
            {
                // Size >= 4 GiB: rcheevos hashes only the first MAX_BUFFER_SIZE bytes.
                contentSize = MaxBufferSize;
            }

            AppendRange(md5, stream, contentAddr, Math.Min(contentSize, MaxBufferSize), cancellationToken);
            contentAddr = AlignUp(contentAddr + contentSize);
        }

        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendRange(
        IncrementalHash md5,
        Stream stream,
        long offset,
        long count,
        CancellationToken cancellationToken)
    {
        if (offset < 0 || count < 0 || offset > stream.Length || count > stream.Length - offset)
            throw new InvalidDataException("The Wii WAD ended unexpectedly.");
        if (count == 0)
            return;

        stream.Position = offset;
        var buffer = new byte[(int)Math.Min(count, 1 << 20)];
        var remaining = count;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toRead = (int)Math.Min(remaining, buffer.Length);
            ReadExactly(stream, buffer.AsSpan(0, toRead));
            md5.AppendData(buffer, 0, toRead);
            remaining -= toRead;
        }
    }

    private static uint ReadUInt32BigEndianAt(Stream stream, long offset)
    {
        Span<byte> value = stackalloc byte[4];
        ReadExactlyAt(stream, offset, value);
        return BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    private static ushort ReadUInt16BigEndianAt(Stream stream, long offset)
    {
        Span<byte> value = stackalloc byte[2];
        ReadExactlyAt(stream, offset, value);
        return BinaryPrimitives.ReadUInt16BigEndian(value);
    }

    private static void ReadExactlyAt(Stream stream, long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > stream.Length - destination.Length)
            throw new InvalidDataException("The Wii WAD ended unexpectedly.");
        stream.Position = offset;
        ReadExactly(stream, destination);
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = stream.Read(destination[read..]);
            if (count == 0)
                throw new InvalidDataException("The Wii WAD ended unexpectedly.");
            read += count;
        }
    }

    private static long AlignUp(long value) => (value + (Alignment - 1)) & ~(Alignment - 1);
}
