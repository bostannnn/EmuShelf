using System.Security.Cryptography;
using System.Text;

namespace EmuShelf.Integrations.Achievements;

internal static class PlayStationDiscHasher
{
    private const uint MaxHashedFileSize = 64 * 1024 * 1024;

    public static string Hash(
        string path,
        bool isPlayStation2,
        CancellationToken cancellationToken)
    {
        using var disc = CdSectorReader.Open(path);
        if (isPlayStation2)
        {
            var ps2 = TryHash(disc, "BOOT2", "cdrom0:", isPlayStation: false, cancellationToken);
            if (ps2 is not null)
                return ps2;

            // rcheevos also tries the PS1 disc algorithm for a PlayStation image
            // presented under the PS2 console id.
            return TryHash(disc, "BOOT", "cdrom:", isPlayStation: true, cancellationToken)
                ?? throw new InvalidDataException("The primary PlayStation executable was not found.");
        }

        return TryHash(disc, "BOOT", "cdrom:", isPlayStation: true, cancellationToken)
            ?? throw new InvalidDataException("The primary PlayStation executable was not found.");
    }

    private static string? TryHash(
        CdSectorReader disc,
        string bootKey,
        string cdromPrefix,
        bool isPlayStation,
        CancellationToken cancellationToken)
    {
        var executable = FindExecutable(disc, bootKey, cdromPrefix);
        if (executable is null && isPlayStation)
        {
            var fallback = Iso9660Directory.FindFile(disc, "PSX.EXE");
            if (fallback is not null)
                executable = new DiscFile("PSX.EXE", fallback.Value.Sector, fallback.Value.Size);
        }
        if (executable is null)
            return null;

        Span<byte> header = stackalloc byte[32];
        if (disc.ReadSector(executable.Sector, header) < header.Length)
            throw new InvalidDataException("The primary executable could not be read.");

        var size = executable.Size;
        if (isPlayStation && header[..7].SequenceEqual("PS-X EX"u8))
            size = checked(BitConverter.ToUInt32(header[28..32]) + 2048U);

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(Encoding.ASCII.GetBytes(executable.Name));
        AppendFile(md5, disc, executable.Sector, size, cancellationToken);
        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }

    private static DiscFile? FindExecutable(
        CdSectorReader disc,
        string bootKey,
        string cdromPrefix)
    {
        var system = Iso9660Directory.FindFile(disc, "SYSTEM.CNF");
        if (system is null)
            return null;

        var buffer = new byte[2048];
        var read = disc.ReadSector(system.Value.Sector, buffer.AsSpan(0, 2047));
        if (read <= 0)
            return null;

        var contents = Encoding.ASCII.GetString(buffer, 0, read);
        foreach (var line in contents.Split('\n'))
        {
            if (!line.StartsWith(bootKey, StringComparison.Ordinal))
                continue;

            var index = bootKey.Length;
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
            if (index >= line.Length || line[index] != '=')
                continue;

            index++;
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
            if (line.AsSpan(index).StartsWith(cdromPrefix, StringComparison.Ordinal))
                index += cdromPrefix.Length;
            while (index < line.Length && line[index] == '\\')
                index++;

            var start = index;
            while (index < line.Length && !char.IsWhiteSpace(line[index]) && line[index] != ';')
                index++;
            if (index == start)
                continue;

            var name = line[start..index];
            if (name.Length > 63)
                name = name[..63];
            var file = Iso9660Directory.FindFile(disc, name);
            if (file is not null)
                return new DiscFile(name, file.Value.Sector, file.Value.Size);
        }

        return null;
    }

    private static void AppendFile(
        IncrementalHash md5,
        CdSectorReader disc,
        uint sector,
        uint requestedSize,
        CancellationToken cancellationToken)
    {
        var remaining = Math.Min(requestedSize, MaxHashedFileSize);
        var buffer = new byte[2048];
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min((uint)buffer.Length, remaining);
            if (disc.ReadSector(sector, buffer.AsSpan(0, count)) < count)
                throw new InvalidDataException("The primary executable ended unexpectedly.");
            md5.AppendData(buffer, 0, count);
            remaining -= (uint)count;
            sector++;
        }
    }

    private sealed record DiscFile(string Name, uint Sector, uint Size);
}
