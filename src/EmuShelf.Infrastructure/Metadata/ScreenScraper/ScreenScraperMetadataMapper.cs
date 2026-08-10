using System.Globalization;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

/// <summary>
/// Converts provider-specific results into provenance-bearing domain values without applying
/// them to the library. The caller chooses fill-missing, provider refresh, or user-edit policy.
/// </summary>
public static class ScreenScraperMetadataMapper
{
    public static IReadOnlyList<GameMetadataValue> MapMetadata(
        long gameId,
        int screenScraperSystemId,
        ScreenScraperGameInfo game,
        ScreenScraperSettings settings,
        DateTimeOffset fetchedAt)
    {
        var fields = settings.MetadataFields ?? [];
        var values = new List<GameMetadataValue>();
        var sourceUri = BuildPublicGameUri(screenScraperSystemId, game.ProviderGameId);

        if (fields.Contains(GameMetadataField.Title))
            Add(values, gameId, GameMetadataField.Title, SelectByRegion(game.Names, settings), null, game, sourceUri, fetchedAt);
        if (fields.Contains(GameMetadataField.Developer))
            Add(values, gameId, GameMetadataField.Developer, game.Developer, null, game, sourceUri, fetchedAt);
        if (fields.Contains(GameMetadataField.Publisher))
            Add(values, gameId, GameMetadataField.Publisher, game.Publisher, null, game, sourceUri, fetchedAt);
        if (fields.Contains(GameMetadataField.Genre))
            Add(values, gameId, GameMetadataField.Genre, SelectByLanguage(game.Genres, settings), null, game, sourceUri, fetchedAt);
        if (fields.Contains(GameMetadataField.ReleaseDate))
            Add(values, gameId, GameMetadataField.ReleaseDate, SelectDate(game.ReleaseDates, settings), null, game, sourceUri, fetchedAt);
        if (fields.Contains(GameMetadataField.Players))
            Add(values, gameId, GameMetadataField.Players, game.Players, null, game, sourceUri, fetchedAt);
        if (fields.Contains(GameMetadataField.Rating) &&
            decimal.TryParse(game.Rating, NumberStyles.Number, CultureInfo.InvariantCulture, out var rating))
        {
            Add(
                values,
                gameId,
                GameMetadataField.Rating,
                rating.ToString("0.##", CultureInfo.InvariantCulture),
                null,
                game,
                sourceUri,
                fetchedAt);
        }

        if (fields.Contains(GameMetadataField.Description))
        {
            foreach (var description in game.Descriptions
                         .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                         .GroupBy(item => NormalizeCode(item.Language), StringComparer.OrdinalIgnoreCase)
                         .Select(group => group.First()))
            {
                Add(
                    values,
                    gameId,
                    GameMetadataField.Description,
                    description.Value,
                    NormalizeCode(description.Language),
                    game,
                    sourceUri,
                    fetchedAt);
            }
        }

        return values;
    }

    public static IReadOnlyDictionary<GameMediaKind, ScreenScraperMediaCandidate> SelectMedia(
        ScreenScraperGameInfo game,
        ScreenScraperSettings settings)
    {
        var selected = new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>();
        foreach (var kind in settings.MediaKinds ?? [])
        {
            var candidate = game.Media
                .Select(media => new { Media = media, TypeRank = GetTypeRank(kind, media.MediaType) })
                .Where(item => item.TypeRank >= 0)
                .OrderBy(item => GetRegionRank(item.Media.Region, settings.RegionPriority))
                .ThenBy(item => GetLanguageRank(item.Media.Language, settings.PreferredLanguage))
                .ThenBy(item => item.TypeRank)
                .Select(item => item.Media)
                .FirstOrDefault();
            if (candidate is not null)
                selected[kind] = candidate;
        }
        return selected;
    }

    private static string? SelectByRegion(
        IReadOnlyList<ScreenScraperLocalizedText> values,
        ScreenScraperSettings settings) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .OrderBy(value => GetRegionRank(value.Region, settings.RegionPriority))
            .Select(value => value.Value)
            .FirstOrDefault();

    private static string? SelectByLanguage(
        IReadOnlyList<ScreenScraperLocalizedText> values,
        ScreenScraperSettings settings) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value.Value))
            .OrderBy(value => GetLanguageRank(value.Language, settings.PreferredLanguage))
            .Select(value => value.Value)
            .FirstOrDefault();

    private static string? SelectDate(
        IReadOnlyList<ScreenScraperReleaseDate> values,
        ScreenScraperSettings settings) =>
        values
            .Where(value => DateOnly.TryParse(
                value.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
            .OrderBy(value => GetRegionRank(value.Region, settings.RegionPriority))
            .Select(value => DateOnly.Parse(value.Value, CultureInfo.InvariantCulture).ToString("yyyy-MM-dd"))
            .FirstOrDefault();

    private static int GetTypeRank(GameMediaKind kind, string mediaType)
    {
        var types = kind switch
        {
            GameMediaKind.BoxFront => new[] { "box-2D" },
            GameMediaKind.Screenshot => ["ss-hd", "ss"],
            GameMediaKind.Wheel => ["wheel-hd", "wheel"],
            GameMediaKind.Fanart => new[] { "fanart" },
            GameMediaKind.TitleScreen => ["sstitle"],
            GameMediaKind.BoxBack => ["box-2D-back"],
            GameMediaKind.BoxSpine => ["box-2D-side"],
            GameMediaKind.PhysicalMedia => ["support-2D"],
            GameMediaKind.PhysicalMediaTexture => ["support-texture"],
            // Prefer the normalized encode (standardized, smaller) over the raw source video.
            GameMediaKind.Video => ["video-normalized", "video"],
            _ => [],
        };
        return Array.FindIndex(types, type => string.Equals(type, mediaType, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Which media kind is projected to a game's cover, by EmuShelf system. Arcade uses the title
    /// screen because arcade box art is nearly nonexistent (its built-in cover already prefers the
    /// Libretro title thumbnail); every other system keeps the box front.
    /// </summary>
    public static GameMediaKind CoverKindFor(string systemId) =>
        string.Equals(systemId, "arcade", StringComparison.OrdinalIgnoreCase)
            ? GameMediaKind.TitleScreen
            : GameMediaKind.BoxFront;

    private static int GetRegionRank(string? region, IReadOnlyList<string>? priority)
    {
        if (priority is not null)
        {
            for (var index = 0; index < priority.Count; index++)
            {
                if (string.Equals(priority[index], region, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
        }
        return string.IsNullOrWhiteSpace(region) ? 10_000 : 20_000;
    }

    private static int GetLanguageRank(string? language, string? preferredLanguage)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguage) &&
            string.Equals(language, preferredLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
            return 1;
        return string.IsNullOrWhiteSpace(language) ? 2 : 3;
    }

    private static void Add(
        ICollection<GameMetadataValue> values,
        long gameId,
        GameMetadataField field,
        string? value,
        string? locale,
        ScreenScraperGameInfo game,
        string sourceUri,
        DateTimeOffset fetchedAt)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        values.Add(new GameMetadataValue(
            gameId,
            field,
            value.Trim(),
            locale,
            GameMetadataValueOrigin.Provider,
            ScreenScraperProvider.Id,
            game.ProviderGameId,
            sourceUri,
            fetchedAt));
    }

    private static string BuildPublicGameUri(int systemId, string gameId) =>
        $"https://www.screenscraper.fr/gameinfos.php?plateforme={systemId.ToString(CultureInfo.InvariantCulture)}" +
        $"&gameid={Uri.EscapeDataString(gameId)}";

    private static string? NormalizeCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
