using System.Buffers.Binary;
using System.Text;

namespace EmuShelf.Integrations.Importing;

/// <summary>The Azahar container family a recognized 3DS file belongs to.</summary>
public enum Nintendo3dsFormat
{
    /// <summary>NCSD cartridge dump (<c>.3ds</c>, <c>.cci</c>).</summary>
    NcsdCartridge,

    /// <summary>Single NCCH title (<c>.cxi</c>, <c>.app</c>).</summary>
    Ncch,

    /// <summary>CIA installable archive (<c>.cia</c>).</summary>
    Cia,

    /// <summary>Homebrew executable (<c>.3dsx</c>, <c>.elf</c>, <c>.axf</c>).</summary>
    Homebrew,

    /// <summary>Seekable-Zstandard compressed variant (<c>.z3ds</c>, <c>.zcci</c>, <c>.zcxi</c>,
    /// <c>.zcia</c>, <c>.z3dsx</c>).</summary>
    Compressed,
}

/// <summary>
/// Recognizes the Nintendo 3DS formats Azahar loads and reads their plaintext header identity
/// where it is cheaply available. Every family is recognized by a bounded magic/structure check so
/// a renamed arbitrary file is never imported, but only the uncompressed NCSD/NCCH dumps carry an
/// exact product code and title id in a fixed header location. CIA, homebrew, and the compressed
/// containers are recognized and launchable but supply no identity here (the library falls back to
/// the filename for their cover match until a dedicated reader lands).
///
/// 3DS ROMs are multi-gigabyte, so this reader never hashes the whole file: import-time identity
/// comes only from targeted reads of the NCSD/NCCH header, which stays plaintext even on encrypted
/// dumps. Whole-file hashing of a clean .3ds/.cci dump is a separate concern, done on demand and
/// with consent by the ScreenScraper fingerprint path, not here.
/// </summary>
public static class Nintendo3dsRomReader
{
    private const int HeaderProbeBytes = 0x200;
    private const int MediaUnit = 0x200;

    // NCSD/NCCH header field offsets (relative to the container/partition start).
    private const int MagicOffset = 0x100;
    private const int NcsdPartitionTableOffset = 0x120;
    private const int NcchTitleIdOffset = 0x118;
    private const int NcchProductCodeOffset = 0x150;
    private const int NcchProductCodeLength = 0x10;

    private static readonly byte[] NcsdMagic = "NCSD"u8.ToArray();
    private static readonly byte[] NcchMagic = "NCCH"u8.ToArray();
    private static readonly byte[] HomebrewMagic = "3DSX"u8.ToArray();
    private static readonly byte[] ElfMagic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];
    private static readonly byte[] ZstandardFrameMagic = [0x28, 0xB5, 0x2F, 0xFD];

    private static readonly IReadOnlyDictionary<string, Nintendo3dsFormat> FormatsByExtension =
        new Dictionary<string, Nintendo3dsFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".3ds"] = Nintendo3dsFormat.NcsdCartridge,
            [".cci"] = Nintendo3dsFormat.NcsdCartridge,
            [".cxi"] = Nintendo3dsFormat.Ncch,
            [".app"] = Nintendo3dsFormat.Ncch,
            [".cia"] = Nintendo3dsFormat.Cia,
            [".3dsx"] = Nintendo3dsFormat.Homebrew,
            [".elf"] = Nintendo3dsFormat.Homebrew,
            [".axf"] = Nintendo3dsFormat.Homebrew,
            [".z3ds"] = Nintendo3dsFormat.Compressed,
            [".zcci"] = Nintendo3dsFormat.Compressed,
            [".zcxi"] = Nintendo3dsFormat.Compressed,
            [".zcia"] = Nintendo3dsFormat.Compressed,
            [".z3dsx"] = Nintendo3dsFormat.Compressed,
        };

    /// <summary>The extensions Azahar can load, used to build the import extension map.</summary>
    public static IReadOnlyCollection<string> SupportedExtensions { get; } =
        FormatsByExtension.Keys.ToArray();

    /// <summary>
    /// Validates the format's magic/structure for folder discovery and explicit-file confirmation.
    /// Returns the recognized family or <c>null</c> for an unsupported extension or a file whose
    /// content does not match the container it claims to be.
    /// </summary>
    public static Nintendo3dsRecognition? TryRecognize(string path)
    {
        var extension = Path.GetExtension(path);
        if (!FormatsByExtension.TryGetValue(extension, out var format))
            return null;

        try
        {
            using var stream = OpenRead(path);
            var header = ReadHeaderProbe(stream);
            return IsValidHeader(format, header) ? new Nintendo3dsRecognition(format) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the recognized family plus, for uncompressed NCSD/NCCH dumps, the plaintext product
    /// code and title id. Other families return the family with no identity so callers fall back to
    /// the filename.
    /// </summary>
    public static Nintendo3dsEvidence? TryRead(string path)
    {
        var extension = Path.GetExtension(path);
        if (!FormatsByExtension.TryGetValue(extension, out var format))
            return null;

        try
        {
            using var stream = OpenRead(path);
            var header = ReadHeaderProbe(stream);
            if (!IsValidHeader(format, header))
                return null;

            return format switch
            {
                Nintendo3dsFormat.Ncch => ReadNcchEvidence(header.AsSpan(), Nintendo3dsFormat.Ncch),
                Nintendo3dsFormat.NcsdCartridge => ReadNcsdEvidence(stream, header),
                _ => new Nintendo3dsEvidence(format, ProductCode: null, TitleId: null),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsValidHeader(Nintendo3dsFormat format, byte[] header) => format switch
    {
        Nintendo3dsFormat.NcsdCartridge => HasMagicAt(header, MagicOffset, NcsdMagic),
        Nintendo3dsFormat.Ncch => HasMagicAt(header, MagicOffset, NcchMagic),
        Nintendo3dsFormat.Cia => IsCiaHeader(header),
        Nintendo3dsFormat.Homebrew =>
            HasMagicAt(header, 0, HomebrewMagic) || HasMagicAt(header, 0, ElfMagic),
        Nintendo3dsFormat.Compressed => IsSeekableZstandard(header),
        _ => false,
    };

    // The CIA header is a fixed 0x2020-byte structure whose first fields are a header size, a
    // type, and a version. Validating those three rejects an arbitrary file without opening the
    // embedded ticket/TMD, which a later pass will parse for the installable title's identity.
    private static bool IsCiaHeader(ReadOnlySpan<byte> header) =>
        header.Length >= 8 &&
        BinaryPrimitives.ReadUInt32LittleEndian(header[..4]) == 0x2020u &&
        BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2)) == 0 &&
        BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2)) == 0;

    // Azahar's compressed containers are seekable-Zstandard files handed straight to zstd. A
    // seekable-zstd file begins with either a standard frame or a skippable metadata frame, so a
    // valid file starts with one of the two Zstandard frame magics.
    private static bool IsSeekableZstandard(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4)
            return false;
        if (header[..4].SequenceEqual(ZstandardFrameMagic))
            return true;
        // Skippable frame magic 0x184D2A50..0x184D2A5F (little-endian on disk: 50..5F 2A 4D 18).
        return (header[0] & 0xF0) == 0x50 && header[1] == 0x2A && header[2] == 0x4D && header[3] == 0x18;
    }

    private static Nintendo3dsEvidence ReadNcsdEvidence(Stream stream, byte[] header)
    {
        // The cartridge's title lives in partition 0, an NCCH at the media-unit offset named by the
        // NCSD partition table. If that partition is unreadable the dump is still a recognized
        // cartridge; it just carries no header identity. A file long enough to carry the NCSD magic
        // but not the partition table (only possible on a truncated dump) is treated the same way.
        if (header.Length < NcsdPartitionTableOffset + 4)
            return new Nintendo3dsEvidence(Nintendo3dsFormat.NcsdCartridge, null, null);

        var partitionUnits = BinaryPrimitives.ReadUInt32LittleEndian(
            header.AsSpan(NcsdPartitionTableOffset, 4));
        var partitionOffset = (long)partitionUnits * MediaUnit;
        if (partitionUnits == 0 || partitionOffset + HeaderProbeBytes > stream.Length)
            return new Nintendo3dsEvidence(Nintendo3dsFormat.NcsdCartridge, null, null);

        var partitionHeader = ReadAt(stream, partitionOffset, HeaderProbeBytes);
        if (!HasMagicAt(partitionHeader, MagicOffset, NcchMagic))
            return new Nintendo3dsEvidence(Nintendo3dsFormat.NcsdCartridge, null, null);

        return ReadNcchEvidence(partitionHeader, Nintendo3dsFormat.NcsdCartridge);
    }

    // The NCSD partition read always supplies a full 0x200-byte header, but a direct NCCH file can
    // be shorter than the product-code field on a truncated dump, so each field is guarded rather
    // than assuming the whole header is present.
    private static Nintendo3dsEvidence ReadNcchEvidence(ReadOnlySpan<byte> ncch, Nintendo3dsFormat format)
    {
        string? titleId = null;
        if (ncch.Length >= NcchTitleIdOffset + 8)
        {
            var rawTitleId = BinaryPrimitives.ReadUInt64LittleEndian(ncch.Slice(NcchTitleIdOffset, 8));
            titleId = rawTitleId == 0 ? null : rawTitleId.ToString("X16");
        }

        var productCode = ncch.Length >= NcchProductCodeOffset + NcchProductCodeLength
            ? ReadProductCode(ncch.Slice(NcchProductCodeOffset, NcchProductCodeLength))
            : null;

        return new Nintendo3dsEvidence(format, productCode, titleId);
    }

    // The product code is fixed-width ASCII (for example "CTR-P-AQNE"), null-padded. Anything with
    // a non-printable byte or no content is treated as absent rather than guessed.
    private static string? ReadProductCode(ReadOnlySpan<byte> value)
    {
        var length = value.IndexOf((byte)0);
        if (length < 0)
            length = value.Length;

        var code = value[..length];
        if (code.IsEmpty)
            return null;

        foreach (var character in code)
        {
            if (character is < 0x20 or > 0x7E)
                return null;
        }

        var text = Encoding.ASCII.GetString(code).Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool HasMagicAt(ReadOnlySpan<byte> header, int offset, ReadOnlySpan<byte> magic) =>
        header.Length >= offset + magic.Length &&
        header.Slice(offset, magic.Length).SequenceEqual(magic);

    private static byte[] ReadHeaderProbe(Stream stream)
    {
        var length = (int)Math.Min(HeaderProbeBytes, stream.Length);
        var buffer = new byte[length];
        stream.Position = 0;
        ReadExactly(stream, buffer);
        return buffer;
    }

    private static byte[] ReadAt(Stream stream, long offset, int count)
    {
        var buffer = new byte[count];
        stream.Position = offset;
        ReadExactly(stream, buffer);
        return buffer;
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 4096,
        FileOptions.None);

    private static void ReadExactly(Stream stream, Span<byte> bytes)
    {
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes[read..]);
            if (count == 0)
                throw new EndOfStreamException("The Nintendo 3DS ROM changed while it was being read.");
            read += count;
        }
    }
}

public sealed record Nintendo3dsRecognition(Nintendo3dsFormat Format);

public sealed record Nintendo3dsEvidence(
    Nintendo3dsFormat Format,
    string? ProductCode,
    string? TitleId);
