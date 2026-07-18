using System.Text;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Reads 2048-byte logical sectors from a disc image. Implemented by the raw
/// <see cref="CdSectorReader"/> and by container adapters (e.g. CSO/ZSO) so the ISO9660
/// walk and serial reader are container-agnostic.
/// </summary>
internal interface ILogicalSectorReader : IDisposable
{
    int FirstTrackSector { get; }

    int ReadSector(uint sector, Span<byte> destination);
}

/// <summary>
/// Minimal, read-only ISO9660 file lookup shared by disc consumers (RetroAchievements
/// hashing and metadata serial extraction). It resolves a backslash-separated path to
/// its directory record by walking the primary volume descriptor's root directory.
/// </summary>
internal readonly record struct Iso9660Entry(uint Sector, uint Size);

internal static class Iso9660Directory
{
    public static Iso9660Entry? FindFile(ILogicalSectorReader disc, string rawPath)
    {
        var path = rawPath.TrimStart('\\');
        var separator = path.LastIndexOf('\\');
        uint sector;
        uint sectorsToScan;
        string name;

        if (separator >= 0)
        {
            var directory = FindFile(disc, path[..separator]);
            if (directory is null)
                return null;
            sector = directory.Value.Sector;
            sectorsToScan = 1;
            name = path[(separator + 1)..];
        }
        else
        {
            Span<byte> descriptor = stackalloc byte[256];
            if (disc.ReadSector((uint)(disc.FirstTrackSector + 16), descriptor) < descriptor.Length)
                return null;

            sector = ReadUInt24LittleEndian(descriptor[158..161]);
            var logicalBlockSize = BitConverter.ToUInt16(descriptor[128..130]);
            if (logicalBlockSize == 0)
            {
                sectorsToScan = 1;
            }
            else
            {
                var rootSize = BitConverter.ToUInt32(descriptor[166..170]);
                sectorsToScan = Math.Max(1, rootSize / logicalBlockSize);
            }
            name = path;
        }

        var nameBytes = Encoding.ASCII.GetBytes(name);
        var directoryBuffer = new byte[2048];
        for (uint page = 0; page < sectorsToScan; page++)
        {
            if (disc.ReadSector(sector + page, directoryBuffer) < directoryBuffer.Length)
                return null;

            var offset = 0;
            while (offset < directoryBuffer.Length && directoryBuffer[offset] != 0)
            {
                var recordLength = directoryBuffer[offset];
                if (recordLength < 34 || offset + recordLength > directoryBuffer.Length)
                    break;

                var record = directoryBuffer.AsSpan(offset, recordLength);
                var matchesLength = record[32] == nameBytes.Length;
                var hasVersion = 33 + nameBytes.Length < record.Length &&
                                 record[33 + nameBytes.Length] == (byte)';';
                if (33 + nameBytes.Length <= record.Length &&
                    (matchesLength || hasVersion) &&
                    AsciiEqualsIgnoreCase(record.Slice(33, nameBytes.Length), nameBytes))
                {
                    return new Iso9660Entry(
                        ReadUInt24LittleEndian(record[2..5]),
                        BitConverter.ToUInt32(record[10..14]));
                }

                offset += recordLength;
            }
        }

        return null;
    }

    private static uint ReadUInt24LittleEndian(ReadOnlySpan<byte> value) =>
        (uint)(value[0] | (value[1] << 8) | (value[2] << 16));

    private static bool AsciiEqualsIgnoreCase(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right)
    {
        if (left.Length != right.Length)
            return false;
        for (var index = 0; index < left.Length; index++)
        {
            var a = left[index];
            var b = right[index];
            if (a is >= (byte)'a' and <= (byte)'z')
                a -= (byte)('a' - 'A');
            if (b is >= (byte)'a' and <= (byte)'z')
                b -= (byte)('a' - 'A');
            if (a != b)
                return false;
        }
        return true;
    }
}
