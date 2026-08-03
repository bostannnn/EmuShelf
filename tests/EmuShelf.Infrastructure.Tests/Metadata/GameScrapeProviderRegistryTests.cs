using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class GameScrapeProviderRegistryTests
{
    [Fact]
    public void KnownProviders_KeepAutomaticAndManualSourcesDistinct()
    {
        var registry = new GameScrapeProviderRegistry(KnownScrapeProviders.All);

        Assert.True(registry.TryGet(KnownScrapeProviders.ScreenScraperId, out var screenScraper));
        Assert.NotNull(screenScraper);
        Assert.True(screenScraper.RequiresAuthentication);
        Assert.True(screenScraper.Capabilities.HasFlag(GameScrapeCapability.Metadata));
        Assert.True(screenScraper.Capabilities.HasFlag(GameScrapeCapability.Fanart));
        Assert.Equal(GameScrapeProviderTrust.VerifiedIdentity, screenScraper.Trust);

        Assert.True(registry.TryGet(KnownScrapeProviders.DuckDuckGoArtworkId, out var duckDuckGo));
        Assert.NotNull(duckDuckGo);
        Assert.False(duckDuckGo.Capabilities.HasFlag(GameScrapeCapability.Metadata));
        Assert.False(duckDuckGo.Capabilities.HasFlag(GameScrapeCapability.Batch));
        Assert.Equal(GameScrapeProviderTrust.UserReviewedSearch, duckDuckGo.Trust);
    }

    [Fact]
    public void ProviderIds_AreCaseInsensitiveAndMustBeUnique()
    {
        var provider = KnownScrapeProviders.All[0];

        Assert.Throws<ArgumentException>(() => new GameScrapeProviderRegistry(
            [provider, provider with { Id = provider.Id.ToUpperInvariant() }]));
    }
}
