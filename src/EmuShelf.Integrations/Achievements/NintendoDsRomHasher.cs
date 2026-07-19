using System.Buffers.Binary;
using System.Security.Cryptography;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Reads the same DS header, ARM boot-code, and icon/title ranges as rcheevos' pinned
/// <c>rc_hash_nintendo_ds</c>. EmuShelf intentionally accepts only its already-validated raw
/// import layout; SuperCard headers and other alternate layouts remain unsupported.
/// </summary>
internal static class NintendoDsRomHasher
{
    private const int FullHeaderBytes = 0x200;
    private const int HashedHeaderBytes = 0x160;
    private const int IconBytes = 0xA00;
    private const int Arm9Offset = 0x20;
    private const int Arm9Size = 0x2C;
    private const int Arm7Offset = 0x30;
    private const int Arm7Size = 0x3C;
    private const int IconOffset = 0x68;
    private const uint MaximumArmCodeBytes = 16 * 1024 * 1024;

    public static string Hash(string path, CancellationToken cancellationToken)
    {
        if (NintendoDsRomReader.TryRecognize(path) is not { IsHomebrew: false })
        {
            throw new UnsupportedDiscLayoutException(
                "This Nintendo DS image is not a supported retail raw cartridge layout.");
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.RandomAccess);
        var header = new byte[FullHeaderBytes];
        ReadExactly(stream, header);

        var arm9Address = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(Arm9Offset, 4));
        var arm9Size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(Arm9Size, 4));
        var arm7Address = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(Arm7Offset, 4));
        var arm7Size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(Arm7Size, 4));
        var iconAddress = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(IconOffset, 4));
        if ((ulong)arm9Size + arm7Size > MaximumArmCodeBytes)
        {
            throw new UnsupportedDiscLayoutException(
                "The Nintendo DS cartridge code ranges exceed rcheevos' verified limit.");
        }

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(header, 0, HashedHeaderBytes);
        AppendRange(md5, stream, arm9Address, arm9Size, cancellationToken);
        AppendRange(md5, stream, arm7Address, arm7Size, cancellationToken);
        AppendIconRange(md5, stream, iconAddress, cancellationToken);
        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendRange(
        IncrementalHash md5,
        Stream stream,
        uint offset,
        uint length,
        CancellationToken cancellationToken)
    {
        stream.Position = offset;
        var remaining = (long)length;
        var buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = stream.Read(buffer, 0, requested);
            if (read != requested)
                throw new InvalidDataException("The Nintendo DS cartridge changed while it was being identified.");
            md5.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void AppendIconRange(
        IncrementalHash md5,
        Stream stream,
        uint offset,
        CancellationToken cancellationToken)
    {
        // rcheevos zero-pads a truncated icon/title block. It is the one permitted short read in
        // the algorithm; use the same all-zero initialized buffer here.
        var buffer = new byte[IconBytes];
        if (offset < stream.Length)
        {
            stream.Position = offset;
            var total = 0;
            while (total < buffer.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, total, buffer.Length - total);
                if (read == 0)
                    break;
                total += read;
            }
        }
        md5.AppendData(buffer);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
                throw new InvalidDataException("The Nintendo DS header could not be read.");
            total += read;
        }
    }
}
