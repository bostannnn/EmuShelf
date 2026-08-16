namespace EmuShelf.Core.Settings;

public sealed record ScrapingSettings
{
    /// <summary>Authenticated ScreenScraper metadata and media. Off until an account is connected.</summary>
    public ScreenScraperSettings ScreenScraper { get; init; } = new();

    /// <summary>
    /// Manual, user-driven web image search for covers (the "Set cover" picker). Unverified results are
    /// never applied automatically; this only toggles whether the manual picker offers web search at all.
    /// </summary>
    public bool WebImageSearchEnabled { get; init; } = true;
}

public sealed record ScreenScraperSettings
{
    /// <summary>
    /// True while a ScreenScraper account is connected; cleared on disconnect. This mirrors connection
    /// state rather than being an independent preference — there is no separate "use ScreenScraper" toggle.
    /// </summary>
    public bool Enabled { get; init; }
}
