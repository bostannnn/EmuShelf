using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Validates raw Nintendo DS cartridges and reads their local header evidence. No alternate
/// container or headered layout is normalized in this first DS import profile.
/// </summary>
public static class NintendoDsRomReader
{
    // Retail Nintendo DS cards top out at 512 MiB. This caps both automatic discovery and the
    // one full-file checksum pass without accepting a renamed arbitrary large file.
    public const long MaximumRomBytes = 512L * 1024 * 1024;

    private const int HeaderBytes = 0x200;
    private const int CommercialArm9MinimumOffset = 0x4000;
    private const int HeaderCrcOffset = 0x15E;
    private const int NintendoLogoOffset = 0xC0;
    private const int NintendoLogoBytes = 156;
    private const int NintendoLogoCrcOffset = 0x15C;
    private const int Arm9Offset = 0x20;
    private const int Arm9Size = 0x2C;
    private const int Arm7Offset = 0x30;
    private const int Arm7Size = 0x3C;
    private const int RomSizeOffset = 0x80;
    private const int HeaderSizeOffset = 0x84;
    private const int CardCapacityOffset = 0x14;

    /// <summary>
    /// Reads only the fixed-size header for folder discovery and explicit-file validation.
    /// Full-file SHA-1 extraction is intentionally deferred to <see cref="TryRead"/>.
    /// </summary>
    public static NintendoDsRomHeader? TryRecognize(string path)
    {
        if (!Path.GetExtension(path).Equals(".nds", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            using var stream = OpenRead(path);
            return TryReadHeader(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Returns local header evidence plus the exact SHA-1 of the validated raw ROM.</summary>
    public static NintendoDsRomEvidence? TryRead(string path)
    {
        if (!Path.GetExtension(path).Equals(".nds", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            using var stream = OpenRead(path);
            var header = TryReadHeader(stream);
            if (header is null)
                return null;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            stream.Position = 0;
            AppendExactly(hash, stream, stream.Length);
            return new NintendoDsRomEvidence(
                header.Title,
                header.GameCode,
                header.IsHomebrew,
                Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static NintendoDsRomHeader? TryReadHeader(Stream stream)
    {
        if (stream.Length < HeaderBytes || stream.Length > MaximumRomBytes)
            return null;

        var bytes = new byte[HeaderBytes];
        stream.Position = 0;
        ReadExactly(stream, bytes);
        if (!HasValidChecksums(bytes) || !HasSupportedCardLayout(bytes, stream.Length))
            return null;

        var rawGameCode = bytes.AsSpan(0x0C, 4);
        var isHomebrew = rawGameCode.SequenceEqual("####"u8);
        if (!isHomebrew &&
            (!IsUpperAlphaNumeric(rawGameCode) ||
             !IsUpperAlphaNumeric(bytes.AsSpan(0x10, 2))))
        {
            return null;
        }

        var arm9Offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Arm9Offset, 4));
        var arm9Size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Arm9Size, 4));
        var arm7Offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Arm7Offset, 4));
        var arm7Size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Arm7Size, 4));
        var minimumArmOffset = isHomebrew ? HeaderBytes : CommercialArm9MinimumOffset;
        if (!IsFileRange(arm9Offset, arm9Size, minimumArmOffset, stream.Length) ||
            !IsFileRange(arm7Offset, arm7Size, HeaderBytes, stream.Length))
        {
            return null;
        }

        return new NintendoDsRomHeader(
            ReadDisplayTitle(bytes.AsSpan(0, 12)),
            isHomebrew ? null : Encoding.ASCII.GetString(rawGameCode),
            isHomebrew);
    }

    private static bool HasSupportedCardLayout(ReadOnlySpan<byte> bytes, long length)
    {
        // 0x00 is a DS cart and 0x02 is a DS-compatible DSi-enhanced cart. DSi-exclusive
        // software deliberately remains out of the DS profile until it has its own launch
        // contract and test coverage.
        if (bytes[0x12] is not (0x00 or 0x02) || bytes[CardCapacityOffset] > 12)
            return false;

        var declaredRomSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(RomSizeOffset, 4));
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(HeaderSizeOffset, 4));
        return declaredRomSize is > 0 and <= (uint)MaximumRomBytes &&
               headerSize is >= HeaderBytes and <= (uint)MaximumRomBytes &&
               headerSize <= length;
    }

    private static bool HasValidChecksums(ReadOnlySpan<byte> bytes) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(NintendoLogoCrcOffset, 2)) ==
        CalculateCrc16(bytes.Slice(NintendoLogoOffset, NintendoLogoBytes)) &&
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(HeaderCrcOffset, 2)) ==
        CalculateCrc16(bytes.Slice(0, HeaderCrcOffset));

    private static ushort CalculateCrc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private static bool IsFileRange(uint offset, uint size, int minimumOffset, long length) =>
        size > 0 &&
        offset >= minimumOffset &&
        offset <= length &&
        size <= length - offset;

    private static bool IsUpperAlphaNumeric(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return false;

        foreach (var character in value)
        {
            if (!((character >= (byte)'0' && character <= (byte)'9') ||
                  (character >= (byte)'A' && character <= (byte)'Z')))
                return false;
        }
        return true;
    }

    private static string? ReadDisplayTitle(ReadOnlySpan<byte> value)
    {
        var length = value.IndexOf((byte)0);
        if (length < 0)
            length = value.Length;
        else if (!IsPadding(value[(length + 1)..]))
            return null;

        var title = value[..length];
        foreach (var character in title)
        {
            if (character is < 0x20 or > 0x7E)
                return null;
        }

        var text = Encoding.ASCII.GetString(title).TrimEnd();
        return text.Length == 0 ? null : text;
    }

    private static bool IsPadding(ReadOnlySpan<byte> value)
    {
        foreach (var character in value)
        {
            if (character is not (0 or (byte)' '))
                return false;
        }
        return true;
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 64 * 1024,
        FileOptions.SequentialScan);

    private static void AppendExactly(IncrementalHash hash, Stream stream, long remaining)
    {
        var buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = stream.Read(buffer, 0, requested);
            if (read == 0)
                throw new EndOfStreamException("The Nintendo DS ROM changed while it was being read.");

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> bytes)
    {
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes[read..]);
            if (count == 0)
                throw new EndOfStreamException("The Nintendo DS ROM changed while it was being read.");
            read += count;
        }
    }
}

public sealed record NintendoDsRomHeader(string? Title, string? GameCode, bool IsHomebrew);

public sealed record NintendoDsRomEvidence(
    string? Title,
    string? GameCode,
    bool IsHomebrew,
    string Sha1);
