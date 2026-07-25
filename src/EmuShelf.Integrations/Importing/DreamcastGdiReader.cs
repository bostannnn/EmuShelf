using System.Security.Cryptography;
using EmuShelf.Integrations.Achievements;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Parses the textual GDI descriptor used by Dreamcast disc sets. Only a descriptor with a
/// readable third data track and a valid IP.BIN marker is accepted; loose BIN files are never
/// treated as games. The reader is strictly read-only.
/// </summary>
public static class DreamcastGdiReader
{
    private const int IpBinBytes = 256;
    private static ReadOnlySpan<byte> IpMarker => "SEGA SEGAKATANA "u8;

    public static DreamcastGdiEvidence? TryRead(string path)
    {
        if (!Path.GetExtension(path).Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var descriptor = Parse(path);
            var dataTrack = descriptor.Tracks.Single(track => track.Number == 3);
            if (dataTrack.Type != 4 || dataTrack.SectorSize is not (2048 or 2352))
                return null;

            ValidateTrackFiles(descriptor.Tracks);
            using var stream = OpenRead(dataTrack.Path);
            if (!HasIpBinMarker(stream, dataTrack))
                return null;

            // GDI's offset field is required to be zero (and Parse enforces that), so this is
            // precisely the data-track payload recorded by the Redump catalogue.
            stream.Position = dataTrack.Offset;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);

            return new DreamcastGdiEvidence(
                dataTrack.Path,
                Convert.ToHexString(hash.GetHashAndReset()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or InvalidDataException or
                                   OverflowException)
        {
            return null;
        }
    }

    /// <summary>Checks descriptor and IP.BIN structure without reading the whole data track.</summary>
    public static bool TryRecognize(string path)
    {
        if (!Path.GetExtension(path).Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var descriptor = Parse(path);
            var dataTrack = descriptor.Tracks.Single(track => track.Number == 3);
            ValidateTrackFiles(descriptor.Tracks);
            using var stream = OpenRead(dataTrack.Path);
            return dataTrack.Type == 4 && dataTrack.SectorSize is 2048 or 2352 &&
                   HasIpBinMarker(stream, dataTrack);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or InvalidDataException or
                                   OverflowException)
        {
            return false;
        }
    }

    public static IReadOnlyList<string> GetReferencedFiles(string path)
    {
        try
        {
            return Parse(path).Tracks.Select(track => track.Path).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or InvalidDataException or
                                   OverflowException)
        {
            return [];
        }
    }

    internal static DreamcastGdiTrackReader OpenDataTrack(string path)
    {
        var descriptor = Parse(path);
        var dataTrack = descriptor.Tracks.Single(track => track.Number == 3);
        ValidateTrackFiles(descriptor.Tracks);
        try
        {
            if (dataTrack.Type != 4 || dataTrack.SectorSize is not (2048 or 2352))
            {
                throw new InvalidDataException("The GDI data track does not start with Dreamcast IP.BIN.");
            }

            return new DreamcastGdiTrackReader(descriptor.Tracks, dataTrack);
        }
        catch
        {
            throw;
        }
    }

    private static DreamcastGdiDescriptor Parse(string path)
    {
        var lines = File.ReadLines(path)
            .Select(line => line.Trim().TrimStart('\uFEFF'))
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length < 4 || !int.TryParse(lines[0], out var count) || count is < 3 or > 99 ||
            lines.Length != count + 1)
        {
            throw new InvalidDataException("The GDI descriptor has an invalid track count.");
        }

        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidDataException("The GDI descriptor has no parent directory.");
        var tracks = new List<DreamcastGdiTrack>(count);
        var priorLba = -1;
        for (var index = 0; index < count; index++)
        {
            var fields = Tokenize(lines[index + 1]);
            if (fields.Count != 6 || !int.TryParse(fields[0], out var number) ||
                !int.TryParse(fields[1], out var lba) || !int.TryParse(fields[2], out var type) ||
                !int.TryParse(fields[3], out var sectorSize) || !long.TryParse(fields[5], out var offset) ||
                number != index + 1 || lba <= priorLba || type is not (0 or 4) ||
                sectorSize is not (2048 or 2352) || offset != 0)
            {
                throw new InvalidDataException("The GDI descriptor has an invalid track entry.");
            }

            var reference = fields[4].Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(reference) || reference.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                throw new InvalidDataException("The GDI descriptor uses an unsafe track path.");

            var trackPath = Path.GetFullPath(Path.Combine(baseDirectory, reference));
            var relativeTrackPath = Path.GetRelativePath(baseDirectory, trackPath);
            if (Path.IsPathRooted(relativeTrackPath) ||
                relativeTrackPath.Equals("..", StringComparison.Ordinal) ||
                relativeTrackPath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The GDI descriptor references a track outside its folder.");
            }

            tracks.Add(new DreamcastGdiTrack(number, lba, type, sectorSize, trackPath, offset));
            priorLba = lba;
        }

        if (tracks.All(track => track.Number != 3))
            throw new InvalidDataException("The GDI descriptor has no track 03.");
        return new DreamcastGdiDescriptor(tracks);
    }

    private static void ValidateTrackFiles(IReadOnlyList<DreamcastGdiTrack> tracks)
    {
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var info = new FileInfo(track.Path);
            if (!info.Exists || info.Length < track.Offset + track.SectorSize)
                throw new InvalidDataException("The GDI descriptor references a missing or truncated track.");

            // High-density tracks are contiguous within the GDI's descriptor. Their next LBA
            // therefore gives a reliable minimum extent, unlike the separate low-density session
            // whose lead-in gap is intentionally not represented by the track file.
            if (track.Number >= 3 && index + 1 < tracks.Count)
            {
                var requiredLength = checked(track.Offset +
                    ((long)tracks[index + 1].Lba - track.Lba) * track.SectorSize);
                if (info.Length < requiredLength)
                    throw new InvalidDataException("The GDI descriptor references a missing or truncated track.");
            }
        }
    }

    private static bool HasIpBinMarker(FileStream stream, DreamcastGdiTrack track)
    {
        if (track.Offset > stream.Length || stream.Length - track.Offset < IpBinBytes)
            return false;

        var probe = new byte[Math.Min(track.SectorSize, 64)];
        stream.Position = track.Offset;
        stream.ReadExactly(probe);
        var userOffset = GetUserDataOffset(probe);
        return userOffset + IpMarker.Length <= probe.Length &&
               probe.AsSpan(userOffset, IpMarker.Length).SequenceEqual(IpMarker);
    }

    internal static int GetUserDataOffset(ReadOnlySpan<byte> sector)
    {
        if (sector.StartsWith(IpMarker))
            return 0;
        if (sector.Length >= 16 + IpMarker.Length && sector.Slice(16, IpMarker.Length).SequenceEqual(IpMarker))
            return 16;
        if (sector.Length >= 24 + IpMarker.Length && sector.Slice(24, IpMarker.Length).SequenceEqual(IpMarker))
            return 24;
        return -1;
    }

    private static List<string> Tokenize(string line)
    {
        var fields = new List<string>(6);
        for (var index = 0; index < line.Length;)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index]))
                index++;
            if (index == line.Length)
                break;

            if (line[index] == '"')
            {
                var end = line.IndexOf('"', index + 1);
                if (end <= index + 1)
                    throw new InvalidDataException("The GDI descriptor has an invalid quoted filename.");
                fields.Add(line[(index + 1)..end]);
                index = end + 1;
            }
            else
            {
                var start = index;
                while (index < line.Length && !char.IsWhiteSpace(line[index]))
                    index++;
                fields.Add(line[start..index]);
            }
        }
        return fields;
    }

    private static FileStream OpenRead(string path) => new(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 64 * 1024, FileOptions.SequentialScan);
}

public sealed record DreamcastGdiEvidence(string DataTrackPath, string DataTrackSha1);

internal sealed record DreamcastGdiDescriptor(IReadOnlyList<DreamcastGdiTrack> Tracks);
internal sealed record DreamcastGdiTrack(
    int Number,
    int Lba,
    int Type,
    int SectorSize,
    string Path,
    long Offset);
