using EmuShelf.Core.SaveSync;
using EmuShelf.Integrations.Emulators.DuckStation;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class DuckStationAndroidSaveSyncTests : TempAppDirectoryTestBase
{
    private string CreateMemcards(params string[] cardFileNames)
    {
        var memcards = Path.Combine(BaseDirectory, "memcards");
        Directory.CreateDirectory(memcards);
        foreach (var name in cardFileNames)
            File.WriteAllText(Path.Combine(memcards, name), "card");
        return memcards;
    }

    [Fact]
    public async Task Enumerates_PerGameCards_WithDesktopCompatibleUnitIds()
    {
        // The exact names observed on the Thor plus a serial-named card. The title cards must match the
        // ids the desktop provider emitted for the identical files (playstation/per-game/title/<name>).
        var memcards = CreateMemcards(
            "Metal Gear Solid (USA)_1.mcd",
            "R4 - Ridge Racer Type 4 (USA)_1.mcd",
            "SLUS-00594_2.mcd");
        var provider = new DuckStationAndroidSaveLocationProvider(memcards);

        var ids = (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId).ToArray();

        Assert.Equal(
            [
                "playstation/per-game/serial/SLUS-00594_2.mcd",
                "playstation/per-game/title/Metal Gear Solid (USA)_1.mcd",
                "playstation/per-game/title/R4 - Ridge Racer Type 4 (USA)_1.mcd",
            ],
            ids.OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(await provider.GetSaveUnitsAsync(), unit => Assert.Equal(SaveUnitKind.File, unit.Kind));
    }

    [Fact]
    public async Task Skips_GlobalAndSharedCards_ForNow()
    {
        // memorycard.mcd (DuckStation Android's global card) and shared_card_N.mcd carry no recoverable
        // slot in their name, so this slice does not map them rather than guess a slot.
        var memcards = CreateMemcards("memorycard.mcd", "shared_card_1.mcd", "Silent Hill (Europe)_1.mcd");
        var provider = new DuckStationAndroidSaveLocationProvider(memcards);

        var ids = (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId).ToArray();

        Assert.Equal(["playstation/per-game/title/Silent Hill (Europe)_1.mcd"], ids);
    }

    [Fact]
    public void ResolveUnit_RoundTripsAPerGameCardToItsFile()
    {
        var memcards = CreateMemcards("Metal Gear Solid (USA)_1.mcd");
        var provider = new DuckStationAndroidSaveLocationProvider(memcards);

        var location = provider.ResolveUnit("playstation/per-game/title/Metal Gear Solid (USA)_1.mcd");

        Assert.NotNull(location);
        Assert.Equal(Path.Combine(memcards, "Metal Gear Solid (USA)_1.mcd"), location!.Path);
        Assert.Equal(memcards, location.RootPath);
        Assert.Equal(SaveUnitKind.File, location.Kind);
    }

    [Fact]
    public void ResolveUnit_RejectsWrongSchemeForTheName()
    {
        // The card classifies to 'title', so a remote id claiming 'serial' for it must not resolve.
        var memcards = CreateMemcards("Metal Gear Solid (USA)_1.mcd");
        var provider = new DuckStationAndroidSaveLocationProvider(memcards);

        Assert.Null(provider.ResolveUnit("playstation/per-game/serial/Metal Gear Solid (USA)_1.mcd"));
    }

    [Theory]
    [InlineData("playstation/per-game/title/../escape.mcd")]
    [InlineData("playstation/per-game/title/sub/dir.mcd")]
    [InlineData("playstation/shared/card1")]
    [InlineData("playstation/per-game/title/memorycard.mcd")]
    [InlineData("retroarch/psx/whatever")]
    public void ResolveUnit_RejectsUnsafeOrForeignIds(string unitId)
    {
        var provider = new DuckStationAndroidSaveLocationProvider(CreateMemcards());

        Assert.Null(provider.ResolveUnit(unitId));
    }

    [Fact]
    public async Task MissingFolder_YieldsNoUnits()
    {
        var provider = new DuckStationAndroidSaveLocationProvider(
            Path.Combine(BaseDirectory, "does-not-exist", "memcards"));

        Assert.Empty(await provider.GetSaveUnitsAsync());
    }
}
