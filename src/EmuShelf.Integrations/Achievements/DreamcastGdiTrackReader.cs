using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Reads cooked ISO9660 sectors across a GDI's data tracks. Track 03 carries the volume
/// descriptor; games with intervening audio tracks may put their boot executable in the final
/// data track, so a single-track reader is not sufficient.
/// </summary>
internal sealed class DreamcastGdiTrackReader : ILogicalSectorReader
{
    private static ReadOnlySpan<byte> RawCdSync =>
        [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    private readonly IReadOnlyList<DataTrack> _tracks;
    private readonly int _primaryTrackLba;

    public DreamcastGdiTrackReader(
        IReadOnlyList<DreamcastGdiTrack> tracks,
        DreamcastGdiTrack primaryTrack)
    {
        _primaryTrackLba = primaryTrack.Lba;
        var dataTracks = new List<DataTrack>();
        try
        {
            // Tracks 1/2 are the standard-density CD area. Dreamcast's IP.BIN and ISO9660
            // volume begin in high-density track 03; later high-density data tracks may hold
            // the executable after audio tracks.
            for (var index = 0; index < tracks.Count; index++)
            {
                var track = tracks[index];
                if (track.Type != 4 || track.Number < 3)
                    continue;

                var stream = new FileStream(
                    track.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024, FileOptions.RandomAccess);
                var userDataOffset = GetUserDataOffset(stream, track, track.Number == primaryTrack.Number);
                var nextTrackLba = index + 1 < tracks.Count ? tracks[index + 1].Lba : (int?)null;
                dataTracks.Add(new DataTrack(track, stream, userDataOffset, nextTrackLba, stream.Length));
            }
            _tracks = dataTracks.OrderBy(track => track.Definition.Lba).ToArray();
            if (_tracks.Count == 0 || !HasPrimaryIpBin(primaryTrack))
                throw new InvalidDataException("The Dreamcast data track has no readable IP.BIN.");
        }
        catch
        {
            foreach (var track in dataTracks)
                track.Stream.Dispose();
            throw;
        }
    }

    // GDI sector references are absolute disc LBAs. A few generated sets expose them relative to
    // track 03, so ReadSector also accepts that form when it unambiguously targets the primary
    // data area.
    public int FirstTrackSector => _primaryTrackLba;

    public int ReadSector(uint sector, Span<byte> destination)
    {
        if (destination.Length > 2048)
            return 0;

        var resolved = FindTrack(sector);
        if (resolved is null)
            return 0;
        var (track, trackSector) = resolved.Value;

        long offset;
        try
        {
            offset = checked(track.Definition.Offset +
                             (trackSector - track.Definition.Lba) * track.Definition.SectorSize +
                             track.UserDataOffset);
        }
        catch (OverflowException)
        {
            return 0;
        }

        if (offset < 0 || offset + destination.Length > track.Length)
            return 0;
        track.Stream.Position = offset;
        var read = 0;
        while (read < destination.Length)
        {
            var count = track.Stream.Read(destination[read..]);
            if (count == 0)
                break;
            read += count;
        }
        return read;
    }

    public void Dispose()
    {
        foreach (var track in _tracks)
            track.Stream.Dispose();
    }

    private (DataTrack Track, long Sector)? FindTrack(uint sector)
    {
        var absolute = (long)sector;
        var direct = _tracks.FirstOrDefault(track => Contains(track, absolute));
        if (direct is not null)
            return (direct, absolute);

        // The ISO9660 implementation sees sector numbers returned from the primary volume. If a
        // generated image uses offsets relative to track 03, translate only those low numbers.
        if (absolute < _primaryTrackLba)
        {
            var relative = absolute + _primaryTrackLba;
            var relativeTrack = _tracks.FirstOrDefault(track => Contains(track, relative));
            return relativeTrack is null ? null : (relativeTrack, relative);
        }
        return null;
    }

    private static bool Contains(DataTrack track, long sector)
    {
        var sectors = (track.Length - track.Definition.Offset) / track.Definition.SectorSize;
        var fileEnd = track.Definition.Lba + sectors;
        var endExclusive = track.NextTrackLba is { } nextTrackLba
            // The descriptor includes a 150-sector pregap before the following track, but a
            // track file does not. Treat padding in that gap as unreadable so it cannot affect
            // the canonical RetroAchievements hash.
            ? Math.Min(fileEnd, (long)nextTrackLba - DreamcastGdiReader.PregapSectors)
            : fileEnd;
        return sector >= track.Definition.Lba && sector < endExclusive;
    }

    private static int GetUserDataOffset(
        FileStream stream,
        DreamcastGdiTrack track,
        bool isPrimaryTrack)
    {
        if (track.SectorSize == 2048)
            return 0;

        Span<byte> header = stackalloc byte[64];
        stream.Position = track.Offset;
        stream.ReadExactly(header);
        var dreamcastOffset = DreamcastGdiReader.GetUserDataOffset(header);
        if (isPrimaryTrack && dreamcastOffset >= 0)
            return dreamcastOffset;
        if (!header[..12].SequenceEqual(RawCdSync))
            throw new InvalidDataException("A Dreamcast data track does not contain raw CD sectors.");
        return header[15] == 2 ? 24 : 16;
    }

    private bool HasPrimaryIpBin(DreamcastGdiTrack primaryTrack)
    {
        var primary = _tracks.Single(track => track.Definition.Number == primaryTrack.Number);
        Span<byte> ipBin = stackalloc byte[16];
        return ReadSector((uint)_primaryTrackLba, ipBin) == ipBin.Length &&
               ipBin.SequenceEqual("SEGA SEGAKATANA "u8);
    }

    // Length is captured once at open time: Contains runs for every track on every sector read,
    // and FileStream.Length queries the OS each time it is touched.
    private sealed record DataTrack(
        DreamcastGdiTrack Definition,
        FileStream Stream,
        int UserDataOffset,
        int? NextTrackLba,
        long Length);
}
