using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class PpssppSaveSyncTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task WindowsPortable_UsesMemstickBesideExecutable_WhenInstalledFileIsAbsent()
    {
        var installation = Path.Combine(BaseDirectory, "PPSSPP");
        Directory.CreateDirectory(installation);
        var provider = CreateWindowsProvider(installation);

        Assert.Equal(
            Path.Combine(installation, "memstick"),
            await provider.GetMemoryStickDirectoryAsync());
    }

    [Fact]
    public async Task WindowsInstalled_UsesDocuments_WhenInstalledFileIsEmpty()
    {
        var installation = Path.Combine(BaseDirectory, "PPSSPP");
        var documents = Path.Combine(BaseDirectory, "Documents");
        Directory.CreateDirectory(installation);
        await File.WriteAllTextAsync(Path.Combine(installation, "installed.txt"), "");
        var provider = CreateWindowsProvider(installation, documentsDirectory: documents);

        Assert.Equal(
            Path.Combine(documents, "PPSSPP"),
            await provider.GetMemoryStickDirectoryAsync());
    }

    [Fact]
    public async Task WindowsInstalled_UsesConfiguredPath_AndNormalizesSelectedPspDirectory()
    {
        var installation = Path.Combine(BaseDirectory, "PPSSPP");
        var selectedPsp = Path.Combine(BaseDirectory, "Portable saves", "PSP");
        Directory.CreateDirectory(installation);
        await File.WriteAllTextAsync(Path.Combine(installation, "installed.txt"), selectedPsp);
        var provider = CreateWindowsProvider(installation);

        Assert.Equal(
            Path.GetDirectoryName(selectedPsp),
            await provider.GetMemoryStickDirectoryAsync());
    }

    [Fact]
    public void Defaults_UseConfigDirectoryOrFlatpakSandboxOnUnix()
    {
        var home = Path.Combine(BaseDirectory, "home");

        Assert.Equal(
            Path.Combine(home, ".config", "ppsspp"),
            PpssppSaveLocationProvider.GetDefaultMemoryStickDirectory("/app", home, "/docs", false, false));
        Assert.Equal(
            Path.Combine(home, ".var", "app", "org.ppsspp.PPSSPP", "config", "ppsspp"),
            PpssppSaveLocationProvider.GetDefaultMemoryStickDirectory("/app", home, "/docs", false, true));

        // macOS keeps the Memory Stick under Application Support, not ~/.config — the same root the
        // texture resolver already reads this installation's ppsspp.ini from.
        Assert.Equal(
            Path.Combine(home, "Library", "Application Support", "PPSSPP"),
            PpssppSaveLocationProvider.GetDefaultMemoryStickDirectory(
                "/app", home, "/docs", isWindows: false, isFlatpak: false, isMacOS: true));
    }

    [Fact]
    public async Task MacOs_ResolvesSavesUnderApplicationSupport()
    {
        var home = Path.Combine(BaseDirectory, "mac-home");
        var saveData = Path.Combine(home, "Library", "Application Support", "PPSSPP", "PSP", "SAVEDATA");
        Directory.CreateDirectory(Path.Combine(saveData, "ULUS10041DATA00"));
        var provider = new PpssppSaveLocationProvider(
            Path.Combine(BaseDirectory, "install"),
            homeDirectory: home,
            isWindows: false,
            isMacOS: true);

        Assert.Equal(saveData, await provider.GetSaveDataDirectoryAsync());
        Assert.Single(await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task Provider_EnumeratesOnlyImmediateSavedataFolders()
    {
        var memoryStick = Path.Combine(BaseDirectory, "memstick");
        var saveData = Path.Combine(memoryStick, "PSP", "SAVEDATA");
        Directory.CreateDirectory(Path.Combine(saveData, "ULUS10041DATA00"));
        Directory.CreateDirectory(Path.Combine(saveData, "NPJH50676"));
        Directory.CreateDirectory(Path.Combine(memoryStick, "PSP", "PPSSPP_STATE"));
        await File.WriteAllTextAsync(Path.Combine(saveData, "not-a-save.bin"), "ignored");
        var provider = new PpssppSaveLocationProvider(
            Path.Combine(BaseDirectory, "install"),
            memoryStickDirectoryOverride: memoryStick);

        Assert.Equal(
            [
                new SaveUnit("ppsspp/NPJH50676", "NPJH50676", SaveUnitKind.Folder),
                new SaveUnit("ppsspp/ULUS10041DATA00", "ULUS10041DATA00", SaveUnitKind.Folder),
            ],
            await provider.GetSaveUnitsAsync());
        Assert.Null(provider.ResolveUnit("ppsspp/../PPSSPP_STATE"));
        Assert.Null(provider.ResolveUnit("ppsspp/."));
        Assert.Null(provider.ResolveUnit("pcsx2/Mcd001.ps2"));
    }

    [Fact]
    public async Task FolderSave_RoundTripsToAnEmptySecondMemoryStick()
    {
        var pathsA = new AppPaths(Path.Combine(BaseDirectory, "machine-a"));
        var pathsB = new AppPaths(Path.Combine(BaseDirectory, "machine-b"));
        pathsA.EnsureDirectoriesExist();
        pathsB.EnsureDirectoriesExist();
        var stickA = Path.Combine(pathsA.BaseDirectory, "memstick");
        var stickB = Path.Combine(pathsB.BaseDirectory, "memstick");
        var saveA = Path.Combine(stickA, "PSP", "SAVEDATA", "ULUS10041DATA00");
        Directory.CreateDirectory(saveA);
        await File.WriteAllTextAsync(Path.Combine(saveA, "PARAM.SFO"), "metadata");
        await File.WriteAllTextAsync(Path.Combine(saveA, "DATA.BIN"), "progress");

        var providerA = new PpssppSaveLocationProvider(pathsA.BaseDirectory, stickA);
        var providerB = new PpssppSaveLocationProvider(pathsB.BaseDirectory, stickB);
        var remote = new InMemoryCloudSyncTransport();
        var serviceA = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerA, pathsA),
            remote,
            new JsonSaveSyncManifestStore(pathsA));
        var serviceB = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerB, pathsB),
            remote,
            new JsonSaveSyncManifestStore(pathsB));

        Assert.Equal(1, (await serviceA.SyncAsync(providerA)).Uploaded);
        Assert.Equal(1, (await serviceB.SyncAsync(providerB)).Downloaded);

        var restored = Path.Combine(stickB, "PSP", "SAVEDATA", "ULUS10041DATA00");
        Assert.Equal("metadata", await File.ReadAllTextAsync(Path.Combine(restored, "PARAM.SFO")));
        Assert.Equal("progress", await File.ReadAllTextAsync(Path.Combine(restored, "DATA.BIN")));
    }

    [Fact]
    public async Task FolderSave_ForceUploadAndDownload_AreScopedAndUsable()
    {
        var pathsA = new AppPaths(Path.Combine(BaseDirectory, "force-a"));
        var pathsB = new AppPaths(Path.Combine(BaseDirectory, "force-b"));
        pathsA.EnsureDirectoriesExist();
        pathsB.EnsureDirectoriesExist();
        var stickA = Path.Combine(pathsA.BaseDirectory, "memstick");
        var stickB = Path.Combine(pathsB.BaseDirectory, "memstick");
        var saveA = Path.Combine(stickA, "PSP", "SAVEDATA", "ULUS10041DATA00");
        Directory.CreateDirectory(saveA);
        await File.WriteAllTextAsync(Path.Combine(saveA, "DATA.BIN"), "forced progress");

        var providerA = new PpssppSaveLocationProvider(pathsA.BaseDirectory, stickA);
        var providerB = new PpssppSaveLocationProvider(pathsB.BaseDirectory, stickB);
        var remote = new InMemoryCloudSyncTransport();
        var serviceA = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerA, pathsA),
            remote,
            new JsonSaveSyncManifestStore(pathsA));
        var serviceB = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerB, pathsB),
            remote,
            new JsonSaveSyncManifestStore(pathsB));

        Assert.Equal(1, (await serviceA.ForceAsync(providerA, SaveSyncDirection.Upload)).Uploaded);
        Assert.Equal(1, (await serviceB.ForceAsync(providerB, SaveSyncDirection.Download)).Downloaded);

        Assert.Equal(
            "forced progress",
            await File.ReadAllTextAsync(Path.Combine(
                stickB,
                "PSP",
                "SAVEDATA",
                "ULUS10041DATA00",
                "DATA.BIN")));
    }

    private PpssppSaveLocationProvider CreateWindowsProvider(
        string installation,
        string? documentsDirectory = null) =>
        new(
            installation,
            homeDirectory: Path.Combine(BaseDirectory, "home"),
            documentsDirectory: documentsDirectory ?? Path.Combine(BaseDirectory, "Documents"),
            isWindows: true);
}
