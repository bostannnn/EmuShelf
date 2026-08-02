using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Storage;

public sealed class AtomicFileTests : TempAppDirectoryTestBase
{
    [Fact]
    public void WriteAllText_ReplacesExistingContent()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "atomic.txt");
        File.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.EnumerateFiles(BaseDirectory, "*.tmp"));
    }

    [Fact]
    public async Task WriteAllText_RetriesAShortWindowsDestinationLock()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "locked.txt");
        File.WriteAllText(path, "old");
        using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var write = Task.Run(() => AtomicFile.WriteAllText(path, "new"));
        await Task.Delay(100);
        locked.Dispose();
        await write;

        Assert.Equal("new", File.ReadAllText(path));
    }
}
