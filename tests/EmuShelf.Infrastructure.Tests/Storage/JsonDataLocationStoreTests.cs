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

    private string MirrorPath => Path.Combine(_directory, "shared", ".emushelf-data-location.json");

    [Fact]
    public void Write_AlsoWritesTheMirror_AndReadPrefersThePrimary()
    {
        var store = new JsonDataLocationStore(PointerPath, MirrorPath);
        store.Write(new DataLocation("/storage/emulated/0/User/EmuShelf"));

        Assert.True(File.Exists(MirrorPath));
        Assert.Equal("/storage/emulated/0/User/EmuShelf", store.Read()!.BaseDirectory);
    }

    [Fact]
    public void Read_FallsBackToTheMirror_WhenThePrimaryIsGone()
    {
        // An uninstall wipes app-private storage (the primary) but not shared storage (the mirror): the
        // reinstalled app must find its folder again instead of re-running onboarding over it.
        new JsonDataLocationStore(PointerPath, MirrorPath).Write(new DataLocation("/storage/emulated/0/User/EmuShelf"));
        File.Delete(PointerPath);

        var read = new JsonDataLocationStore(PointerPath, MirrorPath).Read();

        Assert.Equal("/storage/emulated/0/User/EmuShelf", read!.BaseDirectory);
    }

    [Fact]
    public void Read_RecreatesAMissingMirror_FromThePrimary()
    {
        // An install that pre-dates the mirror has only the primary; its first resolve heals the mirror.
        new JsonDataLocationStore(PointerPath).Write(new DataLocation("/storage/emulated/0/User/EmuShelf"));
        Assert.False(File.Exists(MirrorPath));

        var read = new JsonDataLocationStore(PointerPath, MirrorPath).Read();

        Assert.Equal("/storage/emulated/0/User/EmuShelf", read!.BaseDirectory);
        Assert.True(File.Exists(MirrorPath));
    }

    [Fact]
    public void MirrorFailures_NeverBreakThePrimary()
    {
        // Before the all-files grant the mirror's location is not writable. Simulate with a mirror path whose
        // parent "directory" is a plain file, so creating it throws.
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var store = new JsonDataLocationStore(PointerPath, Path.Combine(blocker, "mirror.json"));

        store.Write(new DataLocation("/somewhere"));
        Assert.Equal("/somewhere", store.Read()!.BaseDirectory);

        store.Clear();
        Assert.Null(store.Read());
    }

    [Fact]
    public void Clear_RemovesTheMirrorToo()
    {
        var store = new JsonDataLocationStore(PointerPath, MirrorPath);
        store.Write(new DataLocation("/somewhere"));

        store.Clear();

        Assert.False(File.Exists(MirrorPath));
        Assert.Null(store.Read());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
