using System.Buffers.Binary;

namespace EmuShelf.Integrations.Importing;

internal enum NintendoDiscSystem
{
    Unknown,
    GameCube,
    Wii,
}

/// <summary>
/// Reads only the small, uncompressed disc-header area exposed by each supported
/// Dolphin container. It never decompresses or modifies an image.
/// </summary>
internal static class NintendoDiscDetector
{
    private const uint GameCubeMagic = 0xC2339F3D;
    private const uint WiiMagic = 0x5D1C9EA3;
    private const int DiscHeaderSize = 0x20;
    private const int CisoHeaderSize = 0x8000;
    private const int RvzHeader1Size = 0x48;
    private const int RvzDiscHeaderOffsetInHeader2 = 0x10;
    private const int RvzMinimumHeader2Size = 0xD5;

    public static NintendoDiscSystem Detect(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var discHeaderOffset = extension switch
            {
                ".iso" or ".gcm" => 0,
                ".ciso" => GetCisoDiscHeaderOffset(stream),
                ".rvz" => GetRvzDiscHeaderOffset(stream),
                ".wbfs" => GetWbfsDiscHeaderOffset(stream),
                _ => -1,
            };

            return discHeaderOffset >= 0
                ? DetectAt(stream, discHeaderOffset)
                : NintendoDiscSystem.Unknown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return NintendoDiscSystem.Unknown;
        }
    }

    private static long GetCisoDiscHeaderOffset(Stream stream)
    {
        Span<byte> header = stackalloc byte[9];
        if (!ReadAt(stream, 0, header) || !header[..4].SequenceEqual("CISO"u8))
            return -1;

        var blockSize = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        return blockSize >= DiscHeaderSize && header[8] == 1 ? CisoHeaderSize : -1;
    }

    private static long GetRvzDiscHeaderOffset(Stream stream)
    {
        Span<byte> header = stackalloc byte[0x10];
        if (!ReadAt(stream, 0, header) || !header[..4].SequenceEqual("RVZ\x01"u8))
            return -1;

        var header2Size = BinaryPrimitives.ReadUInt32BigEndian(header[0x0C..0x10]);
        return header2Size >= RvzMinimumHeader2Size
            ? RvzHeader1Size + RvzDiscHeaderOffsetInHeader2
            : -1;
    }

    private static long GetWbfsDiscHeaderOffset(Stream stream)
    {
        Span<byte> header = stackalloc byte[13];
        if (!ReadAt(stream, 0, header) || !header[..4].SequenceEqual("WBFS"u8))
            return -1;

        var hdSectorShift = header[8];
        var hasDiscInFirstSlot = header[12] != 0;
        return hdSectorShift is >= 9 and <= 31 && hasDiscInFirstSlot
            ? 1L << hdSectorShift
            : -1;
    }

    private static NintendoDiscSystem DetectAt(Stream stream, long offset)
    {
        Span<byte> header = stackalloc byte[DiscHeaderSize];
        if (!ReadAt(stream, offset, header))
            return NintendoDiscSystem.Unknown;

        var hasWiiMagic = BinaryPrimitives.ReadUInt32BigEndian(header[0x18..0x1C]) == WiiMagic;
        var hasGameCubeMagic = BinaryPrimitives.ReadUInt32BigEndian(header[0x1C..0x20]) == GameCubeMagic;

        return (hasGameCubeMagic, hasWiiMagic) switch
        {
            (true, false) => NintendoDiscSystem.GameCube,
            (false, true) => NintendoDiscSystem.Wii,
            _ => NintendoDiscSystem.Unknown,
        };
    }

    private static bool ReadAt(Stream stream, long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset > stream.Length - buffer.Length)
            return false;

        stream.Position = offset;
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                return false;
            totalRead += read;
        }

        return true;
    }
}
