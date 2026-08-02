using EmuShelf.Integrations.Achievements;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Routes a Dreamcast image to the reader for its packaging: a GDI descriptor naming loose track
/// files, or a self-contained CHD. Both are validated by the same IP.BIN evidence, so a caller
/// never has to know which one it is holding.
/// </summary>
public static class DreamcastDisc
{
    public const string GdiExtension = ".gdi";
    public const string ChdExtension = ".chd";

    public static bool IsSupportedExtension(string extension) =>
        extension.Equals(GdiExtension, StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(ChdExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Validates structure without reading a whole data track.</summary>
    public static bool TryRecognize(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            GdiExtension => DreamcastGdiReader.TryRecognize(path),
            ChdExtension => DreamcastChdReader.TryRecognize(path),
            _ => false,
        };

    /// <summary>
    /// The other files the image needs to be readable. A CHD is self-contained, so only a GDI
    /// descriptor reports anything.
    /// </summary>
    public static IReadOnlyList<string> GetReferencedFiles(string path) =>
        Path.GetExtension(path).Equals(GdiExtension, StringComparison.OrdinalIgnoreCase)
            ? DreamcastGdiReader.GetReferencedFiles(path)
            : [];

    internal static ILogicalSectorReader OpenDataTrack(string path) =>
        Path.GetExtension(path).Equals(ChdExtension, StringComparison.OrdinalIgnoreCase)
            ? DreamcastChdReader.OpenDataTrack(path)
            : DreamcastGdiReader.OpenDataTrack(path);
}
