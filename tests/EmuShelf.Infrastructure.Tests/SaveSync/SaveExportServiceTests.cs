using System.IO.Compression;
using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class SaveExportServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryLocalSaveEndpoint _local = new();
    private readonly InMemoryCloudSyncTransport _remote = new();
    private readonly CapturingSink _sink = new();

    private static SaveExportService Service() => new(() => T0);

    [Fact]
    public async Task Device_ExportsFileAndFolderUnits_WithReadmeAndManifest()
    {
        var card = new SaveUnit("pcsx2/Mcd001.ps2", "Memory Card 1", SaveUnitKind.File);
        var folder = new SaveUnit("pcsx2/folder/SLUS-20552", "God of War", SaveUnitKind.Folder);
        _local.Seed(card.UnitId, Bytes("card-bytes"), T0);
        _local.Seed(folder.UnitId, Zip(("save.bin", Bytes("AAA")), ("sub/icon.ico", Bytes("BBBB"))), T0);
        var provider = new FakeSaveLocationProvider("playstation2", card, folder);

        var result = await Service().ExportAsync(
            [new SaveExportTarget(provider, _local, "PlayStation 2")], cloud: null, _sink);

        Assert.Equal(SaveExportStatus.Completed, result.Status);
        Assert.Equal(2, result.SavesExported);
        Assert.Equal(0, result.FromCloud);
        Assert.Equal(Bytes("card-bytes").Length + 3 + 4, result.TotalBytes);

        Assert.Equal(Bytes("card-bytes"), _sink.Entry("PlayStation 2/Mcd001.ps2"));
        Assert.Equal(Bytes("AAA"), _sink.Entry("PlayStation 2/folder/SLUS-20552/save.bin"));
        Assert.Equal(Bytes("BBBB"), _sink.Entry("PlayStation 2/folder/SLUS-20552/sub/icon.ico"));
        Assert.True(_sink.Has("EXPORT-README.txt"));
        Assert.True(_sink.Has("manifest.json"));
    }

    [Fact]
    public async Task DeviceAndCloud_AddsCloudOnlyUnit_AndPrefersDeviceOnConflict()
    {
        var card = new SaveUnit("pcsx2/Mcd001.ps2", "Memory Card 1", SaveUnitKind.File);
        _local.Seed(card.UnitId, Bytes("local-copy"), T0);
        var provider = new FakeSaveLocationProvider("playstation2", card);
        provider.ResolvableUnitKinds["pcsx2/Mcd002.ps2"] = SaveUnitKind.File;

        // The remote holds a different version of the device's card (a conflict) and one card the
        // device does not have.
        _remote.Seed(card.UnitId, Bytes("CLOUD-copy"), T0);
        _remote.Seed("pcsx2/Mcd002.ps2", Bytes("cloud-only"), T0);

        var result = await Service().ExportAsync(
            [new SaveExportTarget(provider, _local, "PlayStation 2")], _remote, _sink);

        Assert.Equal(SaveExportStatus.Completed, result.Status);
        Assert.Equal(2, result.SavesExported);
        Assert.Equal(1, result.FromCloud);
        // Device copy wins; the conflicting cloud copy is never downloaded.
        Assert.Equal(Bytes("local-copy"), _sink.Entry("PlayStation 2/Mcd001.ps2"));
        Assert.Equal(Bytes("cloud-only"), _sink.Entry("PlayStation 2/Mcd002.ps2"));
        Assert.Equal(1, _remote.Downloads);
    }

    [Fact]
    public async Task DeviceAndCloud_ExpandsCloudFolderPayload()
    {
        // One seeded local unit fixes the provider's "dolphin/" prefix so the cloud folder is owned.
        var provider = new FakeSaveLocationProvider("gamecube", new SaveUnit("dolphin/placeholder", "x", SaveUnitKind.File));
        _local.Seed("dolphin/placeholder", Bytes("p"), T0);
        provider.ResolvableUnitKinds["dolphin/cards/USA"] = SaveUnitKind.Folder;
        _remote.Seed("dolphin/cards/USA", Zip(("mem.raw", Bytes("RAW"))), T0);

        var result = await Service().ExportAsync(
            [new SaveExportTarget(provider, _local, "GameCube")], _remote, _sink);

        Assert.Equal(SaveExportStatus.Completed, result.Status);
        Assert.Equal(2, result.SavesExported);
        Assert.Equal(1, result.FromCloud);
        // A cloud folder payload (a zip) is expanded into individual files, not nested as a zip.
        Assert.Equal(Bytes("RAW"), _sink.Entry("GameCube/cards/USA/mem.raw"));
    }

    [Fact]
    public async Task CloudUnit_WithNoOwningPlatform_IsSkipped()
    {
        var provider = new FakeSaveLocationProvider("playstation2", new SaveUnit("pcsx2/Mcd001.ps2", "c", SaveUnitKind.File));
        _local.Seed("pcsx2/Mcd001.ps2", Bytes("card"), T0);
        _remote.Seed("otheremu/foreign.sav", Bytes("nope"), T0);

        var result = await Service().ExportAsync(
            [new SaveExportTarget(provider, _local, "PlayStation 2")], _remote, _sink);

        Assert.Equal(SaveExportStatus.Completed, result.Status);
        Assert.Equal(1, result.SavesExported);
        Assert.Equal(0, result.FromCloud);
        Assert.Contains(result.Skipped, note => note.Contains("otheremu/foreign.sav", StringComparison.Ordinal));
        Assert.False(_sink.Has("PlayStation 2/../otheremu/foreign.sav"));
    }

    [Fact]
    public async Task CloudCheatsAndPatches_UnderAKnownPlatform_AreIgnoredSilently()
    {
        // Older builds uploaded thousands of cheats/patches under a platform's prefix; a platform does
        // not own them, so they must be ignored quietly rather than flooding the skipped list.
        var provider = new FakeSaveLocationProvider("playstation2");
        _remote.Seed("pcsx2/cheats/SLUS-20552.pnach", Bytes("cheat"), T0);
        _remote.Seed("pcsx2/patches/SLUS-20552.pnach", Bytes("patch"), T0);

        var result = await Service().ExportAsync(
            [new SaveExportTarget(provider, _local, "PlayStation 2")], _remote, _sink);

        Assert.Equal(SaveExportStatus.NothingToExport, result.Status);
        Assert.Empty(result.Skipped);
        Assert.Equal(0, _remote.Downloads);
    }

    [Fact]
    public async Task CloudUnit_OwnedButUnresolvable_IsSkippedWithoutDownloading()
    {
        // No local units (the zero-unit provider still resolves the "pcsx2/" prefix), so pcsx2/Mcd009
        // is owned by the prefix but not resolvable here.
        var provider = new FakeSaveLocationProvider("playstation2");
        _remote.Seed("pcsx2/Mcd009.ps2", Bytes("orphan"), T0);

        var result = await Service().ExportAsync(
            [new SaveExportTarget(provider, _local, "PlayStation 2")], _remote, _sink);

        Assert.Equal(SaveExportStatus.NothingToExport, result.Status);
        Assert.Single(result.Skipped);
        Assert.Equal(0, _remote.Downloads);
    }

    [Fact]
    public async Task NothingPresent_ReturnsNothingToExport_AndWritesNoArchiveEntries()
    {
        var provider = new FakeSaveLocationProvider("playstation2");

        var result = await Service().ExportAsync(
            [new SaveExportTarget(provider, _local, "PlayStation 2")], cloud: null, _sink);

        Assert.Equal(SaveExportStatus.NothingToExport, result.Status);
        Assert.False(_sink.Has("manifest.json"));
        Assert.Empty(_sink.Entries);
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private sealed class CapturingSink : ISaveExportSink
    {
        public Dictionary<string, byte[]> Entries { get; } = new(StringComparer.Ordinal);

        public bool Has(string entryPath) => Entries.ContainsKey(entryPath);

        public byte[] Entry(string entryPath) => Entries[entryPath];

        public async Task AddFileAsync(string entryPath, Stream content, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Entries[entryPath] = buffer.ToArray();
        }
    }
}
