using System.Security.Cryptography;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Reproduces the rcheevos NES hash: the 16-byte iNES header is skipped and the PRG + CHR ROM the
/// header declares (<c>prg * 16 KiB + chr * 8 KiB</c>) is MD5-hashed, trimming any trailing over-dump
/// so a padded file matches the exact set. This is not the whole-file cartridge hash that stays the
/// No-Intro catalogue key, so NES has its own reader. A trainer, when present, is not skipped —
/// matching rcheevos, which hashes from byte 16. NES 2.0 exponent PRG/CHR sizing is not decoded; such
/// images fall back to hashing the whole post-header body and so may not match RetroAchievements.
/// </summary>
internal static class NesRomHasher
{
    private const int HeaderBytes = 16;
    private const int PrgBankCountOffset = 4;
    private const int ChrBankCountOffset = 5;
    private const int PrgBankBytes = 16 * 1024;
    private const int ChrBankBytes = 8 * 1024;

    public static string Hash(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        Span<byte> header = stackalloc byte[HeaderBytes];
        if (!TryReadExactly(stream, header))
            throw new InvalidDataException("The NES image is too small to contain an iNES header.");

        // Size the hash from the header's PRG/CHR bank counts, then clamp to what is actually present
        // so a trailing over-dump does not change the hash. A zero size (only seen with NES 2.0
        // exponent notation, which is not decoded here) hashes the remaining body instead.
        var romBytes = (long)header[PrgBankCountOffset] * PrgBankBytes +
                       (long)header[ChrBankCountOffset] * ChrBankBytes;
        var available = stream.Length - HeaderBytes;
        var toHash = romBytes > 0 && romBytes <= available ? romBytes : available;

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[64 * 1024];
        var remaining = toHash;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = stream.Read(buffer, 0, requested);
            if (read == 0)
                break;

            md5.AppendData(buffer, 0, read);
            remaining -= read;
        }

        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
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
