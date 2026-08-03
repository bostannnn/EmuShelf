using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperSystemMapTests
{
    [Fact]
    public void EverySupportedSystemHasAnExplicitProviderMapping()
    {
        foreach (var system in KnownSystems.All)
        {
            Assert.True(
                ScreenScraperSystemMap.TryGetSystemId(system.Id, out var providerId),
                $"Missing ScreenScraper mapping for {system.Id}.");
            Assert.True(providerId > 0);
        }
    }

    [Fact]
    public void Mapping_IsCaseInsensitive_AndRejectsUnknownSystems()
    {
        Assert.True(ScreenScraperSystemMap.TryGetSystemId("PlayStation2", out var playStation2));
        Assert.Equal(58, playStation2);
        Assert.False(ScreenScraperSystemMap.TryGetSystemId("unknown", out _));
    }
}
