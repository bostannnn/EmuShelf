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
        // Release the lock from a dedicated thread. An awaited continuation would be posted back to
        // xUnit's bounded worker pool and queued behind other running tests, while the writer burns
        // its retry budget on wall-clock schedule — so on a loaded runner this "short" lock could
        // outlive the budget and fail the test for reasons unrelated to the code under test.
        var release = new Thread(() =>
        {
            Thread.Sleep(100);
            locked.Dispose();
        }) { IsBackground = true };
        release.Start();
        await write;
        release.Join();

        Assert.Equal("new", File.ReadAllText(path));
    }
}
