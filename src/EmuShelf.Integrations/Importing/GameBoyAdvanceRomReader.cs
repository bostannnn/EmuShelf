using System.Security.Cryptography;
using System.Text;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Validates the raw Game Boy Advance cartridge layout and reads bounded header evidence. Copier
/// headers, converted images, and archives intentionally have no normalization path yet.
/// </summary>
public static class GameBoyAdvanceRomReader
{
    // The GBA's ROM address space is 32 MiB. Keeping the limit at the hardware boundary avoids
    // spending scan time hashing a renamed non-cartridge file.
    public const long MaximumRomBytes = 32L * 1024 * 1024;

    private const int HeaderBytes = 0xC0;
    private const int TitleOffset = 0xA0;
    private const int GameCodeOffset = 0xAC;
    private const int HeaderChecksumOffset = 0xBD;

    /// <summary>
    /// Reads only the raw fixed header for discovery. Full-file SHA-1 extraction is deferred to
    /// <see cref="TryRead"/> after an accepted import is reconciled.
    /// </summary>
    public static GameBoyAdvanceRomHeader? TryRecognize(string path)
    {
        if (!Path.GetExtension(path).Equals(".gba", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>Returns local header evidence and the exact SHA-1 of the validated raw ROM.</summary>
    public static GameBoyAdvanceRomEvidence? TryRead(string path)
    {
        if (!Path.GetExtension(path).Equals(".gba", StringComparison.OrdinalIgnoreCase))
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
            return new GameBoyAdvanceRomEvidence(
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

    private static GameBoyAdvanceRomHeader? TryReadHeader(Stream stream)
    {
        if (stream.Length < HeaderBytes || stream.Length > MaximumRomBytes)
            return null;

        var bytes = new byte[HeaderBytes];
        stream.Position = 0;
        ReadExactly(stream, bytes);
        if (bytes[3] != 0xEA ||
            bytes[0xB2] != 0x96 ||
            bytes[0xB3] != 0 ||
            bytes[0xBE] != 0 ||
            bytes[0xBF] != 0 ||
            bytes[HeaderChecksumOffset] != CalculateHeaderChecksum(bytes))
        {
            return null;
        }

        var rawGameCode = bytes.AsSpan(GameCodeOffset, 4);
        var isHomebrew = rawGameCode.SequenceEqual("####"u8);
        if (!isHomebrew && !IsUpperAlphaNumeric(rawGameCode))
            return null;

        return new GameBoyAdvanceRomHeader(
            ReadDisplayTitle(bytes.AsSpan(TitleOffset, 12)),
            isHomebrew ? null : Encoding.ASCII.GetString(rawGameCode),
            isHomebrew);
    }

    // The standard GBA complement check covers title, game code, maker code, fixed value,
    // device type, reserved bytes, and version (0xA0..0xBC).
    private static byte CalculateHeaderChecksum(ReadOnlySpan<byte> bytes)
    {
        byte checksum = 0x19;
        foreach (var value in bytes.Slice(TitleOffset, HeaderChecksumOffset - TitleOffset))
            checksum -= value;
        return checksum;
    }

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
                throw new EndOfStreamException("The Game Boy Advance ROM changed while it was being read.");

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
                throw new EndOfStreamException("The Game Boy Advance ROM changed while it was being read.");
            read += count;
        }
    }
}

public sealed record GameBoyAdvanceRomHeader(string? Title, string? GameCode, bool IsHomebrew);

public sealed record GameBoyAdvanceRomEvidence(
    string? Title,
    string? GameCode,
    bool IsHomebrew,
    string Sha1);
