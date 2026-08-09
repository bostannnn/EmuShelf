using System.Buffers.Binary;
using System.Text;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Recognizes the Wii installable-package (WAD) container Dolphin boots directly — WiiWare, Virtual
/// Console, and channel titles — and reads the title identity from its embedded TMD without modifying
/// the file. A WAD is not a disc image: it has no disc header, so it never goes through
/// <see cref="NintendoDiscDetector"/>. Every file is validated by its header and section layout, so a
/// renamed arbitrary <c>.wad</c> (for example a Doom IWAD/PWAD) is never accepted as a Wii title.
///
/// The four-character title code carried in the low word of the 64-bit title id (for example "WB4E")
/// is the same id-addressed key GameTDB serves WiiWare/VC/channel covers by, so it is exposed as the
/// disc-id evidence the existing GameTDB cover route already understands. A WAD whose title id is not
/// a printable ASCII code (a system title or IOS) is still recognized but yields no code, so the
/// library falls back to the filename — exactly as a homebrew/CIA 3DS file does.
/// </summary>
public static class WiiWadReader
{
    private const int HeaderSize = 0x20;
    private const int Alignment = 0x40;

    // Metadata sections are small (a ticket is 0x2A4, a TMD a few KiB, the cert chain ~2.5 KiB), so a
    // generous 256 KiB cap rejects a junk file that claims an absurd section without excluding any
    // real WAD. The encrypted content can be large (a Virtual Console/WiiWare payload); its declared
    // size is a u32, already bounded, and the file-length check below is its real guard.
    private const long MaximumMetadataSectionSize = 256 * 1024;

    // A TMD begins with a signature blob (signature type + signature + padding to a 0x40 boundary);
    // the TMD header — and its title id — follows it. Wii TMDs are RSA-2048 in practice; the other
    // two signing types are handled so an unusual TMD still resolves its title-id offset rather than
    // being misread from the wrong place.
    private const uint SignatureRsa4096 = 0x00010000;
    private const uint SignatureRsa2048 = 0x00010001;
    private const uint SignatureEcc = 0x00010002;
    private const int TitleIdOffsetInTmdHeader = 0x4C;

    /// <summary>The extensions this reader claims, used to build the import extension map.</summary>
    public static IReadOnlyCollection<string> SupportedExtensions { get; } = [".wad"];

    /// <summary>
    /// Validates the WAD header and section layout for folder discovery and explicit-file
    /// confirmation. Returns <c>true</c> only for a structurally valid installable WAD; an
    /// unsupported extension or a file whose content is not a WAD returns <c>false</c>.
    /// </summary>
    public static bool TryRecognize(string path)
    {
        if (!IsWadExtension(path))
            return false;

        try
        {
            using var stream = OpenRead(path);
            return TryReadLayout(stream, out _);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the game code (the GameTDB cover key) and the full sixteen-hex title id for a
    /// recognized WAD, or <c>null</c> when the file is not a valid WAD. A recognized WAD whose title
    /// id is not a printable four-character code returns evidence with a <c>null</c> code so callers
    /// fall back to the filename.
    /// </summary>
    public static WiiWadEvidence? TryRead(string path)
    {
        if (!IsWadExtension(path))
            return null;

        try
        {
            using var stream = OpenRead(path);
            if (!TryReadLayout(stream, out var layout))
                return null;

            var tmd = new byte[layout.TmdSize];
            if (!TryReadExactlyAt(stream, layout.TmdOffset, tmd))
                return new WiiWadEvidence(GameCode: null, TitleId: null);

            return ReadTmdEvidence(tmd);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or OverflowException)
        {
            return null;
        }
    }

    private static bool TryReadLayout(Stream stream, out WadLayout layout)
    {
        layout = default;
        if (stream.Length < Alignment)
            return false;

        Span<byte> header = stackalloc byte[HeaderSize];
        if (!TryReadExactlyAt(stream, 0, header))
            return false;

        // header_size == 0x20 and an "Is" (installable) type together reject a Doom WAD (whose first
        // bytes are "IWAD"/"PWAD") and any other file that merely borrows the extension.
        if (BinaryPrimitives.ReadUInt32BigEndian(header[..4]) != HeaderSize)
            return false;
        if (header[0x04] != (byte)'I' || header[0x05] != (byte)'s')
            return false;

        var certSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(0x08, 4));
        var crlSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(0x0C, 4));
        var ticketSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(0x10, 4));
        var tmdSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(0x14, 4));
        var dataSize = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(0x18, 4));

        if (ticketSize == 0 || tmdSize == 0 ||
            certSize > MaximumMetadataSectionSize || crlSize > MaximumMetadataSectionSize ||
            ticketSize > MaximumMetadataSectionSize || tmdSize > MaximumMetadataSectionSize)
        {
            return false;
        }

        // Each section starts on the next 0x40 boundary after the previous one, beginning at 0x40.
        var tmdOffset = Alignment + AlignUp(certSize) + AlignUp(crlSize) + AlignUp(ticketSize);
        var tmdEnd = tmdOffset + tmdSize;
        if (tmdEnd > stream.Length)
            return false;

        // The encrypted content must fit as well, so a small junk file cannot claim a huge payload.
        var dataOffset = AlignUp(tmdEnd);
        if (dataOffset + dataSize > stream.Length + Alignment)
            return false;

        layout = new WadLayout(tmdOffset, (int)tmdSize);
        return true;
    }

    private static WiiWadEvidence ReadTmdEvidence(ReadOnlySpan<byte> tmd)
    {
        if (tmd.Length < 4)
            return new WiiWadEvidence(null, null);

        var signatureBlockSize = BinaryPrimitives.ReadUInt32BigEndian(tmd[..4]) switch
        {
            SignatureRsa4096 => 0x240,
            SignatureRsa2048 => 0x140,
            SignatureEcc => 0x80,
            _ => -1,
        };
        if (signatureBlockSize < 0)
            return new WiiWadEvidence(null, null);

        var titleIdOffset = signatureBlockSize + TitleIdOffsetInTmdHeader;
        if (titleIdOffset + 8 > tmd.Length)
            return new WiiWadEvidence(null, null);

        var titleId = BinaryPrimitives.ReadUInt64BigEndian(tmd.Slice(titleIdOffset, 8));
        if (titleId == 0)
            return new WiiWadEvidence(null, null);

        return new WiiWadEvidence(
            ReadGameCode(tmd.Slice(titleIdOffset + 4, 4)),
            titleId.ToString("X16"));
    }

    // The low word of the title id is the four-character ASCII game code (for example "WB4E") of a
    // WiiWare, Virtual Console, or channel title. A title whose low word is not four printable
    // letters/digits (an IOS or a system title) has no game code; it is treated as absent, never
    // guessed.
    private static string? ReadGameCode(ReadOnlySpan<byte> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit((char)character))
                return null;
        }

        return Encoding.ASCII.GetString(value);
    }

    private static bool IsWadExtension(string path) =>
        Path.GetExtension(path).Equals(".wad", StringComparison.OrdinalIgnoreCase);

    private static long AlignUp(long value) => (value + (Alignment - 1)) & ~((long)Alignment - 1);

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 4096,
        FileOptions.None);

    private static bool TryReadExactlyAt(Stream stream, long offset, Span<byte> destination)
    {
        if (offset < 0 || offset > stream.Length - destination.Length)
            return false;

        stream.Position = offset;
        var read = 0;
        while (read < destination.Length)
        {
            var count = stream.Read(destination[read..]);
            if (count == 0)
                return false;
            read += count;
        }

        return true;
    }

    private readonly record struct WadLayout(long TmdOffset, int TmdSize);
}

/// <summary>
/// Title identity read from a Wii WAD's TMD. <see cref="GameCode"/> is the four-character GameTDB
/// cover key when the title carries one; <see cref="TitleId"/> is the full sixteen-hex title id.
/// Either may be <c>null</c> for a recognized WAD that carries no game code (a system title/IOS).
/// </summary>
public sealed record WiiWadEvidence(string? GameCode, string? TitleId);
