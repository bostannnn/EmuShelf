using System.Text;

namespace EmuShelf.Infrastructure.Tests.Metadata;

/// <summary>
/// Builds a minimal, valid 2048-byte-sector ISO9660 image carrying a single
/// <c>SYSTEM.CNF</c> so tests can exercise the targeted serial read end to end.
/// </summary>
internal static class PlayStationIsoBuilder
{
    public static byte[] BuildPlayStation2Iso(string bootName, string? decoySerial = null)
    {
        var image = CreateIso9660(sectors: 24, label: "TEST");
        var systemCnf = $"BOOT2 = cdrom0:\\{bootName};1\nVER = 1.0\nVMODE = NTSC\n";
        AddIsoFile(image, "SYSTEM.CNF", Encoding.ASCII.GetBytes(systemCnf));

        // A decoy in the reserved system area (before the volume descriptor) would be the
        // first product code a linear scan encounters; the targeted read must ignore it.
        if (decoySerial is not null)
            Encoding.ASCII.GetBytes(decoySerial).CopyTo(image.AsSpan(2 * 2048));

        return image;
    }

    private static byte[] CreateIso9660(int sectors, string label)
    {
        var image = new byte[sectors * 2048];
        var descriptor = image.AsSpan(16 * 2048, 2048);
        new byte[] { 0x01, (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1', 0x01, 0x00 }
            .CopyTo(descriptor);
        Encoding.ASCII.GetBytes(label).CopyTo(descriptor[40..]);
        descriptor[128] = 0x00;
        descriptor[129] = 0x08;
        descriptor[158] = 17;
        image[17 * 2048 - 4] = 18;
        return image;
    }

    private static void AddIsoFile(byte[] image, string filename, byte[] contents)
    {
        const int rootOffset = 17 * 2048;
        var entryOffset = rootOffset;
        while (image[entryOffset] != 0)
            entryOffset += image[entryOffset];

        var nextSector = image[rootOffset - 4] |
                         (image[rootOffset - 3] << 8) |
                         (image[rootOffset - 2] << 16);
        var nameBytes = Encoding.ASCII.GetBytes(filename);
        image[entryOffset] = (byte)(nameBytes.Length + 48);
        image[entryOffset + 2] = (byte)nextSector;
        image[entryOffset + 3] = (byte)(nextSector >> 8);
        image[entryOffset + 10] = (byte)contents.Length;
        image[entryOffset + 11] = (byte)(contents.Length >> 8);
        image[entryOffset + 12] = (byte)(contents.Length >> 16);
        image[entryOffset + 32] = (byte)(nameBytes.Length + 2);
        nameBytes.CopyTo(image.AsSpan(entryOffset + 33));
        image[entryOffset + 33 + nameBytes.Length] = (byte)';';
        image[entryOffset + 34 + nameBytes.Length] = (byte)'1';

        contents.CopyTo(image.AsSpan(nextSector * 2048));
        nextSector += (contents.Length + 2047) / 2048;
        image[rootOffset - 4] = (byte)nextSector;
        image[rootOffset - 3] = (byte)(nextSector >> 8);
        image[rootOffset - 2] = (byte)(nextSector >> 16);
    }
}
