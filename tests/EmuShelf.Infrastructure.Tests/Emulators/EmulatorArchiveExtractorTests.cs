using System.IO.Compression;
using System.Text;
using EmuShelf.Core.Emulators;
using EmuShelf.Infrastructure.Emulators;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public class EmulatorArchiveExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "EmuShelfTests", "extract", Guid.NewGuid().ToString("N"));

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "EmulatorInstall", name);

    public EmulatorArchiveExtractorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Extract_Zip_WritesNestedFiles()
    {
        var zipPath = Path.Combine(_root, "sample.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("payload/tool.exe");
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("ZIP-EXE"));
        }

        var destination = Path.Combine(_root, "out-zip");
        EmulatorArchiveExtractor.Extract(zipPath, EmulatorArchiveKind.Zip, destination);

        Assert.Equal("ZIP-EXE", File.ReadAllText(Path.Combine(destination, "payload", "tool.exe")));
    }

    [Fact]
    public void Extract_SevenZip_WritesNestedFiles()
    {
        var destination = Path.Combine(_root, "out-7z");
        EmulatorArchiveExtractor.Extract(FixturePath("sample.7z"), EmulatorArchiveKind.SevenZip, destination);

        Assert.Equal("EMUSHELF-FIXTURE-EXE", File.ReadAllText(Path.Combine(destination, "payload", "tool.exe")));
        Assert.Equal("readme", File.ReadAllText(Path.Combine(destination, "payload", "data", "readme.txt")));
    }

    [Fact]
    public void Extract_TarXz_WritesNestedFiles()
    {
        var destination = Path.Combine(_root, "out-tarxz");
        EmulatorArchiveExtractor.Extract(FixturePath("sample.tar.xz"), EmulatorArchiveKind.TarXz, destination);

        Assert.Equal("EMUSHELF-FIXTURE-EXE", File.ReadAllText(Path.Combine(destination, "payload", "tool.exe")));
        Assert.Equal("readme", File.ReadAllText(Path.Combine(destination, "payload", "data", "readme.txt")));
    }

    [Fact]
    public void Extract_AppImage_PlacesFileAndMarksExecutable()
    {
        var appImage = Path.Combine(_root, "TestEmu-x64.AppImage");
        File.WriteAllText(appImage, "APPIMAGE-BODY");

        var destination = Path.Combine(_root, "out-appimage");
        EmulatorArchiveExtractor.Extract(appImage, EmulatorArchiveKind.AppImage, destination);

        var placed = Path.Combine(destination, "TestEmu-x64.AppImage");
        Assert.Equal("APPIMAGE-BODY", File.ReadAllText(placed));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(placed).HasFlag(UnixFileMode.UserExecute));
    }

    [Fact]
    public void ResolveExecutable_PrefersTheShallowestMatch()
    {
        // Extract the fixture, then confirm the runnable file is located by pattern.
        var destination = Path.Combine(_root, "resolve");
        EmulatorArchiveExtractor.Extract(FixturePath("sample.tar.xz"), EmulatorArchiveKind.TarXz, destination);

        var rule = new EmulatorReleaseAsset("linux", "x64", "n/a", EmulatorArchiveKind.TarXz, @"tool\.exe$");
        Assert.Equal("payload/tool.exe", EmulatorInstallService.ResolveExecutable(destination, rule));
    }
}
