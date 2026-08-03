using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.Azahar;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class AzaharSaveSyncTests : TempAppDirectoryTestBase
{
    // Two consoles with distinct 32-hex ID0/ID1 pairs, to prove a save keyed by title id crosses
    // between machines whose on-disk save paths differ.
    private const string Id0A = "00112233445566778899aabbccddeeff";
    private const string Id1A = "ffeeddccbbaa99887766554433221100";
    private const string Id0B = "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f";
    private const string Id1B = "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1";

    [Fact]
    public async Task Provider_EnumeratesTitleDataAndExtdataUnits_IgnoringContentOnlyAndJunk()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        var console = Nintendo3ds(userDirectory, Id0A, Id1A);
        CreateDirectory(console, "title", "00040000", "00112200", "data");   // valid save archive
        CreateDirectory(console, "title", "00040000", "00112201", "content"); // update/DLC only → not a save
        CreateDirectory(console, "title", "0004000e", "000abc00", "data");   // valid save archive
        CreateDirectory(console, "title", "zzzzzzzz", "00000000", "data");   // non-hex → ignored
        CreateDirectory(console, "extdata", "00000000", "00000042");         // valid extdata
        CreateDirectory(console, "extdata", "00000000", "nothex");           // non-hex → ignored

        var provider = new AzaharSaveLocationProvider("/install", userDirectoryOverride: userDirectory);

        Assert.Equal(
            [
                new SaveUnit("azahar/extdata/00000042", "3DS extdata 00000042", SaveUnitKind.Folder),
                new SaveUnit("azahar/title/00040000/00112200", "3DS 0004000000112200", SaveUnitKind.Folder),
                new SaveUnit("azahar/title/0004000e/000abc00", "3DS 0004000E000ABC00", SaveUnitKind.Folder),
            ],
            await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public void DefaultUserDirectory_MatchesAzaharPerOsAndFlatpak()
    {
        var home = Path.Combine(BaseDirectory, "home");
        var appData = Path.Combine(BaseDirectory, "appdata");

        Assert.Equal(
            Path.Combine(appData, "Azahar"),
            AzaharSaveLocationProvider.GetDefaultUserDirectory(home, appData, isWindows: true, isMacOS: false, isFlatpak: false));
        Assert.Equal(
            Path.Combine(home, ".local", "share", "azahar-emu"),
            AzaharSaveLocationProvider.GetDefaultUserDirectory(home, appData, isWindows: false, isMacOS: false, isFlatpak: false));
        Assert.Equal(
            Path.Combine(home, "Library", "Application Support", "Azahar"),
            AzaharSaveLocationProvider.GetDefaultUserDirectory(home, appData, isWindows: false, isMacOS: true, isFlatpak: false));
        Assert.Equal(
            Path.Combine(home, ".var", "app", "org.azahar_emu.Azahar", "data", "azahar-emu"),
            AzaharSaveLocationProvider.GetDefaultUserDirectory(home, appData, isWindows: false, isMacOS: false, isFlatpak: true));
    }

    [Fact]
    public void PortableUserDirectory_BesideExecutable_WinsOverPlatformDefault()
    {
        var installation = Path.Combine(BaseDirectory, "Azahar");
        Directory.CreateDirectory(Path.Combine(installation, "user"));
        var provider = new AzaharSaveLocationProvider(
            installation,
            appDataDirectory: Path.Combine(BaseDirectory, "appdata"),
            isWindows: true);

        Assert.Equal(Path.Combine(installation, "user"), provider.GetUserDirectory());
    }

    [Fact]
    public async Task TitleSave_RoundTripsAcrossMachinesWithDifferentConsoleIds()
    {
        var pathsA = new AppPaths(Path.Combine(BaseDirectory, "machine-a"));
        var pathsB = new AppPaths(Path.Combine(BaseDirectory, "machine-b"));
        pathsA.EnsureDirectoriesExist();
        pathsB.EnsureDirectoriesExist();

        var userA = Path.Combine(pathsA.BaseDirectory, "user");
        var userB = Path.Combine(pathsB.BaseDirectory, "user");
        var saveA = CreateDirectory(Nintendo3ds(userA, Id0A, Id1A), "title", "00040000", "00112200", "data");
        await File.WriteAllTextAsync(Path.Combine(saveA, "game.sav"), "progress");
        // Machine B has run Azahar once (its own console exists) but never played this game.
        Directory.CreateDirectory(Nintendo3ds(userB, Id0B, Id1B));

        var providerA = new AzaharSaveLocationProvider("/install-a", userDirectoryOverride: userA);
        var providerB = new AzaharSaveLocationProvider("/install-b", userDirectoryOverride: userB);
        var remote = new InMemoryCloudSyncTransport();
        var serviceA = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerA, pathsA), remote, new JsonSaveSyncManifestStore(pathsA));
        var serviceB = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerB, pathsB), remote, new JsonSaveSyncManifestStore(pathsB));

        Assert.Equal(1, (await serviceA.SyncAsync(providerA)).Uploaded);
        Assert.Equal(1, (await serviceB.SyncAsync(providerB)).Downloaded);

        var restored = Path.Combine(
            Nintendo3ds(userB, Id0B, Id1B), "title", "00040000", "00112200", "data", "game.sav");
        Assert.Equal("progress", await File.ReadAllTextAsync(restored));
    }

    [Fact]
    public void ResolveUnit_RejectsTraversalUnknownIdsAndMissingSdCard()
    {
        var userDirectory = Path.Combine(BaseDirectory, "user");
        CreateDirectory(Nintendo3ds(userDirectory, Id0A, Id1A), "title", "00040000", "00112200", "data");
        var provider = new AzaharSaveLocationProvider("/install", userDirectoryOverride: userDirectory);

        Assert.NotNull(provider.ResolveUnit("azahar/title/00040000/00112200"));
        Assert.NotNull(provider.ResolveUnit("azahar/extdata/00000042"));
        Assert.True(((ISaveLocationProvider)provider).OwnsUnit("azahar/title/00040000/00112200"));
        Assert.False(((ISaveLocationProvider)provider).OwnsUnit("azahar/states/whatever"));
        Assert.Null(provider.ResolveUnit("azahar/title/../../secret"));
        Assert.Null(provider.ResolveUnit("azahar/title/00040000")); // missing low id
        Assert.Null(provider.ResolveUnit("pcsx2/Mcd001.ps2"));

        // A machine whose SD card does not exist yet cannot materialize a remote unit.
        var withoutSdCard = new AzaharSaveLocationProvider(
            "/install", userDirectoryOverride: Path.Combine(BaseDirectory, "empty-user"));
        Assert.Null(withoutSdCard.ResolveUnit("azahar/title/00040000/00112200"));
    }

    private static string Nintendo3ds(string userDirectory, string id0, string id1) =>
        Path.Combine(userDirectory, "sdmc", "Nintendo 3DS", id0, id1);

    private static string CreateDirectory(params string[] segments)
    {
        var path = Path.Combine(segments);
        Directory.CreateDirectory(path);
        return path;
    }
}
