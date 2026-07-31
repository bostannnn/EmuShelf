using System.Security.Cryptography;
using System.Text;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Validates a raw Game Boy Color cartridge header and reads bounded evidence. A file is accepted
/// only when it carries the fixed Nintendo boot logo, a valid header checksum, and the CGB
/// compatibility flag set — so an original Game Boy title is never classified as Game Boy Color.
/// Copier layouts and archives intentionally have no normalization path.
/// </summary>
public static class GameBoyColorRomReader
{
    // The largest licensed Game Boy Color cartridge is 8 MiB. Bounding at the hardware limit avoids
    // spending scan time hashing a renamed non-cartridge file.
    public const long MaximumRomBytes = 8L * 1024 * 1024;

    private const int HeaderBytes = 0x150;
    private const int LogoOffset = 0x104;
    private const int LogoBytes = 48;
    private const int TitleOffset = 0x134;
    private const int CgbFlagOffset = 0x143;
    private const int HeaderChecksumOffset = 0x14D;
    private const int ChecksumStart = 0x134;
    private const int ChecksumEndInclusive = 0x14C;

    // SHA-256 of the canonical 48-byte Game Boy boot logo (0x104..0x133). Storing only the digest
    // recognizes the logo without redistributing it, exactly as the GBA/DS validator does.
    private static readonly byte[] CanonicalLogoSha256 = Convert.FromHexString(
        "DAF4CABDC852BAA0291849203F0B41FD0B4ECD58E0D7AFF4A509F5DE4D7F9A2E");

    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".gbc", ".gb" };

    /// <summary>Reads only the fixed header for discovery; full-file SHA-1 is deferred to <see cref="TryRead"/>.</summary>
    public static GameBoyColorRomHeader? TryRecognize(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path)))
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
    public static GameBoyColorRomEvidence? TryRead(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path)))
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
            return new GameBoyColorRomEvidence(
                header.Title,
                header.IsColorOnly,
                Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static GameBoyColorRomHeader? TryReadHeader(Stream stream)
    {
        if (stream.Length < HeaderBytes || stream.Length > MaximumRomBytes)
            return null;

        var bytes = new byte[HeaderBytes];
        stream.Position = 0;
        ReadExactly(stream, bytes);

        if (!IsCanonicalLogo(bytes.AsSpan(LogoOffset, LogoBytes)) ||
            bytes[HeaderChecksumOffset] != CalculateHeaderChecksum(bytes))
        {
            return null;
        }

        // 0x80 = Game Boy Color enhanced (backwards compatible); 0xC0 = Game Boy Color only.
        // Any other value is an original Game Boy title, which this platform does not claim.
        var cgbFlag = bytes[CgbFlagOffset];
        if (cgbFlag != 0x80 && cgbFlag != 0xC0)
            return null;

        return new GameBoyColorRomHeader(
            ReadDisplayTitle(bytes.AsSpan(TitleOffset, CgbFlagOffset - TitleOffset)),
            cgbFlag == 0xC0);
    }

    // The Game Boy header checksum: starting from 0, subtract each byte in 0x134..0x14C and one more,
    // as an unsigned byte. The result must equal the stored byte at 0x14D.
    private static byte CalculateHeaderChecksum(ReadOnlySpan<byte> bytes)
    {
        byte checksum = 0;
        for (var offset = ChecksumStart; offset <= ChecksumEndInclusive; offset++)
            checksum = unchecked((byte)(checksum - bytes[offset] - 1));
        return checksum;
    }

    private static bool IsCanonicalLogo(ReadOnlySpan<byte> logo) =>
        logo.Length == LogoBytes &&
        SHA256.HashData(logo).AsSpan().SequenceEqual(CanonicalLogoSha256);

    private static string? ReadDisplayTitle(ReadOnlySpan<byte> value)
    {
        var length = value.IndexOf((byte)0);
        if (length < 0)
            length = value.Length;

        var title = value[..length];
        foreach (var character in title)
        {
            if (character is < 0x20 or > 0x7E)
                return null;
        }

        var text = Encoding.ASCII.GetString(title).TrimEnd();
        return text.Length == 0 ? null : text;
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
                throw new EndOfStreamException("The Game Boy Color ROM changed while it was being read.");

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
                throw new EndOfStreamException("The Game Boy Color ROM changed while it was being read.");
            read += count;
        }
    }
}

public sealed record GameBoyColorRomHeader(string? Title, bool IsColorOnly);

public sealed record GameBoyColorRomEvidence(string? Title, bool IsColorOnly, string Sha1);
