using System.Security.Cryptography;
using System.Text;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// rcheevos Dreamcast GDI hash: the complete 256-byte IP.BIN header followed by the bytes of
/// the boot executable named at IP.BIN offset 96. It intentionally never hashes a descriptor or
/// guessed track bytes.
/// </summary>
internal static class DreamcastGdiHasher
{
    public static string Hash(string path, CancellationToken cancellationToken)
    {
        using var disc = DreamcastGdiReader.OpenDataTrack(path);
        var ipBin = new byte[256];
        if (disc.ReadSector((uint)disc.FirstTrackSector, ipBin) != ipBin.Length ||
            !ipBin.AsSpan(0, 16).SequenceEqual("SEGA SEGAKATANA "u8))
            throw new InvalidDataException("The Dreamcast data track has no readable IP.BIN.");

        var end = 96;
        while (end < 112 && ipBin[end] is not 0 and not (byte)' ')
            end++;
        if (end == 96)
            throw new InvalidDataException("Dreamcast IP.BIN does not name a boot executable.");

        var bootFile = Encoding.ASCII.GetString(ipBin, 96, end - 96);
        var executable = Iso9660Directory.FindFile(disc, bootFile)
            ?? throw new InvalidDataException("The Dreamcast boot executable could not be located.");

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(ipBin);
        var buffer = new byte[2048];
        var remaining = executable.Size;
        var sector = executable.Sector;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min((uint)buffer.Length, remaining);
            if (disc.ReadSector(sector, buffer.AsSpan(0, count)) != count)
                throw new InvalidDataException("The Dreamcast boot executable ended unexpectedly.");
            md5.AppendData(buffer, 0, count);
            remaining -= (uint)count;
            sector++;
        }

        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }
}
