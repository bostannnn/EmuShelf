using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

/// <summary>
/// Turns a non-mutating <see cref="ScreenScraperGamePreview"/> into a provider-neutral
/// <see cref="GameScrapeApplyRequest"/>. The caller may narrow which fields/media are applied; the
/// default includes everything the preview surfaced (already filtered by the user's settings).
/// </summary>
public static class ScreenScraperApplyMapper
{
    public static GameScrapeApplyRequest BuildRequest(
        ScreenScraperGamePreview preview,
        GameMetadataApplyMode mode,
        IReadOnlySet<GameMetadataField>? includeFields = null,
        IReadOnlySet<GameMediaKind>? includeMedia = null)
    {
        ArgumentNullException.ThrowIfNull(preview);

        var metadata = includeFields is null
            ? preview.Metadata.ToList()
            : preview.Metadata.Where(value => includeFields.Contains(value.Field)).ToList();

        var media = preview.Media
            .Where(entry => includeMedia is null || includeMedia.Contains(entry.Key))
            .Select(entry => ToImport(entry.Key, entry.Value))
            .ToList();

        return new GameScrapeApplyRequest(
            preview.GameId, preview.Match, metadata, media, mode, preview.CoverKind);
    }

    private static GameMediaImport ToImport(GameMediaKind kind, ScreenScraperMediaCandidate candidate) =>
        new(
            kind,
            candidate.SourceUri,
            candidate.FileExtension,
            ScreenScraperProvider.Id,
            candidate.ProviderMediaId,
            candidate.Region,
            candidate.Language,
            candidate.Width,
            candidate.Height,
            candidate.Crc32,
            candidate.Md5,
            candidate.Sha1);
}
