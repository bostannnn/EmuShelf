using System.Buffers.Binary;
using System.Text;

namespace EmuShelf.Infrastructure.Tests.Metadata;

/// <summary>
/// Builds a minimal PlayStation EBOOT (.pbp) whose embedded PARAM.SFO carries a single
/// <c>DISC_ID</c>, so tests can exercise the targeted PBP serial read end to end.
/// </summary>
internal static class PbpBuilder
{
    public static byte[] BuildWithDiscId(string discId)
    {
        var sfo = BuildParamSfo([("DISC_ID", discId)]);
        var pbp = new byte[0x28 + sfo.Length];
        "\0PBP"u8.CopyTo(pbp);
        BinaryPrimitives.WriteUInt32LittleEndian(pbp.AsSpan(0x04), 0x00010000);

        var sectionsEnd = (uint)pbp.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(pbp.AsSpan(0x08), 0x28); // PARAM.SFO
        for (var offset = 0x0C; offset <= 0x24; offset += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(pbp.AsSpan(offset), sectionsEnd);

        sfo.CopyTo(pbp.AsSpan(0x28));
        return pbp;
    }

    private static byte[] BuildParamSfo(IReadOnlyList<(string Key, string Value)> entries)
    {
        var keyBytes = new List<byte>();
        var keyOffsets = new ushort[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            keyOffsets[index] = (ushort)keyBytes.Count;
            keyBytes.AddRange(Encoding.ASCII.GetBytes(entries[index].Key));
            keyBytes.Add(0);
        }
        while (keyBytes.Count % 4 != 0)
            keyBytes.Add(0);

        var dataBytes = new List<byte>();
        var dataOffsets = new uint[entries.Count];
        var dataLens = new uint[entries.Count];
        var dataMaxes = new uint[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            dataOffsets[index] = (uint)dataBytes.Count;
            var value = Encoding.UTF8.GetBytes(entries[index].Value);
            dataBytes.AddRange(value);
            dataBytes.Add(0);
            dataLens[index] = (uint)(value.Length + 1);
            var max = (uint)(value.Length + 1);
            while (max % 4 != 0)
                max++;
            dataMaxes[index] = max;
            while (dataBytes.Count % 4 != 0)
                dataBytes.Add(0);
        }

        var keyTableStart = (uint)(0x14 + entries.Count * 16);
        var dataTableStart = keyTableStart + (uint)keyBytes.Count;
        var sfo = new byte[dataTableStart + dataBytes.Count];
        "\0PSF"u8.CopyTo(sfo);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x04), 0x00000101);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x08), keyTableStart);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x0C), dataTableStart);
        BinaryPrimitives.WriteUInt32LittleEndian(sfo.AsSpan(0x10), (uint)entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = sfo.AsSpan(0x14 + index * 16);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, keyOffsets[index]);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(2), 0x0204);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4), dataLens[index]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8), dataMaxes[index]);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12), dataOffsets[index]);
        }

        keyBytes.ToArray().CopyTo(sfo, (int)keyTableStart);
        dataBytes.ToArray().CopyTo(sfo, (int)dataTableStart);
        return sfo;
    }
}
