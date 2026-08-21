using System.Text;
using EmuShelf.Core.SaveSync;
using Xunit;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>
/// Covers the one-time copy-only re-key of cloud battery saves from the old emulator-scoped namespace
/// to the new system-scoped one. See DECISIONS 2026-08-21.
/// </summary>
public sealed class BatterySaveNamespaceMigrationTests
{
    private static readonly DateTimeOffset Modified = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Cancellation = CancellationToken.None;

    [Theory]
    [InlineData("duckstation/per-game/serial/SLUS-00001_1.mcd", "playstation/per-game/serial/SLUS-00001_1.mcd")]
    [InlineData("duckstation/shared/card1", "playstation/shared/card1")]
    [InlineData("pcsx2/Mcd001.ps2", "playstation2/Mcd001.ps2")]
    [InlineData("rpcs3/savedata/BLES00000", "playstation3/savedata/BLES00000")]
    [InlineData("ppsspp/ULUS10000", "psp/ULUS10000")]
    [InlineData("azahar/title/0004000000000000", "3ds/title/0004000000000000")]
    [InlineData("dolphin/gc/raw/a/USA", "gamecube/raw/a/USA")]
    [InlineData("dolphin/gc/gci/a/GALE01", "gamecube/gci/a/GALE01")]
    [InlineData("dolphin/wii/title/00010000/52534d45", "wii/title/00010000/52534d45")]
    [InlineData("retroarch/snes/Chrono Trigger.srm", "snes/Chrono Trigger.srm")]
    [InlineData("retroarch/nds/Pokemon.srm", "nds/Pokemon.srm")]
    [InlineData("retroarch/playstation/Final Fantasy.srm", "playstation/Final Fantasy.srm")]
    public void MapToSystemKey_RemapsBatteryPrefixes(string oldId, string expected) =>
        Assert.Equal(expected, BatterySaveNamespaceMigration.MapToSystemKey(oldId));

    [Theory]
    // Save states keep their emulator-scoped key (two emulators for one system must stay apart).
    [InlineData("duckstation/states/game.sav")]
    [InlineData("retroarch/nds/states/game.state")]
    [InlineData("dolphin/gc/states/game.s01")]
    // Legacy cheats/patches are no longer synced and are left untouched.
    [InlineData("pcsx2/cheats/whatever")]
    [InlineData("pcsx2/patches/whatever")]
    // Already system-keyed ids (new format) are not touched.
    [InlineData("playstation/per-game/serial/SLUS-00001_1.mcd")]
    [InlineData("snes/Chrono Trigger.srm")]
    // An unknown prefix is left alone.
    [InlineData("mystery/foo")]
    public void MapToSystemKey_LeavesNonBatteryAndAlreadyMigratedUntouched(string unitId) =>
        Assert.Null(BatterySaveNamespaceMigration.MapToSystemKey(unitId));

    [Fact]
    public async Task RunAsync_CopiesBatteryEntriesToSystemKeyAndLeavesOriginals()
    {
        var transport = new InMemoryCloudSyncTransport();
        transport.Seed("duckstation/per-game/serial/SLUS-00001_1.mcd", Bytes("ps1-card"), Modified);
        transport.Seed("pcsx2/Mcd001.ps2", Bytes("ps2-card"), Modified);
        // A state entry must NOT be migrated.
        transport.Seed("duckstation/states/game.sav", Bytes("state"), Modified, "st1|duckstation|x64|unk:");

        var copied = await new BatterySaveNamespaceMigration(transport).RunAsync(Cancellation);

        Assert.Equal(2, copied);
        // New system keys created, carrying the same payload.
        Assert.True(transport.Has("playstation/per-game/serial/SLUS-00001_1.mcd"));
        Assert.Equal(Bytes("ps1-card"), transport.Content("playstation/per-game/serial/SLUS-00001_1.mcd"));
        Assert.True(transport.Has("playstation2/Mcd001.ps2"));
        // Originals left frozen in the cloud (never deleted).
        Assert.True(transport.Has("duckstation/per-game/serial/SLUS-00001_1.mcd"));
        Assert.True(transport.Has("pcsx2/Mcd001.ps2"));
        // The state entry is untouched and got no system-keyed twin.
        Assert.True(transport.Has("duckstation/states/game.sav"));
        Assert.False(transport.Has("playstation/states/game.sav"));
    }

    [Fact]
    public async Task RunAsync_IsIdempotent_SecondRunCopiesNothing()
    {
        var transport = new InMemoryCloudSyncTransport();
        transport.Seed("pcsx2/Mcd001.ps2", Bytes("ps2-card"), Modified);

        Assert.Equal(1, await new BatterySaveNamespaceMigration(transport).RunAsync(Cancellation));
        var uploadsAfterFirst = transport.Uploads;

        Assert.Equal(0, await new BatterySaveNamespaceMigration(transport).RunAsync(Cancellation));
        Assert.Equal(uploadsAfterFirst, transport.Uploads);
    }

    [Fact]
    public async Task RunAsync_DoesNotOverwriteAnExistingSystemKey()
    {
        var transport = new InMemoryCloudSyncTransport();
        transport.Seed("pcsx2/Mcd001.ps2", Bytes("old-emulator-copy"), Modified);
        // The new key already holds a (newer) payload — the migration must not clobber it.
        transport.Seed("playstation2/Mcd001.ps2", Bytes("already-system-keyed"), Modified);

        var copied = await new BatterySaveNamespaceMigration(transport).RunAsync(Cancellation);

        Assert.Equal(0, copied);
        Assert.Equal(Bytes("already-system-keyed"), transport.Content("playstation2/Mcd001.ps2"));
    }

    [Fact]
    public async Task RunAsync_CarriesTheCompatibilityFieldOntoTheNewKey()
    {
        var transport = new InMemoryCloudSyncTransport();
        transport.Seed("pcsx2/Mcd001.ps2", Bytes("ps2-card"), Modified, "card");

        await new BatterySaveNamespaceMigration(transport).RunAsync(Cancellation);

        Assert.Equal("card", transport.Compatibility("playstation2/Mcd001.ps2"));
    }

    [Fact]
    public void RekeyManifestBaselines_AddsSystemKeyedBaselines_AndLeavesStatesAndExistingKeys()
    {
        var manifest = new SaveSyncManifest(new[]
        {
            new SaveUnitBaseline("pcsx2/Mcd001.ps2", "hash-a", Modified, 3),
            new SaveUnitBaseline("duckstation/states/game.sav", "hash-s", Modified, 1, "st1|duckstation|x64|unk:"),
            // A new-key baseline that already exists must not be overwritten.
            new SaveUnitBaseline("playstation/shared/card1", "hash-existing", Modified, 5),
            new SaveUnitBaseline("duckstation/shared/card1", "hash-old", Modified, 2),
        });

        var rekeyed = BatterySaveNamespaceMigration.RekeyManifestBaselines(manifest);

        // Battery baseline gained a system-keyed twin carrying the same content hash and revision.
        var migrated = Assert.Single(rekeyed.Baselines, b => b.UnitId == "playstation2/Mcd001.ps2");
        Assert.Equal("hash-a", migrated.ContentHash);
        Assert.Equal(3, migrated.Revision);
        // The old baseline is left in place (inert).
        Assert.NotNull(rekeyed.Get("pcsx2/Mcd001.ps2"));
        // The state baseline is untouched and got no twin.
        Assert.Null(rekeyed.Get("playstation/states/game.sav"));
        Assert.NotNull(rekeyed.Get("duckstation/states/game.sav"));
        // The pre-existing new-key baseline is preserved, not clobbered by the old duckstation twin.
        Assert.Equal("hash-existing", rekeyed.Get("playstation/shared/card1")!.ContentHash);
    }

    [Fact]
    public void RekeyManifestBaselines_ReturnsSameInstanceWhenNothingToMigrate()
    {
        var manifest = new SaveSyncManifest(new[]
        {
            new SaveUnitBaseline("playstation2/Mcd001.ps2", "hash", Modified, 1),
        });

        Assert.Same(manifest, BatterySaveNamespaceMigration.RekeyManifestBaselines(manifest));
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
