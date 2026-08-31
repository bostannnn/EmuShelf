using System.Text;
using EmuShelf.Core.SaveSync;
using Xunit;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>
/// Covers the one-time copy-only re-key of cloud Nintendo DS battery saves from the file-name key to
/// the cross-emulator per-game key that standalone melonDS and RetroArch DS cores now share. See
/// DECISIONS 2026-09-01.
/// </summary>
public sealed class NintendoDsBatteryKeyMigrationTests
{
    private static readonly DateTimeOffset Modified = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Cancellation = CancellationToken.None;

    [Theory]
    [InlineData("nds/Pokemon Platinum (USA).srm", "nds/battery/Pokemon Platinum (USA)")]
    [InlineData("nds/Pokemon Platinum (USA).sav", "nds/battery/Pokemon Platinum (USA)")]
    public void MapLegacyUnitId_RemapsRawBatteryFileNames(string oldId, string expected) =>
        Assert.Equal(expected, NintendoDsBatterySaveKey.MapLegacyUnitId(oldId));

    [Theory]
    // A DeSmuME .dsv is not a raw dump, so it is not interchangeable and keeps its own key.
    [InlineData("nds/Contra 4 (USA).dsv")]
    // Already canonical.
    [InlineData("nds/battery/Contra 4 (USA)")]
    // Save states stay emulator-scoped.
    [InlineData("retroarch/nds/states/Contra 4 (USA).state1")]
    [InlineData("melonds/nds/states/Contra 4 (USA).ml0")]
    // Another system's save that happens to be a .srm.
    [InlineData("snes/Chrono Trigger (USA).srm")]
    // Nothing that would escape the save folder, or hide itself, becomes a key.
    [InlineData("nds/../outside.srm")]
    [InlineData("nds/.hidden.srm")]
    [InlineData("nds/sub/dir.srm")]
    public void MapLegacyUnitId_LeavesEverythingElseAlone(string unitId) =>
        Assert.Null(NintendoDsBatterySaveKey.MapLegacyUnitId(unitId));

    [Fact]
    public async Task RunAsync_CopiesDsBatteryEntriesToTheSharedKeyAndLeavesOriginals()
    {
        var transport = new InMemoryCloudSyncTransport();
        transport.Seed("nds/Pokemon Platinum (USA).srm", Bytes("ds-save"), Modified);
        transport.Seed("nds/Contra 4 (USA).dsv", Bytes("desmume-save"), Modified);
        transport.Seed("retroarch/nds/states/Contra 4 (USA).state1", Bytes("state"), Modified, "st1|x");

        var copied = await new NintendoDsBatteryKeyMigration(transport).RunAsync(Cancellation);

        Assert.Equal(1, copied);
        Assert.Equal(Bytes("ds-save"), transport.Content("nds/battery/Pokemon Platinum (USA)"));
        // Originals left frozen in the cloud (the transport has no delete, by design).
        Assert.True(transport.Has("nds/Pokemon Platinum (USA).srm"));
        // A .dsv and a save state get no twin.
        Assert.False(transport.Has("nds/battery/Contra 4 (USA)"));
        Assert.True(transport.Has("retroarch/nds/states/Contra 4 (USA).state1"));
    }

    [Fact]
    public async Task RunAsync_KeepsTheNewestWhenOneGameHasBothExtensions()
    {
        // The two machines that produced .sav and .srm copies of one game map onto a single key, so
        // the entry the user played last must be the one that becomes canonical.
        var transport = new InMemoryCloudSyncTransport();
        transport.Seed("nds/Tetris DS (USA).sav", Bytes("older"), Modified);
        transport.Seed("nds/Tetris DS (USA).srm", Bytes("newer"), Modified.AddDays(1));

        var copied = await new NintendoDsBatteryKeyMigration(transport).RunAsync(Cancellation);

        Assert.Equal(1, copied);
        Assert.Equal(Bytes("newer"), transport.Content("nds/battery/Tetris DS (USA)"));
    }

    [Fact]
    public async Task RunAsync_IsIdempotentAndNeverOverwritesAnExistingKey()
    {
        var transport = new InMemoryCloudSyncTransport();
        transport.Seed("nds/Tetris DS (USA).srm", Bytes("old-copy"), Modified);
        transport.Seed("nds/battery/Tetris DS (USA)", Bytes("already-shared"), Modified);
        transport.Seed("nds/Contra 4 (USA).srm", Bytes("ds-save"), Modified);

        Assert.Equal(1, await new NintendoDsBatteryKeyMigration(transport).RunAsync(Cancellation));
        Assert.Equal(Bytes("already-shared"), transport.Content("nds/battery/Tetris DS (USA)"));

        var uploadsAfterFirst = transport.Uploads;
        Assert.Equal(0, await new NintendoDsBatteryKeyMigration(transport).RunAsync(Cancellation));
        Assert.Equal(uploadsAfterFirst, transport.Uploads);
    }

    [Fact]
    public void RekeyManifestBaselines_AddsSharedKeyedBaselinesWithoutClobbering()
    {
        var manifest = new SaveSyncManifest(new[]
        {
            new SaveUnitBaseline("nds/Pokemon Platinum (USA).srm", "hash-a", Modified, 3),
            new SaveUnitBaseline("nds/Contra 4 (USA).dsv", "hash-d", Modified, 1),
            new SaveUnitBaseline("nds/battery/Tetris DS (USA)", "hash-existing", Modified, 5),
            new SaveUnitBaseline("nds/Tetris DS (USA).srm", "hash-old", Modified, 2),
        });

        var rekeyed = NintendoDsBatteryKeyMigration.RekeyManifestBaselines(manifest);

        var migrated = Assert.Single(
            rekeyed.Baselines, baseline => baseline.UnitId == "nds/battery/Pokemon Platinum (USA)");
        Assert.Equal("hash-a", migrated.ContentHash);
        Assert.Equal(3, migrated.Revision);
        Assert.NotNull(rekeyed.Get("nds/Pokemon Platinum (USA).srm"));
        Assert.Null(rekeyed.Get("nds/battery/Contra 4 (USA)"));
        Assert.Equal("hash-existing", rekeyed.Get("nds/battery/Tetris DS (USA)")!.ContentHash);
    }

    [Fact]
    public void RekeyManifestBaselines_ReturnsSameInstanceWhenNothingToMigrate()
    {
        var manifest = new SaveSyncManifest(new[]
        {
            new SaveUnitBaseline("nds/battery/Tetris DS (USA)", "hash", Modified, 1),
        });

        Assert.Same(manifest, NintendoDsBatteryKeyMigration.RekeyManifestBaselines(manifest));
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
