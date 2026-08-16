using EmuShelf.Core.Metadata;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

/// <summary>
/// Code-owned defaults for which ScreenScraper fields and media EmuShelf fetches, and how it ranks
/// regional/localized variants. These are not user preferences — nothing in the app edits them — so
/// they live in code rather than being serialized into <c>settings.json</c> (where an older build's
/// frozen copy would otherwise filter newly added kinds/fields out of the scraper after an update).
/// </summary>
public static class ScreenScraperMediaProfile
{
    public const string PreferredLanguage = "en";

    public static IReadOnlyList<string> RegionPriority { get; } =
        ["wor", "us", "eu", "jp", "sp", "fr", "de", "it", "kr", "cn"];

    public static IReadOnlyList<GameMetadataField> MetadataFields { get; } =
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

    // Video is intentionally absent: it has no in-app player yet, so it is opt-in rather than
    // downloaded by default.
    public static IReadOnlyList<GameMediaKind> MediaKinds { get; } =
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
