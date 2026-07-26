using System.Security.Cryptography;
using System.Text;
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

    // Every track change carries the standard 150-sector (two-second) pregap. GDI descriptor
    // LBAs include it, but no track file stores it, so a track's own extent is the distance to
    // the next LBA minus this gap. Real dumps are short by exactly this much: 102 Dalmatians
    // declares track 03 at 45000 and track 04 at 266949 while track03.bin holds 221799 sectors.
    internal const int PregapSectors = 150;

    private static ReadOnlySpan<byte> IpMarker => "SEGA SEGAKATANA "u8;

    public static DreamcastGdiEvidence? TryRead(string path)
    {
        if (!Path.GetExtension(path).Equals(".gdi", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var descriptor = Parse(path);
            var primaryTrack = PrimaryDataTrack(descriptor.Tracks);
            if (!IsSupportedDataTrack(primaryTrack))
                return null;

            ValidateTrackFiles(descriptor.Tracks);
            using var probe = OpenRead(primaryTrack.Path);
            var ipBin = TryReadIpBin(probe, primaryTrack);
            if (ipBin is null)
                return null;

            // Redump records one SHA-1 per track file, and libretro's condensed catalogue keeps a
            // single entry per game: the largest data track. That is track 03 on a single-data-track
            // disc, but a later high-density track whenever audio splits the data (Sega Rally 2 is
            // keyed on track 21, Tony Hawk's Pro Skater on track 05). Hash every data track and
            // report them largest first so the lookup matches either layout.
            var hashes = new List<DreamcastDataTrackHash>();
            foreach (var track in descriptor.Tracks)
            {
                if (track.Type != 4)
                    continue;

                using var stream = OpenRead(track.Path);
                var length = stream.Length;
                // GDI's offset field is required to be zero (and Parse enforces that), so this is
                // precisely the track payload recorded by the Redump catalogue.
                hashes.Add(new DreamcastDataTrackHash(
                    track.Number,
                    track.Path,
                    HashToEnd(stream, track.Offset),
                    length));
            }

            if (hashes.Count == 0)
                return null;

            return new DreamcastGdiEvidence(
                hashes
                    .OrderByDescending(track => track.Length)
                    .ThenBy(track => track.TrackNumber)
                    .ToArray(),
                ReadProductNumberAliases(ipBin));
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
            var primaryTrack = PrimaryDataTrack(descriptor.Tracks);
            ValidateTrackFiles(descriptor.Tracks);
            using var stream = OpenRead(primaryTrack.Path);
            return IsSupportedDataTrack(primaryTrack) && TryReadIpBin(stream, primaryTrack) is not null;
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
        var primaryTrack = PrimaryDataTrack(descriptor.Tracks);
        ValidateTrackFiles(descriptor.Tracks);
        if (!IsSupportedDataTrack(primaryTrack))
            throw new InvalidDataException(
                "The GDI primary track is not a supported Dreamcast data track.");

        return new DreamcastGdiTrackReader(descriptor.Tracks, primaryTrack);
    }

    // Parse guarantees sequential numbering from 1 and at least three tracks, so track 03 is
    // always present. The lookup still fails as invalid data rather than throwing an unhandled
    // InvalidOperationException if that validation is ever loosened.
    private static DreamcastGdiTrack PrimaryDataTrack(IReadOnlyList<DreamcastGdiTrack> tracks) =>
        tracks.FirstOrDefault(track => track.Number == 3)
        ?? throw new InvalidDataException("The GDI descriptor has no track 03.");

    private static bool IsSupportedDataTrack(DreamcastGdiTrack track) =>
        track.Type == 4 && track.SectorSize is 2048 or 2352;

    private static string HashToEnd(FileStream stream, long offset)
    {
        stream.Position = offset;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            hash.AppendData(buffer, 0, read);
        return Convert.ToHexString(hash.GetHashAndReset());
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

            // High-density tracks are contiguous within the GDI's descriptor apart from the pregap
            // preceding each following track, so the next LBA less the gap is a reliable minimum
            // extent. The separate low-density session is existence-checked only, because its
            // lead-in is not represented by the track files at all.
            if (track.Number >= 3 && index + 1 < tracks.Count)
            {
                var extentSectors = (long)tracks[index + 1].Lba - track.Lba - PregapSectors;
                if (extentSectors <= 0)
                {
                    // Audio tracks abut each other, so one shorter than a pregap legitimately
                    // lands here and the existence check above already covers it. A data track
                    // with no payload cannot hold IP.BIN or a filesystem, so that stays invalid.
                    if (track.Type == 4)
                    {
                        throw new InvalidDataException(
                            "The GDI descriptor has an invalid high-density track extent.");
                    }
                    continue;
                }

                var requiredLength = checked(track.Offset + extentSectors * track.SectorSize);
                if (info.Length < requiredLength)
                    throw new InvalidDataException("The GDI descriptor references a missing or truncated track.");
            }
        }
    }

    private static byte[]? TryReadIpBin(FileStream stream, DreamcastGdiTrack track)
    {
        if (track.Offset > stream.Length || stream.Length - track.Offset < IpBinBytes)
            return null;

        var probe = new byte[Math.Min(track.SectorSize, 64)];
        stream.Position = track.Offset;
        stream.ReadExactly(probe);
        var userOffset = GetUserDataOffset(probe);
        if (userOffset < 0 || userOffset + IpBinBytes > track.SectorSize ||
            track.Offset + userOffset + IpBinBytes > stream.Length)
        {
            return null;
        }

        var ipBin = new byte[IpBinBytes];
        stream.Position = track.Offset + userOffset;
        stream.ReadExactly(ipBin);
        return ipBin.AsSpan(0, IpMarker.Length).SequenceEqual(IpMarker) ? ipBin : null;
    }

    private static IReadOnlyList<string> ReadProductNumberAliases(ReadOnlySpan<byte> ipBin)
    {
        // IP.BIN stores the product number in its fixed 10-byte field. Redump's Dreamcast DAT is
        // inconsistent about Sega's MK- prefix that the disc header always keeps: US entries drop
        // it (51019 for a disc reading MK-51019) while PAL entries such as MK-51053 retain it.
        // Offer both spellings, the disc's own first so a PAL disc prefers its exact PAL entry and
        // only then falls back to the same game's prefix-less US entry.
        var product = Encoding.ASCII.GetString(ipBin.Slice(64, 10)).Trim('\0', ' ');
        var compact = string.Concat(product.Where(char.IsLetterOrDigit)).ToUpperInvariant();
        if (compact.Length == 0 || !compact.Any(char.IsDigit))
            return [];

        return compact.StartsWith("MK", StringComparison.Ordinal) && compact.Length > 2
            ? [compact, compact[2..]]
            : [compact];
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

/// <summary>
/// The SHA-1 of every data track named by a GDI descriptor, ordered largest first so the
/// catalogue key Redump records for the disc is tried before the smaller tracks.
/// </summary>
public sealed record DreamcastGdiEvidence(
    IReadOnlyList<DreamcastDataTrackHash> DataTracks,
    IReadOnlyList<string> ProductNumberAliases);

public sealed record DreamcastDataTrackHash(int TrackNumber, string Path, string Sha1, long Length);

internal sealed record DreamcastGdiDescriptor(IReadOnlyList<DreamcastGdiTrack> Tracks);
internal sealed record DreamcastGdiTrack(
    int Number,
    int Lba,
    int Type,
    int SectorSize,
    string Path,
    long Offset);
