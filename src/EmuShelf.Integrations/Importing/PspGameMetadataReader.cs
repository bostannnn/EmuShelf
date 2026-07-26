using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using EmuShelf.Integrations.Achievements;
using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Metadata.Chd;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Reads the small <c>PSP_GAME/PARAM.SFO</c> descriptor from a standalone PSP ISO, CSO, or CHD.
/// It deliberately supports only the formats accepted by the M14 importer and never writes to
/// the source image.
/// </summary>
public static partial class PspGameMetadataReader
{
    private const uint MaximumParamSfoBytes = 1 * 1024 * 1024;
    private const int SectorSize = 2048;
    private const int SfoHeaderSize = 0x14;
    private const int SfoIndexEntrySize = 16;
    private const int MaximumSfoEntries = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// A successfully parsed SFO proves that this is a PSP image. Individual evidence fields can
    /// be absent or untrustworthy; callers then retain the filename for presentation.
    /// </summary>
    public sealed record Evidence(string? DiscId, string? Title);

    public static Evidence? TryRead(string path)
    {
        var extension = Path.GetExtension(path);
        try
        {
            if (extension.Equals(".cso", StringComparison.OrdinalIgnoreCase))
            {
                using var compressed = CompressedIsoSectorSource.TryOpen(path);
                return compressed is null ? null : TryRead(compressed);
            }

            // A PSP CHD is DVD-geometry (2048-byte units), which ChdSectorSource already
            // addresses by logical sector, so the SFO read below is container-agnostic.
            if (extension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
            {
                using var chd = ChdSectorSource.TryOpen(path);
                return chd is null ? null : TryRead(chd);
            }

            if (!extension.Equals(".iso", StringComparison.OrdinalIgnoreCase))
                return null;

            using var disc = CdSectorReader.Open(path);
            return TryRead(disc);
        }
        catch (Exception ex) when (ex is InvalidDataException or UnsupportedDiscLayoutException or
                                   IOException or UnauthorizedAccessException or
                                   NotSupportedException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static Evidence? TryRead(ILogicalSectorReader disc)
    {
        var entry = Iso9660Directory.FindFile(disc, "PSP_GAME\\PARAM.SFO");
        if (entry is null || entry.Value.Size is 0 or > MaximumParamSfoBytes)
            return null;

        var sfo = new byte[entry.Value.Size];
        for (var offset = 0; offset < sfo.Length; offset += SectorSize)
        {
            var count = Math.Min(SectorSize, sfo.Length - offset);
            var sector = checked(entry.Value.Sector + (uint)(offset / SectorSize));
            if (disc.ReadSector(sector, sfo.AsSpan(offset, count)) != count)
                return null;
        }

        return TryParse(sfo, out var evidence) ? evidence : null;
    }

    private static bool TryParse(ReadOnlySpan<byte> sfo, out Evidence evidence)
    {
        evidence = new Evidence(null, null);
        if (sfo.Length < SfoHeaderSize || !sfo[..4].SequenceEqual("\0PSF"u8))
            return false;

        var keyTableStart = BinaryPrimitives.ReadUInt32LittleEndian(sfo.Slice(0x08, 4));
        var dataTableStart = BinaryPrimitives.ReadUInt32LittleEndian(sfo.Slice(0x0C, 4));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(sfo.Slice(0x10, 4));
        var indexTableEnd = SfoHeaderSize + ((long)entryCount * SfoIndexEntrySize);
        if (keyTableStart < SfoHeaderSize || keyTableStart > dataTableStart ||
            dataTableStart > sfo.Length || entryCount > MaximumSfoEntries ||
            indexTableEnd > keyTableStart)
        {
            return false;
        }

        string? discId = null;
        string? title = null;
        for (var index = 0; index < (int)entryCount; index++)
        {
            var entryOffset = SfoHeaderSize + index * SfoIndexEntrySize;
            var entry = sfo.Slice(entryOffset, SfoIndexEntrySize);
            var keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(entry[..2]);
            var format = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(2, 2));
            var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4, 4));
            var dataMaximum = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
            var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));
            var key = TryReadKey(sfo, keyTableStart, dataTableStart, keyOffset);
            if (key is null || !HasValidDataRange(
                    sfo, dataTableStart, dataOffset, dataLength, dataMaximum))
                return false;

            if (key is not ("DISC_ID" or "TITLE"))
                continue;

            var value = TryReadTextValue(sfo, dataTableStart, dataOffset, dataLength, format);
            if (value is null)
                continue;

            if (key == "DISC_ID" && IsDiscId(value))
                discId = value.ToUpperInvariant();
            else if (key == "TITLE" && IsTrustedTitle(value))
                title = value;
        }

        evidence = new Evidence(discId, title);
        return true;
    }

    private static bool HasValidDataRange(
        ReadOnlySpan<byte> sfo,
        uint dataTableStart,
        uint dataOffset,
        uint dataLength,
        uint dataMaximum)
    {
        if (dataLength > dataMaximum)
            return false;

        var start = (long)dataTableStart + dataOffset;
        var end = start + dataMaximum;
        return start >= dataTableStart && end >= start && end <= sfo.Length;
    }

    private static string? TryReadKey(
        ReadOnlySpan<byte> sfo,
        uint keyTableStart,
        uint dataTableStart,
        ushort keyOffset)
    {
        var start = (long)keyTableStart + keyOffset;
        if (start < keyTableStart || start >= dataTableStart)
            return null;

        var end = start;
        while (end < dataTableStart && sfo[(int)end] != 0)
            end++;
        return end == dataTableStart
            ? null
            : Encoding.ASCII.GetString(sfo.Slice((int)start, (int)(end - start)));
    }

    private static string? TryReadTextValue(
        ReadOnlySpan<byte> sfo,
        uint dataTableStart,
        uint dataOffset,
        uint dataLength,
        ushort format)
    {
        if (format is not (0x0204 or 0x0004) || dataLength == 0)
            return null;

        var start = (long)dataTableStart + dataOffset;
        var end = start + dataLength;
        if (start < dataTableStart || end > sfo.Length || end < start)
            return null;

        try
        {
            return StrictUtf8.GetString(sfo.Slice((int)start, (int)dataLength))
                .TrimEnd('\0')
                .Trim();
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static bool IsDiscId(string value) => DiscIdPattern().IsMatch(value);

    private static bool IsTrustedTitle(string value) =>
        value.Length is > 0 and <= 256 && value.All(character => !char.IsControl(character));

    [GeneratedRegex("^[A-Z]{4}[0-9]{5}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DiscIdPattern();
}
