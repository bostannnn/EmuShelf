using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Storage;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace EmuShelf.App.Tests;

public class GameCoverServiceTests : IDisposable
{
    private readonly string _baseDirectory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfCoverTests",
        Guid.NewGuid().ToString("N"));
    private readonly AppPaths _paths;

    public GameCoverServiceTests()
    {
        _paths = new AppPaths(_baseDirectory);
        _paths.EnsureDirectoriesExist();
    }

    [AvaloniaFact]
    public async Task ImportAsync_CopiesSourceAndCreatesBoundedPngThumbnail()
    {
        var sourcePath = CreateImage("source.png", new PixelSize(600, 900));
        var service = new GameCoverService(_paths);

        var imported = await service.ImportAsync(42, sourcePath);

        Assert.True(File.Exists(sourcePath));
        Assert.Equal(_paths.CoversDirectory, Path.GetDirectoryName(imported.CoverPath));
        Assert.StartsWith("42-", Path.GetFileName(imported.CoverPath));
        Assert.Equal(".png", Path.GetExtension(imported.CoverPath));
        Assert.Equal(
            Path.GetFileNameWithoutExtension(imported.CoverPath) + ".png",
            Path.GetFileName(imported.ThumbnailPath));
        Assert.Equal(
            Path.Combine(_paths.CacheDirectory, "Covers"),
            Path.GetDirectoryName(imported.ThumbnailPath));
        Assert.True(File.Exists(imported.CoverPath));
        using var thumbnail = new Bitmap(imported.ThumbnailPath);
        Assert.True(thumbnail.PixelSize.Width > 0);
        Assert.True(thumbnail.PixelSize.Height > 0);
        Assert.Equal(
            new PixelSize(267, 400),
            GameCoverService.CalculateThumbnailSize(new PixelSize(600, 900)));
    }

    [AvaloniaFact]
    public async Task GetThumbnailAsync_ReusesCurrentCache()
    {
        var sourcePath = CreateImage("source.png", new PixelSize(300, 400));
        var service = new GameCoverService(_paths);
        var imported = await service.ImportAsync(7, sourcePath);
        var timestamp = File.GetLastWriteTimeUtc(imported.ThumbnailPath);

        var cachedPath = await service.GetThumbnailAsync(7, imported.CoverPath);

        Assert.Equal(imported.ThumbnailPath, cachedPath);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(imported.ThumbnailPath));
    }

    [AvaloniaFact]
    public async Task DeleteOwnedCoverAsync_RemovesOnlyConfirmedOldAppOwnedCover()
    {
        var firstSource = CreateImage("first.png", new PixelSize(300, 400));
        var secondSource = CreateImage("second.jpg", new PixelSize(300, 400));
        var service = new GameCoverService(_paths);
        var firstImport = await service.ImportAsync(5, firstSource);

        var secondImport = await service.ImportAsync(5, secondSource);
        Assert.True(File.Exists(firstImport.CoverPath));
        Assert.True(File.Exists(secondImport.CoverPath));

        await service.DeleteOwnedCoverAsync(5, firstImport.CoverPath);
        await service.DeleteOwnedCoverAsync(5, firstSource);

        Assert.False(File.Exists(firstImport.CoverPath));
        Assert.False(File.Exists(firstImport.ThumbnailPath));
        Assert.True(File.Exists(secondImport.CoverPath));
        Assert.True(File.Exists(firstSource));
        Assert.True(File.Exists(secondSource));
    }

    [AvaloniaFact]
    public void GameViewModel_LoadedArtworkUsesItsActualAspectRatio()
    {
        var sourcePath = CreateImage("pal-dreamcast.png", new PixelSize(512, 722));
        var game = new Game
        {
            Id = 1,
            SystemId = "dreamcast",
            Path = Path.Combine(_baseDirectory, "game.gdi"),
            Title = "Example",
            DateAdded = DateTimeOffset.UtcNow,
        };
        var viewModel = new GameViewModel(
            game,
            "Dreamcast",
            "DC",
            "#F07C3E",
            coverAspectRatio: 1.0);

        viewModel.CoverImage = new Bitmap(sourcePath);

        Assert.Equal(512d / 722d, viewModel.CoverAspectRatio, precision: 3);
        Assert.Equal(37d, viewModel.ListCoverWidth);
        viewModel.Dispose();
    }

    private string CreateImage(string fileName, PixelSize size)
    {
        var path = Path.Combine(_baseDirectory, fileName);
        using var output = File.Create(path);
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)size.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)size.Height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WritePngChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var scanline = new byte[1 + (size.Width * 4)];
            for (var x = 0; x < size.Width; x++)
            {
                var pixel = 1 + (x * 4);
                scanline[pixel] = 64;
                scanline[pixel + 1] = 128;
                scanline[pixel + 2] = 224;
                scanline[pixel + 3] = 255;
            }
            for (var y = 0; y < size.Height; y++)
                zlib.Write(scanline);
        }
        WritePngChunk(output, "IDAT", compressed.ToArray());
        WritePngChunk(output, "IEND", []);
        return path;
    }

    private static void WritePngChunk(Stream output, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)data.Length);
        output.Write(number);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = 0xffffffffu;
        foreach (var value in typeBytes.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xffffffffu);
        output.Write(number);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }
}
