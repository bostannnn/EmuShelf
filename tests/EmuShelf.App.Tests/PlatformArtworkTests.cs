using Avalonia.Headless.XUnit;
using EmuShelf.App.ViewModels;

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
}
