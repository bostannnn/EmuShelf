using Avalonia.Headless.XUnit;
using EmuShelf.App.ViewModels;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

public class PlatformArtworkTests
{
    [AvaloniaFact]
    public void Catalog_LoadsCurrentAndFuturePlatformArtwork()
    {
        foreach (var systemId in PlatformArtwork.SupportedSystemIds)
            Assert.NotNull(PlatformArtwork.ForSystem(systemId));

        Assert.Contains("playstation3", PlatformArtwork.SupportedSystemIds);
        Assert.Contains("playstation4", PlatformArtwork.SupportedSystemIds);
        Assert.Contains("nds", PlatformArtwork.SupportedSystemIds);
        Assert.Contains("psp", PlatformArtwork.SupportedSystemIds);
        Assert.Contains("dreamcast", PlatformArtwork.SupportedSystemIds);

        Assert.Null(PlatformArtwork.ForSystem("not-a-platform"));
    }

    [AvaloniaFact]
    public void ExpansionSystems_HaveStableNavigationIdsAndLicensedArtwork()
    {
        // The systems added after the original five, by stable id. Order-independent: the navigation
        // list is grouped by manufacturer (see NavigationOrder_* below), so what matters here is that
        // every expansion id is still present and still has licensed artwork.
        string[] expansionSystemIds =
            ["psp", "megadrive", "nds", "gba", "3ds", "nes", "snes", "dreamcast", "arcade", "gbc"];
        Assert.All(expansionSystemIds, id =>
            Assert.Contains(KnownSystems.All, system => system.Id == id));
        Assert.All(expansionSystemIds, id =>
            Assert.NotNull(PlatformArtwork.ForSystem(id)));
        Assert.Equal(
            0.708,
            KnownSystems.All.Single(system => system.Id == "megadrive").CoverAspectRatio);
        Assert.Equal(
            1.115,
            KnownSystems.All.Single(system => system.Id == "nds").CoverAspectRatio);
        Assert.Equal(
            0.581,
            KnownSystems.All.Single(system => system.Id == "psp").CoverAspectRatio);
        Assert.Equal(
            1.0,
            KnownSystems.All.Single(system => system.Id == "gba").CoverAspectRatio);
        // 3DS front covers from GameTDB use a fixed near-square 768×680 canvas.
        Assert.Equal(
            1.129,
            KnownSystems.All.Single(system => system.Id == "3ds").CoverAspectRatio);
        // NES uses the portrait North-American cardboard box, like the disc systems.
        Assert.Equal(
            0.72,
            KnownSystems.All.Single(system => system.Id == "nes").CoverAspectRatio);
        // SNES box art is the wide North-American box (Libretro scans cluster at 512×357).
        Assert.Equal(
            1.434,
            KnownSystems.All.Single(system => system.Id == "snes").CoverAspectRatio);
        Assert.Equal(
            1.0,
            KnownSystems.All.Single(system => system.Id == "dreamcast").CoverAspectRatio);
        // Arcade is landscape 4:3 — the card is a title screen / snap, not portrait box art.
        Assert.Equal(
            1.333,
            KnownSystems.All.Single(system => system.Id == "arcade").CoverAspectRatio);
        // Game Boy Color reuses the square Game Boy family frame.
        Assert.Equal(
            1.0,
            KnownSystems.All.Single(system => system.Id == "gbc").CoverAspectRatio);
    }

    [AvaloniaFact]
    public void NavigationOrder_GroupsSystemsByManufacturer_OldestFirst()
    {
        var manufacturers = KnownSystems.All.Select(system => system.Manufacturer).ToArray();

        // The manufacturer value at the start of each run. If a maker were split across the list it
        // would appear in this sequence twice, so asserting the exact run sequence proves both that
        // every manufacturer is contiguous and that the groups run oldest-maker-first.
        var groupOrder = manufacturers
            .Where((manufacturer, index) => index == 0 || manufacturer != manufacturers[index - 1])
            .ToArray();

        Assert.Equal(["Nintendo", "Sega", "Sony", "Arcade"], groupOrder);
    }
}
