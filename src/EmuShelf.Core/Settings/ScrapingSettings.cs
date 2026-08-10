using EmuShelf.Core.Metadata;

namespace EmuShelf.Core.Settings;

public sealed record ScrapingSettings
{
    /// <summary>The existing exact-match catalogue and cover pipeline.</summary>
    public ScrapeProviderSettings BuiltInCatalog { get; init; } = new();

    /// <summary>Authenticated ScreenScraper metadata and media. Off until an account is connected.</summary>
    public ScreenScraperSettings ScreenScraper { get; init; } = new();

    /// <summary>Unverified, user-selected web image search. Never used by automatic scraping.</summary>
    public ScrapeProviderSettings DuckDuckGoArtwork { get; init; } = new();
}

public sealed record ScrapeProviderSettings
{
    public bool Enabled { get; init; } = true;
}

public sealed record ScreenScraperSettings
{
    public bool Enabled { get; init; }

    /// <summary>Separate opt-in; connecting an account never enables background scraping.</summary>
    public bool AutomaticallyScrapeAfterImport { get; init; }

    public string PreferredLanguage { get; init; } = "en";

    public string[] RegionPriority { get; init; } =
        ["wor", "us", "eu", "jp", "sp", "fr", "de", "it", "kr", "cn"];

    public GameMetadataField[] MetadataFields { get; init; } =
    [
        GameMetadataField.Title,
        GameMetadataField.Developer,
        GameMetadataField.Publisher,
        GameMetadataField.Genre,
        GameMetadataField.Description,
        GameMetadataField.ReleaseDate,
        GameMetadataField.Players,
        GameMetadataField.Rating,
    ];

    // Video is intentionally absent: it has no in-app player yet, so it is opt-in (add
    // GameMediaKind.Video here) rather than downloaded by default.
    public GameMediaKind[] MediaKinds { get; init; } =
    [
        GameMediaKind.BoxFront,
        GameMediaKind.Screenshot,
        GameMediaKind.Wheel,
        GameMediaKind.Fanart,
        GameMediaKind.TitleScreen,
        GameMediaKind.BoxBack,
        GameMediaKind.BoxSpine,
        GameMediaKind.PhysicalMedia,
        GameMediaKind.PhysicalMediaTexture,
    ];
}
