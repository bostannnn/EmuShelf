using System.Buffers.Binary;
using System.Text;

namespace EmuShelf.Integrations.Metadata.Chd;

/// <summary>
/// One track of a CD or GD-ROM CHD, as declared by the container's metadata list.
/// <paramref name="Lba"/> is the track's start in disc addresses (what an ISO9660 volume and a
/// GDI descriptor use), while <paramref name="PhysicalFrame"/> is where its frames actually begin
/// inside the CHD's frame stream. The two differ once any earlier track needed alignment padding.
/// </summary>
internal sealed record ChdTrack(
    int Number,
    string Type,
    long Lba,
    long DataFrames,
    long PhysicalFrame)
{
    public bool IsData => !Type.Equals("AUDIO", StringComparison.Ordinal);

    public bool Contains(long lba) => lba >= Lba && lba < Lba + DataFrames;

    /// <summary>
    /// Where this track's 2048-byte user data starts inside a stored frame, or null for a track
    /// that has no such area. A raw track keeps the sector header the cooked types drop, and
    /// chdman records which one it wrote, so no frame has to be recognized by its content.
    /// </summary>
    public int? UserDataOffset => Type switch
    {
        "MODE1_RAW" => 16,
        "MODE2_RAW" => 24,
        "MODE1" or "MODE2_FORM1" => 0,
        "MODE2" or "MODE2_FORM_MIX" => 8,
        _ => null,
    };
}

/// <summary>
/// Reads the track layout from a CHD v5 metadata list. chdman records one text entry per track:
/// <c>CHTR</c>/<c>CHT2</c> for a CD and <c>CHGD</c>/<c>CHGT</c> for a GD-ROM. Verified against
/// chdman 0.249 output by round-tripping real Dreamcast images: the declared FRAMES of each track
/// accumulate into the next track's LBA (which lands high-density track 03 on 45000 exactly), and
/// FRAMES minus PAD is the frame count chdman writes back out for that track.
/// </summary>
internal static class ChdTrackTable
{
    private const int EntryHeaderBytes = 16;
    private const int MaxEntries = 512;
    private const int MaxEntryBytes = 1024;
    private const int MaxFrames = 100_000_000;

    // MAME stores every track on a four-frame boundary, so a track whose frame count is not a
    // multiple of four is followed by unaddressable alignment frames that shift the next track's
    // physical position without moving its disc address.
    private const long TrackAlignmentFrames = 4;

    public static IReadOnlyList<ChdTrack> Read(Stream stream, long metadataOffset)
    {
        var tracks = new List<ChdTrack>();
        var header = new byte[EntryHeaderBytes];
        var offset = metadataOffset;
        long lba = 0;
        long physicalFrame = 0;

        for (var entry = 0; entry < MaxEntries; entry++)
        {
            if (offset <= 0 || offset + EntryHeaderBytes > stream.Length)
                break;

            stream.Position = offset;
            stream.ReadExactly(header);
            var tag = Encoding.ASCII.GetString(header, 0, 4);
            var length = (header[5] << 16) | (header[6] << 8) | header[7];
            var next = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8));

            if (IsTrackTag(tag))
            {
                if (length is <= 0 or > MaxEntryBytes ||
                    offset + EntryHeaderBytes + length > stream.Length)
                {
                    return [];
                }

                var data = new byte[length];
                stream.ReadExactly(data);
                var track = TryParse(
                    Encoding.ASCII.GetString(data).TrimEnd('\0'),
                    tracks.Count + 1,
                    lba,
                    physicalFrame);
                if (track is null)
                    return [];

                tracks.Add(track.Value.Track);
                lba = track.Value.NextLba;
                physicalFrame = track.Value.NextPhysicalFrame;
            }

            if (next == offset)
                break;
            offset = next;
        }

        return tracks;
    }

    private static bool IsTrackTag(string tag) =>
        tag is "CHTR" or "CHT2" or "CHGD" or "CHGT";

    private static (ChdTrack Track, long NextLba, long NextPhysicalFrame)? TryParse(
        string text,
        int expectedNumber,
        long lba,
        long physicalFrame)
    {
        if (ReadInt32(text, "TRACK") != expectedNumber ||
            ReadString(text, "TYPE") is not { Length: > 0 } type ||
            ReadInt32(text, "FRAMES") is not { } frames ||
            frames is <= 0 or > MaxFrames)
        {
            return null;
        }

        // PAD is a GD-ROM-only field. It counts the frames at the end of the declared extent that
        // the dump never stored — chdman zero-fills them — so only FRAMES minus PAD is readable.
        var pad = ReadInt32(text, "PAD") ?? 0;
        if (pad < 0 || pad >= frames)
            return null;

        var track = new ChdTrack(expectedNumber, type, lba, frames - pad, physicalFrame);
        return (
            track,
            lba + frames,
            physicalFrame + AlignFrames(frames));
    }

    private static long AlignFrames(long frames) =>
        (frames + TrackAlignmentFrames - 1) / TrackAlignmentFrames * TrackAlignmentFrames;

    private static int? ReadInt32(string text, string key) =>
        int.TryParse(ReadString(text, key), out var value) ? value : null;

    private static string? ReadString(string text, string key)
    {
        foreach (var field in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = field.IndexOf(':');
            if (separator > 0 && field.AsSpan(0, separator).SequenceEqual(key))
                return field[(separator + 1)..].Trim();
        }

        return null;
    }
}
