using System.IO.Compression;
using System.Text;
using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class ZipSaveExportSinkTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "emushelf-export-" + Guid.NewGuid().ToString("N"));

    public ZipSaveExportSinkTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Complete_WritesEntriesToTheDestinationZip()
    {
        var destination = Path.Combine(_directory, "saves.zip");
        using (var sink = new ZipSaveExportSink(destination))
        {
            await sink.AddFileAsync("PlayStation 2/Mcd001.ps2", Stream("card"));
            await sink.AddFileAsync("folder/a/b.txt", Stream("nested"));
            sink.Complete();
        }

        Assert.True(File.Exists(destination));
        Assert.False(File.Exists(destination + ".emushelf-tmp"));
        using var archive = ZipFile.OpenRead(destination);
        Assert.Equal("card", ReadEntry(archive, "PlayStation 2/Mcd001.ps2"));
        Assert.Equal("nested", ReadEntry(archive, "folder/a/b.txt"));
    }

    [Fact]
    public async Task WithoutComplete_LeavesNoFileAtTheDestination()
    {
        var destination = Path.Combine(_directory, "saves.zip");
        var sink = new ZipSaveExportSink(destination);
        await sink.AddFileAsync("x.bin", Stream("data"));
        sink.Dispose(); // Simulates a failed/abandoned export.

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".emushelf-tmp"));
    }

    [Fact]
    public void Complete_ReplacesAnExistingFileAtTheDestination()
    {
        var destination = Path.Combine(_directory, "saves.zip");
        File.WriteAllText(destination, "stale");

        using (var sink = new ZipSaveExportSink(destination))
            sink.Complete();

        using var archive = ZipFile.OpenRead(destination);
        Assert.Empty(archive.Entries);
    }

    private static MemoryStream Stream(string value) => new(Encoding.UTF8.GetBytes(value), writable: false);

    private static string ReadEntry(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
