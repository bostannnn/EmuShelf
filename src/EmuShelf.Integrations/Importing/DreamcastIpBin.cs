using System.Text;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// The 256-byte IP.BIN header that starts every Dreamcast data track. Shared by the descriptor
/// reader (GDI) and the container reader (CHD) so a disc is recognized, and its catalogue serial
/// derived, from exactly the same bytes however its tracks are packaged.
/// </summary>
internal static class DreamcastIpBin
{
    public const int HeaderBytes = 256;

    public static ReadOnlySpan<byte> Marker => "SEGA SEGAKATANA "u8;

    public static bool HasMarker(ReadOnlySpan<byte> header) =>
        header.Length >= Marker.Length && header[..Marker.Length].SequenceEqual(Marker);

    public static IReadOnlyList<string> ReadProductNumberAliases(ReadOnlySpan<byte> ipBin)
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
}
