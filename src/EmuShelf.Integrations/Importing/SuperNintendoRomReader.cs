using System.Security.Cryptography;
using System.Text;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Recognizes raw Super Nintendo / Super Famicom cartridge images and produces the SHA-1 of the
/// canonical headerless ROM stream. The SNES has no magic bytes, so recognition is structural: the
/// internal LoROM/HiROM header must carry a consistent checksum/complement pair, a reset vector
/// that points into ROM, and a plausible map-mode byte. An optional 512-byte copier header is
/// normalized away exactly as rcheevos and the No-Intro sets expect. The reader never writes to the
/// supplied file.
/// </summary>
public static class SuperNintendoRomReader
{
    // bsnes/snes9x accept cartridge ROMs up to 8 MiB (the largest retail carts, e.g. Tales of
    // Phantasia at 48 Mbit, are 6 MiB). Keeping the ceiling at the hardware boundary keeps hashing
    // bounded and stops a renamed unrelated file from becoming a slow scan candidate.
    public const long MaximumNormalizedRomBytes = 8L * 1024 * 1024;

    // The internal header at 0x7FC0 (LoROM) or 0xFFC0 (HiROM) plus its interrupt vectors occupies
    // 0x40 bytes, so a checkable LoROM ROM is at least 0x8000 bytes after the copier header.
    private const int MinimumNormalizedRomBytes = 0x8000;

    private const int CopierHeaderBytes = 512;
    private const int LoRomHeaderOffset = 0x7FC0;
    private const int HiRomHeaderOffset = 0xFFC0;
    private const int HeaderBlockBytes = 0x40;

    private const int TitleOffset = 0x00;
    private const int TitleLength = 21;
    private const int MapModeOffset = 0x15;
    private const int ComplementOffset = 0x1C;
    private const int ChecksumOffset = 0x1E;
    private const int EmulationResetVectorOffset = 0x3C;

    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".sfc", ".smc" };

    /// <summary>
    /// Validates only the extension, bounded layout, and internal SNES header. Folder scans and the
    /// explicit-file picker use this fast path; SHA-1 extraction happens once an accepted entry is
    /// being reconciled.
    /// </summary>
    public static SuperNintendoRomHeader? TryRecognize(string path)
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

    /// <summary>Returns local header evidence and the exact SHA-1 of the headerless ROM.</summary>
    public static SuperNintendoRomEvidence? TryRead(string path)
    {
        if (!Extensions.Contains(Path.GetExtension(path)))
            return null;

        try
        {
            using var stream = OpenRead(path);
            var header = TryReadHeader(stream);
            if (header is null)
                return null;

            var copierOffset = HasCopierHeader(stream.Length) ? CopierHeaderBytes : 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            stream.Position = copierOffset;
            AppendExactly(hash, stream, stream.Length - copierOffset);
            return new SuperNintendoRomEvidence(
                header.Title,
                header.Mapping,
                Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static SuperNintendoRomHeader? TryReadHeader(Stream stream)
    {
        var copierOffset = HasCopierHeader(stream.Length) ? CopierHeaderBytes : 0;
        var normalizedLength = stream.Length - copierOffset;
        if (normalizedLength < MinimumNormalizedRomBytes ||
            normalizedLength > MaximumNormalizedRomBytes)
        {
            return null;
        }

        // Score both mappings and keep the stronger one. Both offsets can structurally validate on
        // an over-sized image, so prefer the header whose map-mode bit matches its own location.
        var loRom = TryReadCandidate(stream, copierOffset, LoRomHeaderOffset, SuperNintendoMapping.LoRom);
        var hiRom = normalizedLength >= HiRomHeaderOffset + HeaderBlockBytes
            ? TryReadCandidate(stream, copierOffset, HiRomHeaderOffset, SuperNintendoMapping.HiRom)
            : null;

        return (loRom, hiRom) switch
        {
            ({ MapMatches: true }, { MapMatches: false }) => loRom,
            ({ MapMatches: false }, { MapMatches: true }) => hiRom,
            (not null, _) => loRom,
            _ => hiRom,
        };
    }

    private static SuperNintendoRomHeader? TryReadCandidate(
        Stream stream,
        int copierOffset,
        int headerOffset,
        SuperNintendoMapping mapping)
    {
        Span<byte> header = stackalloc byte[HeaderBlockBytes];
        stream.Position = copierOffset + headerOffset;
        if (!TryReadExactly(stream, header))
            return null;

        var checksum = ReadUInt16(header, ChecksumOffset);
        var complement = ReadUInt16(header, ComplementOffset);
        // The complement is defined as checksum XOR 0xFFFF; a consistent, non-degenerate pair is a
        // strong structural signal that this offset really is the cartridge header.
        if ((checksum ^ complement) != 0xFFFF || checksum is 0x0000 or 0xFFFF)
            return null;

        // Real cartridges map ROM into the $8000-$FFFF window, so the 6502-mode reset vector must
        // point there. Combined with the checksum pair this makes a false positive very unlikely.
        if (ReadUInt16(header, EmulationResetVectorOffset) < 0x8000)
            return null;

        // The map-mode high nibble is always 0x2 or 0x3 on a valid cartridge (0x20 LoROM,
        // 0x21 HiROM, 0x23 SA-1, 0x25 ExHiROM, 0x30/0x31 FastROM, ...).
        var mapMode = header[MapModeOffset];
        if ((mapMode & 0x20) == 0)
            return null;

        var mapMatches = (mapMode & 0x01) == (mapping == SuperNintendoMapping.HiRom ? 1 : 0);
        return new SuperNintendoRomHeader(
            ReadDisplayTitle(header.Slice(TitleOffset, TitleLength)),
            mapping,
            mapMatches);
    }

    // The title is Shift-JIS on Japanese cartridges, so it is read best-effort for display only and
    // never gates recognition. Non-ASCII bytes simply end the readable run.
    private static string? ReadDisplayTitle(ReadOnlySpan<byte> value)
    {
        var builder = new StringBuilder(TitleLength);
        foreach (var character in value)
        {
            if (character is < 0x20 or > 0x7E)
                break;
            builder.Append((char)character);
        }

        var title = builder.ToString().TrimEnd();
        return title.Length == 0 ? null : title;
    }

    private static bool HasCopierHeader(long length) => length % 0x2000 == CopierHeaderBytes;

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)(bytes[offset] | (bytes[offset + 1] << 8));

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
                throw new EndOfStreamException("The Super Nintendo ROM changed while it was being read.");

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static bool TryReadExactly(Stream stream, Span<byte> bytes)
    {
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes[read..]);
            if (count == 0)
                return false;
            read += count;
        }
        return true;
    }
}

public enum SuperNintendoMapping
{
    LoRom,
    HiRom,
}

public sealed record SuperNintendoRomHeader(
    string? Title,
    SuperNintendoMapping Mapping,
    bool MapMatches);

public sealed record SuperNintendoRomEvidence(
    string? Title,
    SuperNintendoMapping Mapping,
    string Sha1);
