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

    /// <summary>
    /// Re-merges the current catalogue defaults into <see cref="MediaKinds"/> and
    /// <see cref="MetadataFields"/>. These two lists are code-owned defaults, not user preferences —
    /// nothing in the app edits them — yet they are serialized into <c>settings.json</c>. A file
    /// written by an older build therefore froze the shorter lists that build shipped with, so after an
    /// in-place update the newly added kinds/fields (e.g. the title screen) would be filtered out of the
    /// scraper before ever reaching the UI. Loading calls this so every supported kind/field is present,
    /// while anything the file already listed is preserved.
    /// </summary>
    public ScreenScraperSettings WithCatalogDefaultsEnsured()
    {
        var defaults = new ScreenScraperSettings();
        return this with
        {
            MetadataFields = EnsureAll(MetadataFields, defaults.MetadataFields),
            MediaKinds = EnsureAll(MediaKinds, defaults.MediaKinds),
        };
    }

    // Appends any default entry the persisted list is missing, keeping the persisted order and returning
    // the original array untouched when nothing needs adding (so an already-current file is a no-op).
    private static T[] EnsureAll<T>(T[]? current, T[] defaults)
    {
        if (current is null || current.Length == 0)
            return defaults;

        List<T>? merged = null;
        foreach (var value in defaults)
        {
            if (Array.IndexOf(current, value) >= 0 || (merged?.Contains(value) ?? false))
                continue;
            merged ??= [.. current];
            merged.Add(value);
        }

        return merged?.ToArray() ?? current;
    }
}
