using System.Buffers.Binary;
using Avalonia;
using Avalonia.Media.Imaging;

namespace EmuShelf.App.Services;

internal static class SafeImageDecoder
{
    internal const int MaximumDimension = 16_384;
    internal const long MaximumPixels = 40_000_000;

    public static Bitmap DecodeToFit(string path, int maximumWidth, int maximumHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumHeight);

        using var stream = File.OpenRead(path);
        var sourceSize = ReadAndValidatePixelSize(stream);
        var destinationSize = CalculateFit(sourceSize, maximumWidth, maximumHeight);
        stream.Position = 0;

        if (destinationSize == sourceSize)
            return new Bitmap(stream);

        var widthScale = maximumWidth / (double)sourceSize.Width;
        var heightScale = maximumHeight / (double)sourceSize.Height;
        return widthScale <= heightScale
            ? Bitmap.DecodeToWidth(stream, destinationSize.Width, BitmapInterpolationMode.HighQuality)
            : Bitmap.DecodeToHeight(stream, destinationSize.Height, BitmapInterpolationMode.HighQuality);
    }

    internal static PixelSize ReadAndValidatePixelSize(Stream stream)
    {
        if (!stream.CanSeek)
            throw new InvalidDataException("The cover image could not be inspected safely.");

        var size = TryReadPng(stream)
            ?? TryReadJpeg(stream)
            ?? TryReadBmp(stream)
            ?? TryReadWebP(stream)
            ?? throw new InvalidDataException("The cover image header is invalid or unsupported.");

        if (size.Width <= 0 || size.Height <= 0 ||
            size.Width > MaximumDimension || size.Height > MaximumDimension ||
            (long)size.Width * size.Height > MaximumPixels)
        {
            throw new InvalidDataException(
                $"The cover image dimensions exceed EmuShelf's {MaximumPixels:N0}-pixel safety limit.");
        }

        return size;
    }

    private static PixelSize CalculateFit(PixelSize source, int maximumWidth, int maximumHeight)
    {
        var scale = Math.Min(
            1d,
            Math.Min(maximumWidth / (double)source.Width, maximumHeight / (double)source.Height));
        return new PixelSize(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)));
    }

    private static PixelSize? TryReadPng(Stream stream)
    {
        Span<byte> header = stackalloc byte[24];
        if (!TryReadAtStart(stream, header) ||
            !header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
            !header[12..16].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        return width <= int.MaxValue && height <= int.MaxValue
            ? new PixelSize((int)width, (int)height)
            : null;
    }

    private static PixelSize? TryReadJpeg(Stream stream)
    {
        stream.Position = 0;
        if (ReadByte(stream) != 0xFF || ReadByte(stream) != 0xD8)
            return null;

        while (stream.Position < stream.Length)
        {
            int markerPrefix;
            do
            {
                markerPrefix = ReadByte(stream);
            } while (markerPrefix != -1 && markerPrefix != 0xFF);
            if (markerPrefix == -1)
                return null;

            int marker;
            do
            {
                marker = ReadByte(stream);
            } while (marker == 0xFF);
            if (marker is -1 or 0xD9 or 0xDA)
                return null;
            if (marker is 0x01 or >= 0xD0 and <= 0xD7)
                continue;

            var segmentLength = ReadBigEndianUInt16(stream);
            if (segmentLength < 2 || segmentLength - 2 > stream.Length - stream.Position)
                return null;

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7 || ReadByte(stream) == -1)
                    return null;
                var height = ReadBigEndianUInt16(stream);
                var width = ReadBigEndianUInt16(stream);
                return width > 0 && height > 0 ? new PixelSize(width, height) : null;
            }

            stream.Position += segmentLength - 2;
        }

        return null;
    }

    private static bool IsStartOfFrame(int marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or
        0xC5 or 0xC6 or 0xC7 or
        0xC9 or 0xCA or 0xCB or
        0xCD or 0xCE or 0xCF;

    private static PixelSize? TryReadBmp(Stream stream)
    {
        Span<byte> header = stackalloc byte[26];
        if (!TryReadAtStart(stream, header) || !header[..2].SequenceEqual("BM"u8))
            return null;

        var dibSize = BinaryPrimitives.ReadUInt32LittleEndian(header[14..18]);
        if (dibSize == 12)
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(header[18..20]);
            var height = BinaryPrimitives.ReadUInt16LittleEndian(header[20..22]);
            return new PixelSize(width, height);
        }
        if (dibSize < 40)
            return null;

        var signedWidth = BinaryPrimitives.ReadInt32LittleEndian(header[18..22]);
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(header[22..26]);
        if (signedWidth <= 0 || signedHeight == 0 || signedHeight == int.MinValue)
            return null;
        return new PixelSize(signedWidth, Math.Abs(signedHeight));
    }

    private static PixelSize? TryReadWebP(Stream stream)
    {
        Span<byte> container = stackalloc byte[12];
        if (!TryReadAtStart(stream, container) || !container[..4].SequenceEqual("RIFF"u8) ||
            !container[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        Span<byte> chunkHeader = stackalloc byte[8];
        while (stream.Position + chunkHeader.Length <= stream.Length)
        {
            if (!TryRead(stream, chunkHeader))
                return null;
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(chunkHeader[4..8]);
            var dataPosition = stream.Position;
            if (chunkSize > stream.Length - dataPosition)
                return null;

            if (chunkHeader[..4].SequenceEqual("VP8X"u8) && chunkSize >= 10)
            {
                Span<byte> data = stackalloc byte[10];
                if (!TryRead(stream, data))
                    return null;
                return new PixelSize(ReadUInt24(data[4..7]) + 1, ReadUInt24(data[7..10]) + 1);
            }
            if (chunkHeader[..4].SequenceEqual("VP8L"u8) && chunkSize >= 5)
            {
                Span<byte> data = stackalloc byte[5];
                if (!TryRead(stream, data) || data[0] != 0x2F)
                    return null;
                var width = 1 + data[1] + ((data[2] & 0x3F) << 8);
                var height = 1 + (data[2] >> 6) + (data[3] << 2) + ((data[4] & 0x0F) << 10);
                return new PixelSize(width, height);
            }
            if (chunkHeader[..4].SequenceEqual("VP8 "u8) && chunkSize >= 10)
            {
                Span<byte> data = stackalloc byte[10];
                if (!TryRead(stream, data) || !data[3..6].SequenceEqual(new byte[] { 0x9D, 0x01, 0x2A }))
                    return null;
                var width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]) & 0x3FFF;
                var height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]) & 0x3FFF;
                return new PixelSize(width, height);
            }

            stream.Position = dataPosition + chunkSize + (chunkSize & 1);
        }

        return null;
    }

    private static bool TryReadAtStart(Stream stream, Span<byte> buffer)
    {
        stream.Position = 0;
        return TryRead(stream, buffer);
    }

    private static bool TryRead(Stream stream, Span<byte> buffer)
    {
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

    private static int ReadByte(Stream stream) => stream.ReadByte();

    private static int ReadBigEndianUInt16(Stream stream)
    {
        var high = ReadByte(stream);
        var low = ReadByte(stream);
        return high < 0 || low < 0 ? -1 : (high << 8) | low;
    }

    private static int ReadUInt24(ReadOnlySpan<byte> value) =>
        value[0] | (value[1] << 8) | (value[2] << 16);
}
