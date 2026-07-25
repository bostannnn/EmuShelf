using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.Pcsx2;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class FileSystemSaveSyncTests : TempAppDirectoryTestBase
{
    private readonly string _configurationDirectory;
    private readonly string _memoryCardsDirectory;
    private readonly Pcsx2SaveLocationProvider _endpointProvider;
    private readonly FileSystemLocalSaveEndpoint _endpoint;

    public FileSystemSaveSyncTests()
    {
        _configurationDirectory = Path.Combine(BaseDirectory, "pcsx2-config");
        _memoryCardsDirectory = Path.Combine(_configurationDirectory, "relocated-cards");
        WriteIni(autoManageFolderCards: false);
        _endpointProvider = new Pcsx2SaveLocationProvider(_configurationDirectory);
        _endpoint = new FileSystemLocalSaveEndpoint(_endpointProvider, AppPaths);
    }

    [Fact]
    public async Task Provider_EnumeratesFileCardsFromConfiguredDirectory()
    {
        Directory.CreateDirectory(_memoryCardsDirectory);
        await File.WriteAllTextAsync(Path.Combine(_memoryCardsDirectory, "Mcd001.ps2"), "file-card");
        await File.WriteAllTextAsync(Path.Combine(_memoryCardsDirectory, "ignore.ps2"), "not-a-card");
        WriteIni(autoManageFolderCards: false);

        var provider = new Pcsx2SaveLocationProvider(_configurationDirectory);
        var units = await provider.GetSaveUnitsAsync();

        var unit = Assert.Single(units);
        Assert.Equal(new SaveUnit("pcsx2/Mcd001.ps2", "Mcd001.ps2", SaveUnitKind.File), unit);
        Assert.Equal(_memoryCardsDirectory, await provider.GetMemoryCardsDirectoryAsync());
    }

    [Fact]
    public async Task Provider_KeepsSupportedCustomCardWhenAnotherEnabledSlotUsesIt()
    {
        Directory.CreateDirectory(_memoryCardsDirectory);
        await File.WriteAllTextAsync(Path.Combine(_memoryCardsDirectory, "Custom Card.ps2"), "file-card");
        Directory.CreateDirectory(_configurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_configurationDirectory, "PCSX2.ini"),
            "[UI]\nSettingsVersion = 1\n[Folders]\nMemoryCards = relocated-cards\n[EmuCore]\n" +
            "McdFolderAutoManage = false\n[MemoryCards]\nSlot1_Enable = true\nSlot1_Filename = Custom Card.ps2\n");

        var unit = Assert.Single(await new Pcsx2SaveLocationProvider(_configurationDirectory).GetSaveUnitsAsync());

        Assert.Equal("pcsx2/Custom Card.ps2", unit.UnitId);
    }

    [Fact]
    public async Task Provider_EnumeratesOneFolderUnitPerGameSerial()
    {
        var cardDirectory = Path.Combine(_memoryCardsDirectory, "Mcd001");
        Directory.CreateDirectory(Path.Combine(cardDirectory, "SLUS-20552"));
        Directory.CreateDirectory(Path.Combine(cardDirectory, "SLES-12345"));
        await File.WriteAllTextAsync(Path.Combine(cardDirectory, "_pcsx2_index"), "volatile metadata");
        WriteIni(autoManageFolderCards: true);

        var units = await new Pcsx2SaveLocationProvider(_configurationDirectory).GetSaveUnitsAsync();

        Assert.Equal(
            [
                new SaveUnit("pcsx2/Mcd001/SLES-12345", "Mcd001 — SLES-12345", SaveUnitKind.Folder),
                new SaveUnit("pcsx2/Mcd001/SLUS-20552", "Mcd001 — SLUS-20552", SaveUnitKind.Folder),
            ],
            units);
    }

    [Fact]
    public async Task Provider_FailsClosedForReadableUnsupportedIni()
    {
        Directory.CreateDirectory(_configurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_configurationDirectory, "PCSX2.ini"),
            "[UI]\nSettingsVersion = 99\n[Folders]\nMemoryCards = memcards\n[EmuCore]\nMcdFolderAutoManage = false\n");

        await Assert.ThrowsAsync<Pcsx2ConfigurationFormatException>(() =>
            new Pcsx2SaveLocationProvider(_configurationDirectory).GetSaveUnitsAsync());
    }

    [Fact]
    public async Task Endpoint_RejectsAProviderUnitThatResolvesToItsRoot()
    {
        var root = Path.Combine(BaseDirectory, "save-root");
        Directory.CreateDirectory(root);
        var endpoint = new FileSystemLocalSaveEndpoint(new RootResolvingProvider(root), AppPaths);

        await Assert.ThrowsAsync<ArgumentException>(() => endpoint.SnapshotAsync("unsafe/."));
    }

    [Fact]
    public void Provider_RejectsRemoteFolderUnit_WhenTheActiveCardIsAFile()
    {
        Directory.CreateDirectory(_memoryCardsDirectory);
        File.WriteAllText(Path.Combine(_memoryCardsDirectory, "Mcd001.ps2"), "file-card");
        WriteIni(autoManageFolderCards: false);

        var provider = new Pcsx2SaveLocationProvider(_configurationDirectory);

        Assert.NotNull(provider.ResolveUnit("pcsx2/Mcd001.ps2"));
        Assert.Null(provider.ResolveUnit("pcsx2/Mcd001.ps2/SLUS-20552"));
    }

    [Fact]
    public void DefaultLocations_UseDocumentedWindowsAndFlatpakPaths()
    {
        var windows = Pcsx2SaveLocationProvider.GetDefaultMemoryCardsDirectory("ignored", isWindows: true);
        var flatpak = Pcsx2SaveLocationProvider.GetDefaultMemoryCardsDirectory("/home/deck", isWindows: false);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCSX2", "memcards"),
            windows);
        Assert.Equal(
            Path.Combine("/home/deck", ".var", "app", "net.pcsx2.PCSX2", "config", "PCSX2", "memcards"),
            flatpak);
    }

    [Fact]
    public async Task FolderSnapshot_IgnoresVolatileIndex_AndIsStableAcrossEnumerationOrder()
    {
        var saveDirectory = Path.Combine(_memoryCardsDirectory, "Mcd001", "SLUS-20552");
        Directory.CreateDirectory(Path.Combine(saveDirectory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(saveDirectory, "z.dat"), "z");
        await File.WriteAllTextAsync(Path.Combine(saveDirectory, "nested", "a.dat"), "a");
        var index = Path.Combine(_memoryCardsDirectory, "Mcd001", "_pcsx2_index");
        await File.WriteAllTextAsync(index, "first ordering");

        var first = await _endpoint.SnapshotAsync("pcsx2/Mcd001/SLUS-20552");
        await File.WriteAllTextAsync(index, "different timestamp/order only");
        File.SetLastWriteTimeUtc(index, DateTime.UtcNow.AddHours(1));
        var second = await _endpoint.SnapshotAsync("pcsx2/Mcd001/SLUS-20552");

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task BackupLocal_CopiesFileIntoPortableTimestampedConflictLayout()
    {
        Directory.CreateDirectory(_memoryCardsDirectory);
        var cardPath = Path.Combine(_memoryCardsDirectory, "Mcd001.ps2");
        await File.WriteAllTextAsync(cardPath, "before-overwrite");

        await _endpoint.BackupLocalAsync("pcsx2/Mcd001.ps2", "test conflict");

        var backupRoot = Path.Combine(AppPaths.SavesDirectory, "conflicts", "pcsx2", "Mcd001.ps2");
        var timestampDirectory = Assert.Single(Directory.EnumerateDirectories(backupRoot));
        Assert.Equal("before-overwrite", await File.ReadAllTextAsync(Path.Combine(timestampDirectory, "Mcd001.ps2")));
        Assert.Equal("test conflict", await File.ReadAllTextAsync(Path.Combine(timestampDirectory, "reason.txt")));
    }

    [Fact]
    public async Task SnapshotAndRead_DoNotChangeSourceBytesOrTimestamp()
    {
        Directory.CreateDirectory(_memoryCardsDirectory);
        var cardPath = Path.Combine(_memoryCardsDirectory, "Mcd001.ps2");
        var timestamp = new DateTime(2026, 7, 24, 12, 0, 0, DateTimeKind.Utc);
        await File.WriteAllBytesAsync(cardPath, [1, 2, 3, 4]);
        File.SetLastWriteTimeUtc(cardPath, timestamp);

        _ = await _endpoint.SnapshotAsync("pcsx2/Mcd001.ps2");
        await using var content = await _endpoint.ReadAsync("pcsx2/Mcd001.ps2");
        using var result = new MemoryStream();
        await content.CopyToAsync(result);

        Assert.Equal([1, 2, 3, 4], result.ToArray());
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(cardPath));
        Assert.Equal([1, 2, 3, 4], await File.ReadAllBytesAsync(cardPath));
    }

    [Fact]
    public async Task Service_UsesRealLocalEndpointToUploadAndDownload()
    {
        Directory.CreateDirectory(_memoryCardsDirectory);
        var localCard = Path.Combine(_memoryCardsDirectory, "Mcd001.ps2");
        await File.WriteAllTextAsync(localCard, "local save");
        var remote = new InMemoryCloudSyncTransport();
        var service = new SaveSyncService(_endpoint, remote, new JsonSaveSyncManifestStore(AppPaths));
        var unit = new SaveUnit("pcsx2/Mcd001.ps2", "Mcd001.ps2", SaveUnitKind.File);

        var upload = await service.SyncAsync(new FakeSaveLocationProvider("playstation2", [unit]));
        Assert.Equal(1, upload.Uploaded);
        Assert.Equal(Encoding.UTF8.GetBytes("local save"), remote.Content(unit.UnitId));

        File.Delete(localCard);
        var download = await service.SyncAsync(new FakeSaveLocationProvider("playstation2"));
        Assert.Equal(1, download.Downloaded);
        Assert.Equal("local save", await File.ReadAllTextAsync(localCard));
    }

    [Fact]
    public async Task FolderUnit_ReadThenWrite_RestoresSaveAndIndexWithoutChangingItsContentHash()
    {
        var source = Path.Combine(_memoryCardsDirectory, "Mcd001", "SLUS-20552");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        await File.WriteAllTextAsync(Path.Combine(source, "save.dat"), "save payload");
        await File.WriteAllTextAsync(Path.Combine(source, "nested", "icon.sys"), "icon payload");
        var cardRoot = Path.Combine(_memoryCardsDirectory, "Mcd001");
        await File.WriteAllTextAsync(Path.Combine(cardRoot, "_pcsx2_index"), "card index");
        var unitId = "pcsx2/Mcd001/SLUS-20552";
        var original = await _endpoint.SnapshotAsync(unitId);

        await using var payload = await _endpoint.ReadAsync(unitId);
        Directory.Delete(source, recursive: true);
        await _endpoint.WriteAsync(unitId, payload, original!.ModifiedUtc);

        var restored = await _endpoint.SnapshotAsync(unitId);
        Assert.Equal(original, restored);
        Assert.Equal("save payload", await File.ReadAllTextAsync(Path.Combine(source, "save.dat")));
        Assert.Equal("icon payload", await File.ReadAllTextAsync(Path.Combine(source, "nested", "icon.sys")));
    }

    [Fact]
    public async Task ManifestStore_RoundTripsBaselinesThroughPortableSavesDirectory()
    {
        var store = new JsonSaveSyncManifestStore(AppPaths);
        var baseline = new SaveUnitBaseline("pcsx2/Mcd001.ps2", "ABC", DateTimeOffset.UtcNow, 3);

        await store.SaveAsync(new SaveSyncManifest([baseline]));

        Assert.Equal(baseline, Assert.Single((await store.LoadAsync()).Baselines));
        Assert.True(File.Exists(Path.Combine(AppPaths.SavesDirectory, "sync-manifest.json")));
    }

    [Fact]
    public async Task Provider_RecognizesFolderCard_WhenSlotFilenameHasNoPs2Extension()
    {
        // A folder card's slot filename need not end in .ps2, and its type is decided from disk,
        // not from McdFolderAutoManage — so this is enumerated even with auto-manage off.
        var cardDirectory = Path.Combine(_memoryCardsDirectory, "Mcd001");
        Directory.CreateDirectory(Path.Combine(cardDirectory, "SLUS-20552"));
        await File.WriteAllTextAsync(Path.Combine(cardDirectory, "_pcsx2_index"), "index");
        Directory.CreateDirectory(_configurationDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_configurationDirectory, "PCSX2.ini"),
            "[UI]\nSettingsVersion = 1\n[Folders]\nMemoryCards = relocated-cards\n[EmuCore]\n" +
            "McdFolderAutoManage = false\n[MemoryCards]\nSlot1_Enable = true\nSlot1_Filename = Mcd001\n");

        var unit = Assert.Single(await new Pcsx2SaveLocationProvider(_configurationDirectory).GetSaveUnitsAsync());

        Assert.Equal(new SaveUnit("pcsx2/Mcd001/SLUS-20552", "Mcd001 — SLUS-20552", SaveUnitKind.Folder), unit);
    }

    private sealed class RootResolvingProvider(string root) : ISaveLocationProvider
    {
        public string SystemId => "unsafe";
        public string UnitIdPrefix => "unsafe/";

        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SaveUnit>>([]);

        public SaveUnitLocation? ResolveUnit(string unitId) =>
            new(root, root, SaveUnitKind.Folder);
    }

    [Fact]
    public async Task Provider_IgnoresDirectoryWithoutFolderCardIndex()
    {
        // A directory that is not a folder card (no _pcsx2_index marker) is never treated as one.
        Directory.CreateDirectory(Path.Combine(_memoryCardsDirectory, "Mcd001"));
        await File.WriteAllTextAsync(Path.Combine(_memoryCardsDirectory, "Mcd001", "stray.bin"), "x");
        WriteIni(autoManageFolderCards: true);

        Assert.Empty(await new Pcsx2SaveLocationProvider(_configurationDirectory).GetSaveUnitsAsync());
    }

    [Fact]
    public async Task Service_SyncsFolderCardBetweenTwoMachinesThroughSharedTransport()
    {
        // Two independent portable installs (their own Saves/manifest) sharing one cloud remote.
        var pathsA = new AppPaths(Path.Combine(BaseDirectory, "machineA"));
        var pathsB = new AppPaths(Path.Combine(BaseDirectory, "machineB"));
        pathsA.EnsureDirectoriesExist();
        pathsB.EnsureDirectoriesExist();
        var memcardsA = Path.Combine(pathsA.BaseDirectory, "memcards");
        var memcardsB = Path.Combine(pathsB.BaseDirectory, "memcards");

        var save = Path.Combine(memcardsA, "Mcd001", "SLUS-20552");
        Directory.CreateDirectory(Path.Combine(save, "nested"));
        await File.WriteAllTextAsync(Path.Combine(save, "save.bin"), "progress");
        await File.WriteAllTextAsync(Path.Combine(save, "nested", "icon.sys"), "icon");
        await File.WriteAllTextAsync(Path.Combine(memcardsA, "Mcd001", "_pcsx2_index"), "card index");

        var transport = new InMemoryCloudSyncTransport();
        var unit = new SaveUnit("pcsx2/Mcd001/SLUS-20552", "Mcd001 — SLUS-20552", SaveUnitKind.Folder);

        // Machine A uploads the per-game folder as a deterministic ZIP payload.
        var serviceA = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(CreateConfiguredProvider(pathsA.BaseDirectory, memcardsA), pathsA),
            transport,
            new JsonSaveSyncManifestStore(pathsA));
        Assert.Equal(1, (await serviceA.SyncAsync(new FakeSaveLocationProvider("playstation2", unit))).Uploaded);

        // Machine B (empty) pulls it down and reconstructs the folder byte-for-byte.
        var serviceB = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(CreateConfiguredProvider(pathsB.BaseDirectory, memcardsB), pathsB),
            transport,
            new JsonSaveSyncManifestStore(pathsB));
        var download = await serviceB.SyncAsync(new FakeSaveLocationProvider("playstation2"));

        Assert.Equal(1, download.Downloaded);
        Assert.Equal("progress", await File.ReadAllTextAsync(Path.Combine(memcardsB, "Mcd001", "SLUS-20552", "save.bin")));
        Assert.Equal("icon", await File.ReadAllTextAsync(Path.Combine(memcardsB, "Mcd001", "SLUS-20552", "nested", "icon.sys")));

        // The round trip is hash-stable, so re-syncing machine A now does nothing.
        Assert.Equal(1, (await serviceA.SyncAsync(new FakeSaveLocationProvider("playstation2", unit))).Unchanged);
    }

    [Fact]
    public async Task Provider_ReadsInisSubfolderAndFolderCardWithoutRootIndex()
    {
        // Mirror a real ES-DE PCSX2 layout: PCSX2.ini in an "inis" subfolder, memcards relocated
        // via a Windows-style relative path, a folder card that is a directory ("Mcdf01.ps2")
        // with per-save subfolders and no root _pcsx2_index, plus a shared file card.
        var pcsx2 = Path.Combine(BaseDirectory, "Emulators", "pcsx2-qt");
        var inis = Path.Combine(pcsx2, "inis");
        Directory.CreateDirectory(inis);
        var relocatedMemcards = Path.Combine(BaseDirectory, "saves", "ps2", "pcsx2", "memcards");
        var folderCard = Path.Combine(relocatedMemcards, "Mcdf01.ps2");
        Directory.CreateDirectory(Path.Combine(folderCard, "BASCUS-97399GodOfWar"));
        Directory.CreateDirectory(Path.Combine(folderCard, "BADATA-SYSTEM"));
        Directory.CreateDirectory(Path.Combine(folderCard, "_pcsx2_deleted_BASLUS-20504xyz"));
        await File.WriteAllTextAsync(Path.Combine(relocatedMemcards, "Mcd002.ps2"), "file-card");
        await File.WriteAllTextAsync(
            Path.Combine(inis, "PCSX2.ini"),
            "[UI]\nSettingsVersion = 1\n[Folders]\nMemoryCards = ..\\..\\saves\\ps2\\pcsx2\\memcards\n[EmuCore]\n" +
            "McdFolderAutoManage = true\n[MemoryCards]\nSlot1_Enable = true\nSlot1_Filename = Mcdf01.ps2\n" +
            "Slot2_Enable = true\nSlot2_Filename = Mcd002.ps2\n");

        var provider = new Pcsx2SaveLocationProvider(pcsx2);

        Assert.Equal(Path.GetFullPath(relocatedMemcards), await provider.GetMemoryCardsDirectoryAsync());
        Assert.Equal(
            [
                new SaveUnit("pcsx2/Mcd002.ps2", "Mcd002.ps2", SaveUnitKind.File),
                new SaveUnit("pcsx2/Mcdf01.ps2/BADATA-SYSTEM", "Mcdf01.ps2 — BADATA-SYSTEM", SaveUnitKind.Folder),
                new SaveUnit("pcsx2/Mcdf01.ps2/BASCUS-97399GodOfWar", "Mcdf01.ps2 — BASCUS-97399GodOfWar", SaveUnitKind.Folder),
            ],
            await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task Provider_AcceptsAMemoryCardsFolderSelectedDirectly()
    {
        // The user may point straight at their memcards folder (no PCSX2.ini). Accept it as-is
        // rather than falling back to the platform default.
        var memcards = Path.Combine(BaseDirectory, "ps2", "memcards");
        Directory.CreateDirectory(Path.Combine(memcards, "Mcdf01.ps2", "BASCUS-97399GodOfWar"));
        await File.WriteAllTextAsync(Path.Combine(memcards, "Mcd002.ps2"), "file-card");

        var provider = new Pcsx2SaveLocationProvider(memcards);

        Assert.Equal(Path.GetFullPath(memcards), await provider.GetMemoryCardsDirectoryAsync());
        Assert.Equal(
            [
                new SaveUnit("pcsx2/Mcd002.ps2", "Mcd002.ps2", SaveUnitKind.File),
                new SaveUnit("pcsx2/Mcdf01.ps2/BASCUS-97399GodOfWar", "Mcdf01.ps2 — BASCUS-97399GodOfWar", SaveUnitKind.Folder),
            ],
            await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task Provider_RealPcsx2Directory_ResolvesMemcardsAndUnits()
    {
        // Opt-in: set EMUSHELF_TEST_PCSX2_DIR to a real PCSX2 data directory to verify against it.
        var directory = Environment.GetEnvironmentVariable("EMUSHELF_TEST_PCSX2_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var provider = new Pcsx2SaveLocationProvider(directory);
        var memcards = await provider.GetMemoryCardsDirectoryAsync();
        var units = await provider.GetSaveUnitsAsync();

        Assert.True(Directory.Exists(memcards), $"Detected memcards folder does not exist: {memcards}");
        Assert.NotEmpty(units);
    }

    private void WriteIni(bool autoManageFolderCards)
    {
        Directory.CreateDirectory(_configurationDirectory);
        File.WriteAllText(
            Path.Combine(_configurationDirectory, "PCSX2.ini"),
            "[UI]\nSettingsVersion = 1\n[Folders]\nMemoryCards = relocated-cards\n[EmuCore]\n" +
            $"McdFolderAutoManage = {autoManageFolderCards.ToString().ToLowerInvariant()}\n" +
            "[MemoryCards]\nSlot1_Enable = true\nSlot1_Filename = Mcd001.ps2\n");
    }

    private static Pcsx2SaveLocationProvider CreateConfiguredProvider(string configurationDirectory, string memcards)
    {
        Directory.CreateDirectory(configurationDirectory);
        Directory.CreateDirectory(Path.Combine(memcards, "Mcd001"));
        File.WriteAllText(
            Path.Combine(configurationDirectory, "PCSX2.ini"),
            "[UI]\nSettingsVersion = 1\n[Folders]\n" +
            $"MemoryCards = {memcards}\n[MemoryCards]\nSlot1_Enable = true\nSlot1_Filename = Mcd001\n");
        return new Pcsx2SaveLocationProvider(configurationDirectory);
    }
}
