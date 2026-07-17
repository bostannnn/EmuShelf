using System.Buffers.Binary;
using System.Text;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Reads the disc serial from a PlayStation EBOOT (.pbp) by parsing the embedded
/// PARAM.SFO's <c>DISC_ID</c> key. This is a small, uncompressed, targeted read: the
/// PS1 disc image inside DATA.PSAR is never touched or decompressed.
/// </summary>
internal static class PbpSerialReader
{
    private const int PbpHeaderSize = 0x28;
    private const int SfoHeaderSize = 0x14;
    private const int SfoIndexEntrySize = 16;
    private const uint MaximumParamSfoBytes = 1 * 1024 * 1024;
    private const int MaximumSfoEntries = 4096;

    public static string? TryReadSerial(string path)
    {
        if (!Path.GetExtension(path).Equals(".pbp", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                4096,
                FileOptions.SequentialScan);

            Span<byte> header = stackalloc byte[PbpHeaderSize];
            if (!TryReadExactAt(stream, 0, header) || !header[..4].SequenceEqual("\0PBP"u8))
                return null;

            var paramOffset = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0x08, 4));
            var icon0Offset = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0x0C, 4));
            if (icon0Offset <= paramOffset || icon0Offset > stream.Length)
                return null;

            var sfoLength = icon0Offset - paramOffset;
            if (sfoLength is 0 or > MaximumParamSfoBytes)
                return null;

            var sfo = new byte[sfoLength];
            if (!TryReadExactAt(stream, paramOffset, sfo))
                return null;

            var discId = ReadSfoString(sfo, "DISC_ID");
            return string.IsNullOrEmpty(discId)
                ? null
                : PlayStationIdentifierExtractor.NormalizeProductCode(discId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadSfoString(ReadOnlySpan<byte> sfo, string key)
    {
        if (sfo.Length < SfoHeaderSize || !sfo[..4].SequenceEqual("\0PSF"u8))
            return null;

        var keyTableStart = BinaryPrimitives.ReadUInt32LittleEndian(sfo.Slice(0x08, 4));
        var dataTableStart = BinaryPrimitives.ReadUInt32LittleEndian(sfo.Slice(0x0C, 4));
        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(sfo.Slice(0x10, 4));
        if (keyTableStart > sfo.Length || dataTableStart > sfo.Length)
            return null;

        var entries = (int)Math.Min(entryCount, MaximumSfoEntries);
        for (var index = 0; index < entries; index++)
        {
            var entryOffset = SfoHeaderSize + index * SfoIndexEntrySize;
            if (entryOffset + SfoIndexEntrySize > sfo.Length)
                break;

            var entry = sfo.Slice(entryOffset, SfoIndexEntrySize);
            var keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(entry[..2]);
            var format = BinaryPrimitives.ReadUInt16LittleEndian(entry.Slice(2, 2));
            var dataLen = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4, 4));
            var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));

            var name = ReadCString(sfo, checked((long)keyTableStart + keyOffset));
            if (!string.Equals(name, key, StringComparison.Ordinal))
                continue;

            // 0x0204 = UTF-8 (null-terminated), 0x0004 = UTF-8 (special). A DISC_ID is
            // always textual; an unexpected integer format is not a usable serial.
            if (format is not (0x0204 or 0x0004))
                return null;

            var dataStart = (long)dataTableStart + dataOffset;
            if (dataStart < 0 || dataStart >= sfo.Length)
                return null;
            var available = (int)Math.Min(dataLen, sfo.Length - dataStart);
            if (available <= 0)
                return null;

            return Encoding.UTF8.GetString(sfo.Slice((int)dataStart, available))
                .TrimEnd('\0')
                .Trim();
        }
        return null;
    }

    private static string? ReadCString(ReadOnlySpan<byte> data, long start)
    {
        if (start < 0 || start >= data.Length)
            return null;
        var end = (int)start;
        while (end < data.Length && data[end] != 0)
            end++;
        return Encoding.ASCII.GetString(data.Slice((int)start, end - (int)start));
    }

    private static bool TryReadExactAt(FileStream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > stream.Length)
            return false;

        stream.Position = offset;
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
                return false;
            total += read;
        }
        return true;
    }
}
