using System.Text;
using EmuShelf.Integrations.Achievements;
using EmuShelf.Integrations.Metadata.Chd;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Reads a PlayStation boot serial from the disc's <c>SYSTEM.CNF</c> using the shared
/// ISO9660 reader. This touches only the volume descriptor, the root directory, and the
/// single <c>SYSTEM.CNF</c> sector — a few kilobytes — instead of scanning the disc.
/// Compressed containers are handled by later phases and return no serial here.
/// </summary>
internal static class PlayStationDiscSerialReader
{
    /// <summary>
    /// Returns the normalized serial for a single disc image, or <c>null</c> when the
    /// container is unsupported here, has no recognizable layout, or has no boot record.
    /// </summary>
    public static string? TryReadSerial(string discPath)
    {
        var extension = Path.GetExtension(discPath);

        // PBP is handled by PbpSerialReader.
        if (extension.Equals(".pbp", StringComparison.OrdinalIgnoreCase))
            return null;

        if (extension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
        {
            using var chd = ChdSectorSource.TryOpen(discPath);
            return chd is null ? null : FromBootName(ReadBootName(chd));
        }

        if (extension.Equals(".cso", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".zso", StringComparison.OrdinalIgnoreCase))
        {
            using var source = CompressedIsoSectorSource.TryOpen(discPath);
            return source is null ? null : FromBootName(ReadBootName(source));
        }

        try
        {
            using var disc = CdSectorReader.Open(discPath);
            return FromBootName(ReadBootName(disc));
        }
        catch (Exception ex) when (ex is InvalidDataException or UnsupportedDiscLayoutException or
                                   IOException or UnauthorizedAccessException or
                                   NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static string? FromBootName(string? bootName) =>
        bootName is null ? null : PlayStationIdentifierExtractor.NormalizeProductCode(bootName);

    private static string? ReadBootName(ILogicalSectorReader disc)
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
            // A PlayStation 2 disc keys the boot path under BOOT2/cdrom0:, a PlayStation
            // 1 disc under BOOT/cdrom:. Try both so one reader serves both systems.
            var name = TryParseBootLine(line, "BOOT2", "cdrom0:")
                ?? TryParseBootLine(line, "BOOT", "cdrom:");
            if (name is not null)
                return name;
        }
        return null;
    }

    private static string? TryParseBootLine(string line, string bootKey, string cdromPrefix)
    {
        if (!line.StartsWith(bootKey, StringComparison.Ordinal))
            return null;

        var index = bootKey.Length;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;
        if (index >= line.Length || line[index] != '=')
            return null;

        index++;
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;
        if (line.AsSpan(index).StartsWith(cdromPrefix, StringComparison.Ordinal))
            index += cdromPrefix.Length;
        while (index < line.Length && line[index] == '\\')
            index++;

        var start = index;
        while (index < line.Length &&
               !char.IsWhiteSpace(line[index]) &&
               line[index] is not (';' or '\r'))
        {
            index++;
        }
        return index > start ? line[start..index] : null;
    }
}
