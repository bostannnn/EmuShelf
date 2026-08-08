using EmuShelf.Core.Emulators;
using EmuShelf.Infrastructure.Emulators;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public class JsonEmulatorInstallManifestStoreTests : TempAppDirectoryTestBase
{
    private static EmulatorInstallRecord Record(string id, string version = "1.0") =>
        new(id, version, new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), $"Emulators/{id}/run", $"tag-{version}");

    [Fact]
    public void Save_ThenGet_RoundTripsAcrossInstances()
    {
        var store = new JsonEmulatorInstallManifestStore(AppPaths);
        var record = Record("duckstation", "0.1-7800");
        store.Save(record);

        // A fresh instance must read the persisted file, not a cached dictionary.
        var reopened = new JsonEmulatorInstallManifestStore(AppPaths);
        Assert.Equal(record, reopened.Get("duckstation"));
    }

    [Fact]
    public void Get_ForUnknownEmulator_ReturnsNull()
    {
        var store = new JsonEmulatorInstallManifestStore(AppPaths);
        Assert.Null(store.Get("pcsx2"));
    }

    [Fact]
    public void Save_ForSameEmulator_ReplacesRatherThanDuplicates()
    {
        var store = new JsonEmulatorInstallManifestStore(AppPaths);
        store.Save(Record("pcsx2", "2.0"));
        store.Save(Record("pcsx2", "2.1"));

        Assert.Equal("2.1", store.Get("pcsx2")!.InstalledVersion);
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void GetAll_ReturnsRecordsOrderedById()
    {
        var store = new JsonEmulatorInstallManifestStore(AppPaths);
        store.Save(Record("rpcs3"));
        store.Save(Record("azahar"));
        store.Save(Record("ppsspp"));

        Assert.Equal(new[] { "azahar", "ppsspp", "rpcs3" }, store.GetAll().Select(r => r.EmulatorId));
    }

    [Fact]
    public void Remove_DeletesOnlyTheNamedRecord()
    {
        var store = new JsonEmulatorInstallManifestStore(AppPaths);
        store.Save(Record("duckstation"));
        store.Save(Record("pcsx2"));

        store.Remove("duckstation");

        Assert.Null(store.Get("duckstation"));
        Assert.NotNull(store.Get("pcsx2"));
    }

    [Fact]
    public void Load_ToleratesACorruptManifest_ByStartingEmpty()
    {
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        File.WriteAllText(Path.Combine(AppPaths.SettingsDirectory, "emulator-installs.json"), "{ not json");

        var store = new JsonEmulatorInstallManifestStore(AppPaths);

        Assert.Empty(store.GetAll());
        // The store must still be writable after recovering from the corrupt file.
        store.Save(Record("duckstation"));
        Assert.NotNull(store.Get("duckstation"));
    }
}
