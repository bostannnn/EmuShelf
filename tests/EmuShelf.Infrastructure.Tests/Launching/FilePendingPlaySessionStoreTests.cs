using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public class FilePendingPlaySessionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "EmuShelfPendingSession", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "pending.json");

    [Fact]
    public void Get_WhenNothingStored_ReturnsNull()
    {
        Assert.Null(new FilePendingPlaySessionStore(FilePath).Get());
    }

    [Fact]
    public void SetThenGet_RoundTripsAcrossInstances()
    {
        // A fresh instance simulates a process restart: the record must survive on disk, including the
        // emulator package the return handler needs to close it.
        new FilePendingPlaySessionStore(FilePath).Set(
            new PendingPlaySession(42, "Castlevania SotN", 1_700_000_000_000, "com.github.stenzek.duckstation"));

        var recovered = new FilePendingPlaySessionStore(FilePath).Get();

        Assert.Equal(
            new PendingPlaySession(42, "Castlevania SotN", 1_700_000_000_000, "com.github.stenzek.duckstation"),
            recovered);
    }

    [Fact]
    public void Get_FromRecordWithoutEmulatorPackage_LeavesItNull()
    {
        // Records written before the field existed (and desktop-shaped ones) have no package; the store
        // must read them back with a null EmulatorPackage rather than failing, so nothing is closed.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{"GameId":9,"GameTitle":"Ico","StartedAtUnixMs":1000}""");

        var recovered = new FilePendingPlaySessionStore(FilePath).Get();

        Assert.Equal(new PendingPlaySession(9, "Ico", 1000), recovered);
        Assert.Null(recovered!.EmulatorPackage);
    }

    [Fact]
    public void Set_OverwritesThePreviousSession()
    {
        var store = new FilePendingPlaySessionStore(FilePath);
        store.Set(new PendingPlaySession(1, "First", 1000));
        store.Set(new PendingPlaySession(2, "Second", 2000));

        Assert.Equal(2, store.Get()!.GameId);
    }

    [Fact]
    public void Clear_RemovesTheSession()
    {
        var store = new FilePendingPlaySessionStore(FilePath);
        store.Set(new PendingPlaySession(7, "Game", 500));

        store.Clear();

        Assert.Null(store.Get());
    }

    [Fact]
    public void Get_WithCorruptFile_ReturnsNullWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not valid json");

        Assert.Null(new FilePendingPlaySessionStore(FilePath).Get());
    }

    [Fact]
    public void Clear_WhenNothingStored_DoesNotThrow()
    {
        new FilePendingPlaySessionStore(FilePath).Clear();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
