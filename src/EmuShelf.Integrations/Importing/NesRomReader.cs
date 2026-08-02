using System.Security.Cryptography;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Recognizes iNES-format Nintendo Entertainment System / Famicom cartridge images and produces the
/// SHA-1 of the whole file — the form the No-Intro NES set is keyed by, since those ROMs keep the
/// 16-byte iNES header. Recognition is by the fixed "NES\x1A" magic plus a length that is at least
/// large enough to hold the PRG and CHR banks the header declares, so a renamed non-NES file is not
/// accepted. The reader never writes to the supplied file. The header-stripped RetroAchievements hash
/// lives in <c>NesRomHasher</c>; the catalogue key deliberately keeps the header.
/// </summary>
public static class NesRomReader
{
    // iNES 1.0 tops out near 6 MiB (255 PRG + 255 CHR banks); NES 2.0 can declare more. Bounding at
    // 16 MiB keeps the whole-file hash bounded while still admitting large homebrew.
    public const long MaximumRomBytes = 16L * 1024 * 1024;

    private const int HeaderBytes = 16;
    private const int TrainerBytes = 512;
    private const int PrgBankBytes = 16 * 1024;
    private const int ChrBankBytes = 8 * 1024;
    private const int PrgBankCountOffset = 4;
    private const int ChrBankCountOffset = 5;
    private const int Flags6Offset = 6;
    private const int Flags7Offset = 7;

    // "NES" followed by the MS-DOS EOF byte, the iNES/NES 2.0 signature.
    private static readonly byte[] Magic = [0x4E, 0x45, 0x53, 0x1A];

    private static readonly HashSet<string> Extensions =
        new(StringComparer.OrdinalIgnoreCase) { ".nes" };

    /// <summary>Reads only the fixed header for discovery; full-file SHA-1 is deferred to <see cref="TryRead"/>.</summary>
    public static NesRomHeader? TryRecognize(string path)
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

    /// <summary>Returns local header evidence and the exact SHA-1 of the whole headered ROM.</summary>
    public static NesRomEvidence? TryRead(string path)
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
            return new NesRomEvidence(
                header.PrgBankCount,
                header.ChrBankCount,
                Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static NesRomHeader? TryReadHeader(Stream stream)
    {
        if (stream.Length < HeaderBytes || stream.Length > MaximumRomBytes)
            return null;

        Span<byte> header = stackalloc byte[HeaderBytes];
        stream.Position = 0;
        if (!TryReadExactly(stream, header))
            return null;

        if (!header[..4].SequenceEqual(Magic))
            return null;

        var isNes20 = (header[Flags7Offset] & 0x0C) == 0x08;
        var prgBanks = header[PrgBankCountOffset];
        var chrBanks = header[ChrBankCountOffset];

        // An iNES 1.0 image always declares at least one PRG bank, so a file that only happens to
        // carry the magic with an empty header is rejected. NES 2.0 exponent sizing may use 0 here.
        if (!isNes20 && prgBanks == 0)
            return null;

        var trainer = (header[Flags6Offset] & 0x04) != 0 ? TrainerBytes : 0;

        // Lower bound only: NES 2.0 can declare larger PRG/CHR via the header's high nibbles, so the
        // file may be bigger, but it can never be smaller than the low bytes already account for.
        var minimumBytes = (long)HeaderBytes + trainer +
                           (long)prgBanks * PrgBankBytes + (long)chrBanks * ChrBankBytes;
        if (stream.Length < minimumBytes)
            return null;

        return new NesRomHeader(prgBanks, chrBanks, trainer != 0);
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
                throw new EndOfStreamException("The NES ROM changed while it was being read.");

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

public sealed record NesRomHeader(int PrgBankCount, int ChrBankCount, bool HasTrainer);

public sealed record NesRomEvidence(int PrgBankCount, int ChrBankCount, string Sha1);
