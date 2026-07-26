using System.Security.Cryptography;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Metadata.Chd;

namespace EmuShelf.Integrations.Achievements;

/// <summary>Hashes PSP PARAM.SFO followed by EBOOT.BIN using the rcheevos logical-disc algorithm.</summary>
internal static class PspDiscHasher
{
    private const uint MaximumHashedFileBytes = 64 * 1024 * 1024;

    public static string Hash(string path, CancellationToken cancellationToken)
    {
        if (PspGameMetadataReader.TryRead(path)?.DiscId is null)
        {
            throw new UnsupportedDiscLayoutException(
                "This PSP image does not have a trusted retail product serial.");
        }

        using var disc = OpenDisc(path);
        var paramSfo = Iso9660Directory.FindFile(disc, "PSP_GAME\\PARAM.SFO")
            ?? throw new InvalidDataException("The PSP image does not contain PSP_GAME\\PARAM.SFO.");
        var eboot = Iso9660Directory.FindFile(disc, "PSP_GAME\\SYSDIR\\EBOOT.BIN")
            ?? throw new InvalidDataException("The PSP image does not contain PSP_GAME\\SYSDIR\\EBOOT.BIN.");

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        AppendFile(md5, disc, paramSfo, cancellationToken);
        AppendFile(md5, disc, eboot, cancellationToken);
        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    // The hash is PARAM.SFO plus EBOOT.BIN read by logical sector, so the container only decides
    // which reader opens the image; a CHD and the ISO it was made from hash identically.
    private static ILogicalSectorReader OpenDisc(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".cso", StringComparison.OrdinalIgnoreCase))
        {
            return CompressedIsoSectorSource.TryOpen(path)
                ?? throw new UnsupportedDiscLayoutException(
                    "This compressed PSP ISO could not be opened by the local reader.");
        }

        if (extension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
        {
            return ChdSectorSource.TryOpen(path)
                ?? throw new UnsupportedDiscLayoutException(
                    "This PSP CHD could not be opened by the local reader.");
        }

        return CdSectorReader.Open(path);
    }

    private static void AppendFile(
        IncrementalHash md5,
        ILogicalSectorReader disc,
        Iso9660Entry entry,
        CancellationToken cancellationToken)
    {
        var remaining = Math.Min(entry.Size, MaximumHashedFileBytes);
        var sector = entry.Sector;
        var buffer = new byte[2048];

        // rcheevos proves that the first logical sector is complete before it clips the
        // appended bytes to the ISO9660 record's declared file size. Without this check, a
        // truncated final sector can look like a valid short PARAM.SFO or EBOOT.BIN.
        if (disc.ReadSector(sector, buffer) != buffer.Length)
            throw new InvalidDataException("A PSP hash input's first sector is truncated.");

        var firstCount = (int)Math.Min((uint)buffer.Length, remaining);
        md5.AppendData(buffer, 0, firstCount);
        remaining -= (uint)firstCount;
        sector++;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min((uint)buffer.Length, remaining);
            if (disc.ReadSector(sector, buffer.AsSpan(0, count)) != count)
                throw new InvalidDataException("A PSP hash input ended unexpectedly.");
            md5.AppendData(buffer, 0, count);
            remaining -= (uint)count;
            sector++;
        }
    }
}
