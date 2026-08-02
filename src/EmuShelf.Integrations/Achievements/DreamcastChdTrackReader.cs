using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Metadata.Chd;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Reads cooked ISO9660 sectors from a Dreamcast disc packaged as a single CHD. The container's
/// track list maps a disc address onto the frame that actually holds it: a GD-ROM's high-density
/// area starts at LBA 45000, and any earlier track whose length is not a multiple of four frames
/// shifts every later track's physical position by its alignment padding.
/// </summary>
internal sealed class DreamcastChdTrackReader : ILogicalSectorReader
{
    private const int SectorSize = 2048;

    // A GD-ROM always begins its high-density area — the one holding the game's ISO9660 volume
    // and boot executable — at this disc address.
    private const long HighDensityAreaLba = 45000;

    private readonly ChdSectorSource _source;
    private readonly IReadOnlyList<ChdTrack> _tracks;
    private readonly long _firstTrackLba;

    private DreamcastChdTrackReader(
        ChdSectorSource source,
        IReadOnlyList<ChdTrack> tracks,
        ChdTrack bootTrack)
    {
        _source = source;
        _tracks = tracks;
        _firstTrackLba = bootTrack.Lba;
    }

    // Disc addresses, as the GDI descriptor and the ISO9660 volume both use them.
    public int FirstTrackSector => (int)_firstTrackLba;

    /// <summary>
    /// Opens the disc when the container declares tracks and one of its data tracks starts with a
    /// Dreamcast IP.BIN header; returns null for every other CHD, including PlayStation CD images.
    /// </summary>
    public static DreamcastChdTrackReader? TryOpen(string path)
    {
        var source = ChdSectorSource.TryOpen(path);
        if (source is null)
            return null;

        try
        {
            // A Dreamcast image is always CD/GD geometry. Rejecting DVD-geometry containers here
            // keeps a PS2 or PSP CHD from being probed track by track.
            if (source.IsCd && FindBootTrack(source) is { } bootTrack)
            {
                // Later data tracks can hold the boot executable when audio splits the disc;
                // earlier tracks are the low-density area the game's volume never references.
                // A real disc writes every data track in the boot track's sector layout, so a
                // track declaring a different one is dropped rather than read at an offset only
                // this reader believes in: a missing executable fails visibly, wrong bytes do not.
                var tracks = source.Tracks
                    .Where(candidate =>
                        candidate.IsData &&
                        candidate.Type.Equals(bootTrack.Type, StringComparison.Ordinal) &&
                        candidate.Lba >= bootTrack.Lba)
                    .ToArray();
                return new DreamcastChdTrackReader(source, tracks, bootTrack);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or
                                   InvalidDataException or OverflowException)
        {
            source.Dispose();
            return null;
        }

        source.Dispose();
        return null;
    }

    public int ReadSector(uint sector, Span<byte> destination)
    {
        if (destination.Length > SectorSize)
            return 0;

        var resolved = FindTrack(sector);
        return resolved is null
            ? 0
            : _source.ReadSector(
                (uint)resolved.Value.Frame,
                destination,
                resolved.Value.Track.UserDataOffset);
    }

    public void Dispose() => _source.Dispose();

    private (ChdTrack Track, long Frame)? FindTrack(uint sector)
    {
        var absolute = (long)sector;
        var direct = Locate(absolute);
        if (direct is not null)
            return direct;

        // Some images build their ISO9660 volume with addresses relative to the boot track rather
        // than absolute disc addresses. Translate only those low numbers, exactly as the GDI
        // reader does, so a genuinely out-of-range sector still fails.
        return absolute < _firstTrackLba ? Locate(absolute + _firstTrackLba) : null;
    }

    private (ChdTrack Track, long Frame)? Locate(long lba)
    {
        foreach (var track in _tracks)
        {
            if (!track.Contains(lba))
                continue;

            // Frame numbers come from a metadata list this reader does not otherwise bound, so
            // check the range the container addresses them in rather than letting the cast wrap.
            var frame = track.PhysicalFrame + (lba - track.Lba);
            return frame > uint.MaxValue ? null : (track, frame);
        }

        return null;
    }

    private static ChdTrack? FindBootTrack(ChdSectorSource source)
    {
        ChdTrack? candidate = null;
        foreach (var track in source.Tracks)
        {
            // FirstTrackSector reports the boot address to the ISO9660 walk as an int, so a
            // declared layout that cannot be addressed that way has no usable boot track.
            if (!track.IsData || track.Lba > int.MaxValue || !HasIpBin(source, track))
                continue;

            candidate = track;
            // A GD-ROM's low-density area opens with its own IP.BIN copy and a small ISO9660
            // volume of its own, so the first header on the disc is not the game's. Keep looking
            // until the high-density area, exactly as the GDI reader takes track 03 rather than
            // track 01. A Dreamcast disc pressed as a plain CD has no high-density area, and its
            // single header is the one to use.
            if (track.Lba >= HighDensityAreaLba)
                break;
        }

        return candidate;
    }

    private static bool HasIpBin(ChdSectorSource source, ChdTrack track)
    {
        if (track.PhysicalFrame > uint.MaxValue || track.UserDataOffset is not { } userDataOffset)
            return false;

        Span<byte> marker = stackalloc byte[16];
        return source.ReadSector((uint)track.PhysicalFrame, marker, userDataOffset) == marker.Length &&
               DreamcastIpBin.HasMarker(marker);
    }
}
