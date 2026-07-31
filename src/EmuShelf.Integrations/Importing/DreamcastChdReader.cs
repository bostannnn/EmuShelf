using EmuShelf.Integrations.Achievements;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Recognizes a Dreamcast disc packaged as a single CHD. Unlike a GDI set there is no descriptor
/// to validate: the container's own track list is authoritative, and a disc is accepted only when
/// a declared data track really does start with an IP.BIN header. The reader is strictly
/// read-only and never decompresses more than the few hunks it inspects.
/// </summary>
public static class DreamcastChdReader
{
    public static bool TryRecognize(string path)
    {
        if (!IsChd(path))
            return false;

        using var disc = DreamcastChdTrackReader.TryOpen(path);
        return disc is not null;
    }

    /// <summary>
    /// The IP.BIN product number in the spellings Redump's catalogue uses, or an empty list when
    /// the disc is not a readable Dreamcast image.
    /// </summary>
    public static IReadOnlyList<string> ReadProductNumberAliases(string path)
    {
        if (!IsChd(path))
            return [];

        try
        {
            using var disc = DreamcastChdTrackReader.TryOpen(path);
            if (disc is null)
                return [];

            Span<byte> ipBin = stackalloc byte[DreamcastIpBin.HeaderBytes];
            return disc.ReadSector((uint)disc.FirstTrackSector, ipBin) == ipBin.Length
                ? DreamcastIpBin.ReadProductNumberAliases(ipBin)
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException or
                                   InvalidDataException or OverflowException)
        {
            return [];
        }
    }

    internal static DreamcastChdTrackReader OpenDataTrack(string path) =>
        DreamcastChdTrackReader.TryOpen(path)
        ?? throw new InvalidDataException(
            "This CHD does not contain a readable Dreamcast data track.");

    private static bool IsChd(string path) =>
        Path.GetExtension(path).Equals(
            DreamcastDisc.ChdExtension,
            StringComparison.OrdinalIgnoreCase);
}
