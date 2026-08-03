using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;

namespace EmuShelf.Integrations.Metadata;

public static class KnownScrapeProviders
{
    public const string BuiltInCatalogId = "built-in-catalog";
    public const string ScreenScraperId = ScreenScraperProvider.Id;
    public const string DuckDuckGoArtworkId = "duckduckgo-artwork";

    public static IReadOnlyList<GameScrapeProviderDescriptor> All { get; } =
    [
        new(
            BuiltInCatalogId,
            "Built-in catalogue",
            GameScrapeCapability.Metadata |
            GameScrapeCapability.BoxFront |
            GameScrapeCapability.Batch,
            GameScrapeProviderTrust.VerifiedIdentity,
            RequiresAuthentication: false),
        new(
            ScreenScraperId,
            "ScreenScraper",
            GameScrapeCapability.Metadata |
            GameScrapeCapability.BoxFront |
            GameScrapeCapability.Screenshot |
            GameScrapeCapability.Wheel |
            GameScrapeCapability.Fanart |
            GameScrapeCapability.Batch |
            GameScrapeCapability.ManualTitleSearch,
            GameScrapeProviderTrust.VerifiedIdentity,
            RequiresAuthentication: true),
        new(
            DuckDuckGoArtworkId,
            "DuckDuckGo artwork search",
            GameScrapeCapability.BoxFront |
            GameScrapeCapability.ManualTitleSearch,
            GameScrapeProviderTrust.UserReviewedSearch,
            RequiresAuthentication: false),
    ];
}
