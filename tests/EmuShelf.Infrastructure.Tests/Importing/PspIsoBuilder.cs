using System.Buffers.Binary;
using System.Text;

namespace EmuShelf.Infrastructure.Tests.Importing;

/// <summary>Builds a tiny ISO9660 PSP image with a real PSP_GAME/PARAM.SFO descriptor.</summary>
internal static class PspIsoBuilder
{
    private const int SectorSize = 2048;

    public static byte[] Build(string? discId = "ULUS10041", string? title = "Lumines")
    {
        var entries = new List<(string Key, string Value)>();
        if (discId is not null)
            entries.Add(("DISC_ID", discId));
        if (title is not null)
            entries.Add(("TITLE", title));
        var sfo = BuildParamSfo(entries);
        var image = new byte[24 * SectorSize];

        var descriptor = image.AsSpan(16 * SectorSize, SectorSize);
        descriptor[0] = 1;
        "CD001"u8.CopyTo(descriptor[1..]);
        descriptor[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(descriptor[128..130], SectorSize);
        WriteUInt24LittleEndian(descriptor[158..161], 20); // root directory extent
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[166..170], SectorSize);

        WriteDirectoryRecord(image.AsSpan(20 * SectorSize), "PSP_GAME", 21, SectorSize, isDirectory: true);
        WriteDirectoryRecord(image.AsSpan(21 * SectorSize), "PARAM.SFO", 22, (uint)sfo.Length, isDirectory: false);
        sfo.CopyTo(image.AsSpan(22 * SectorSize));
        return image;
    }

    private static void WriteDirectoryRecord(
        Span<byte> target,
        string name,
        uint sector,
        uint length,
        bool isDirectory)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var recordLength = 33 + nameBytes.Length + ((nameBytes.Length & 1) == 0 ? 1 : 0);
        target[0] = (byte)recordLength;
        WriteUInt24LittleEndian(target[2..5], sector);
        BinaryPrimitives.WriteUInt32LittleEndian(target[10..14], length);
        target[25] = isDirectory ? (byte)2 : (byte)0;
        target[32] = (byte)nameBytes.Length;
        nameBytes.CopyTo(target[33..]);
    }

    private static byte[] BuildParamSfo(IReadOnlyList<(string Key, string Value)> entries)
    {
        var keys = new List<byte>();
        var keyOffsets = new ushort[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            keyOffsets[index] = (ushort)keys.Count;
            keys.AddRange(Encoding.ASCII.GetBytes(entries[index].Key));
            keys.Add(0);
        }
        while (keys.Count % 4 != 0)
            keys.Add(0);

        var data = new List<byte>();
        var dataOffsets = new uint[entries.Count];
        var dataLengths = new uint[entries.Count];
        var dataMaximums = new uint[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            dataOffsets[index] = (uint)data.Count;
            var value = Encoding.UTF8.GetBytes(entries[index].Value);
            data.AddRange(value);
            data.Add(0);
            dataLengths[index] = (uint)(value.Length + 1);
            var maximum = value.Length + 1;
            while (maximum % 4 != 0)
                maximum++;
            dataMaximums[index] = (uint)maximum;
            while (data.Count % 4 != 0)
                data.Add(0);
        }

        var keyTableStart = (uint)(0x14 + entries.Count * 16);
        var dataTableStart = keyTableStart + (uint)keys.Count;
        var sfo = new byte[dataTableStart + data.Count];
        "\0PSF"u8.CopyTo(sfo);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x04), 0x00000101);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x08), keyTableStart);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x0C), dataTableStart);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x10), (uint)entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = sfo.AsSpan(0x14 + index * 16);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, keyOffsets[index]);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 0x0204);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], dataLengths[index]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], dataMaximums[index]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[12..], dataOffsets[index]);
        }
        keys.CopyTo(sfo, (int)keyTableStart);
        data.CopyTo(sfo, (int)dataTableStart);
        return sfo;
    }

    private static void WriteUInt24LittleEndian(Span<byte> target, uint value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
