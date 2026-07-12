using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests;

/// <summary>Gives each test its own throwaway app directory, cleaned up afterwards.</summary>
public abstract class TempAppDirectoryTestBase : IDisposable
{
    protected string BaseDirectory { get; } =
        Path.Combine(Path.GetTempPath(), "EmuShelfTests", Guid.NewGuid().ToString("N"));

    protected AppPaths AppPaths { get; }

    protected TempAppDirectoryTestBase()
    {
        AppPaths = new AppPaths(BaseDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(BaseDirectory))
            Directory.Delete(BaseDirectory, recursive: true);
    }
}
