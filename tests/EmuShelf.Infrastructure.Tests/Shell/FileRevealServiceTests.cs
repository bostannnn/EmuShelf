using System.Diagnostics;
using EmuShelf.Infrastructure.Shell;

namespace EmuShelf.Infrastructure.Tests.Shell;

public class FileRevealServiceTests
{
    [Fact]
    public void WindowsSelect_PassesOneRawQuotedSelectToken()
    {
        var info = FileRevealService.BuildWindowsSelectStartInfo(@"C:\Games\PS2\Ico (USA).iso");

        Assert.Equal("explorer.exe", info.FileName);
        // explorer parses "/select,<path>" itself, so it must arrive as one raw quoted token —
        // ArgumentList's argv escaping would break it (explorer would open Documents instead).
        Assert.Equal("/select,\"C:\\Games\\PS2\\Ico (USA).iso\"", info.Arguments);
        Assert.Empty(info.ArgumentList);
    }

    [Fact]
    public void MacSelect_UsesOpenRevealFlag()
    {
        var info = FileRevealService.BuildMacSelectStartInfo("/Users/me/Games/Ico.iso");

        Assert.Equal("open", info.FileName);
        Assert.Equal(new[] { "-R", "/Users/me/Games/Ico.iso" }, info.ArgumentList);
    }

    [Fact]
    public void LinuxSelect_CallsFileManager1ShowItemsWithAFileUri()
    {
        var path = Path.Combine(Path.GetTempPath(), "Ico (USA).iso");

        var info = FileRevealService.BuildLinuxSelectStartInfo(path);

        Assert.Equal("dbus-send", info.FileName);
        Assert.Contains("org.freedesktop.FileManager1.ShowItems", info.ArgumentList);
        var uriArg = Assert.Single(info.ArgumentList, a => a.StartsWith("array:string:file:"));
        Assert.Equal($"array:string:{new Uri(path).AbsoluteUri}", uriArg);
        Assert.Equal("string:", info.ArgumentList[^1]); // empty startup-id trailer
    }

    [Fact]
    public void LinuxSelect_EncodesCommasSoTheItemStaysOneArrayElement()
    {
        var path = Path.Combine(Path.GetTempPath(), "Fear Effect 2, Retro Helix.iso");

        var info = FileRevealService.BuildLinuxSelectStartInfo(path);

        var uriArg = Assert.Single(info.ArgumentList, a => a.StartsWith("array:string:file:"));
        Assert.DoesNotContain(",", uriArg); // a literal comma would split the dbus-send array
        Assert.Contains("%2C", uriArg);
    }

    [Fact]
    public void ToFileUri_PercentEncodesSpaces()
    {
        var path = Path.Combine(Path.GetTempPath(), "My Game.iso");

        Assert.Contains("%20", FileRevealService.ToFileUri(path));
    }

    [Fact]
    public async Task RevealAsync_RejectsAnEmptyPath()
    {
        var service = new FileRevealService(_ => throw new InvalidOperationException("must not start"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.RevealAsync("   "));
    }

    [Fact]
    public async Task RevealAsync_ThrowsWhenNeitherTheItemNorItsFolderExists()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "gone.iso");
        var service = new FileRevealService(_ => throw new InvalidOperationException("must not start"));

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.RevealAsync(missing));
    }

    [Fact]
    public async Task RevealAsync_SelectsAnExistingFileInItsFolder()
    {
        // The Linux reveal awaits dbus-send, which an unstarted fake process can't satisfy; its
        // argument construction is covered by BuildLinuxSelectStartInfo above.
        if (OperatingSystem.IsLinux())
            return;

        var directory = NewTempDirectory();
        var file = Path.Combine(directory, "game.iso");
        await File.WriteAllTextAsync(file, "x");
        try
        {
            ProcessStartInfo? captured = null;
            var service = new FileRevealService(info => { captured = info; return new Process(); });

            await service.RevealAsync(file);

            Assert.NotNull(captured);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("explorer.exe", captured!.FileName);
                Assert.Contains("/select,", captured.Arguments);
                Assert.Contains(file, captured.Arguments);
            }
            else
            {
                Assert.Equal("open", captured!.FileName);
                Assert.Contains("-R", captured.ArgumentList);
                Assert.Contains(file, captured.ArgumentList);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RevealAsync_OpensTheFolderWhenOnlyTheFolderSurvives()
    {
        var directory = NewTempDirectory();
        var missingFile = Path.Combine(directory, "moved.iso");
        try
        {
            ProcessStartInfo? captured = null;
            var service = new FileRevealService(info => { captured = info; return new Process(); });

            await service.RevealAsync(missingFile);

            Assert.NotNull(captured);
            if (OperatingSystem.IsWindows())
            {
                Assert.Equal("explorer.exe", captured!.FileName);
                Assert.DoesNotContain("/select,", captured.Arguments); // opened, not selected
                Assert.Contains(directory, captured.Arguments);
            }
            else
            {
                Assert.Equal(OperatingSystem.IsMacOS() ? "open" : "xdg-open", captured!.FileName);
                Assert.Contains(directory, captured.ArgumentList);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "EmuShelfRevealTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
