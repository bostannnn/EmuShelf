using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Tests.Storage;

public sealed class JsonDataLocationStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "EmuShelfTests", Guid.NewGuid().ToString("N"));

    private string PointerPath => Path.Combine(_directory, "data-location.json");

    [Fact]
    public void Read_ReturnsNull_BeforeAnythingIsWritten()
    {
        Assert.Null(new JsonDataLocationStore(PointerPath).Read());
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllFields()
    {
        var store = new JsonDataLocationStore(PointerPath);
        var chosenAt = DateTimeOffset.UtcNow;
        store.Write(new DataLocation(
            "/storage/AE6A-1092/EmuShelf",
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3AEmuShelf",
            chosenAt));

        var read = store.Read();

        Assert.NotNull(read);
        Assert.Equal("/storage/AE6A-1092/EmuShelf", read!.BaseDirectory);
        Assert.Equal(
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3AEmuShelf",
            read.SourceUri);
        Assert.Equal(chosenAt, read.ChosenAtUtc);
    }

    [Fact]
    public void Write_CreatesTheParentDirectory()
    {
        // The app-private files dir always exists on Android, but a nested pointer path must not require
        // the caller to pre-create it.
        var nested = Path.Combine(_directory, "nested", "data-location.json");
        new JsonDataLocationStore(nested).Write(new DataLocation("/somewhere"));

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Read_ReturnsNull_ForCorruptJson()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PointerPath, "{ not valid json");

        Assert.Null(new JsonDataLocationStore(PointerPath).Read());
    }

    [Fact]
    public void Read_ReturnsNull_WhenBaseDirectoryIsBlank()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(PointerPath, "{ \"BaseDirectory\": \"\" }");

        Assert.Null(new JsonDataLocationStore(PointerPath).Read());
    }

    [Fact]
    public void Clear_RemovesThePointer_AndIsSafeWhenAbsent()
    {
        var store = new JsonDataLocationStore(PointerPath);
        store.Write(new DataLocation("/somewhere"));
        Assert.NotNull(store.Read());

        store.Clear();
        Assert.Null(store.Read());

        // A second clear on a now-absent file must not throw.
        store.Clear();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
