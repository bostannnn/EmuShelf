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
        Assert.Equal(
            ["psp", "megadrive", "nds", "gba"],
            KnownSystems.All.Skip(5).Select(system => system.Id));
        Assert.All(KnownSystems.All.Skip(5), system =>
            Assert.NotNull(PlatformArtwork.ForSystem(system.Id)));
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
    }
}
