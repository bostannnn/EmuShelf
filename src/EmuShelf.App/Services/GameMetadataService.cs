using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;

namespace EmuShelf.App.Services;

public sealed record MetadataEnrichmentSummary(
    int Processed,
    int TitlesApplied,
    int CoversApplied,
    int Unmatched,
    int Failed)
{
    public string ToStatusText()
    {
        if (Processed == 0)
            return "No games are missing metadata.";

        var parts = new List<string>
        {
            TitlesApplied == 1 ? "1 title" : $"{TitlesApplied} titles",
            CoversApplied == 1 ? "1 cover" : $"{CoversApplied} covers",
        };
        if (Unmatched > 0)
            parts.Add($"{Unmatched} unmatched");
        if (Failed > 0)
            parts.Add($"{Failed} failed");
        return $"Metadata complete — {string.Join(", ", parts)}";
    }
}

public interface IGameMetadataService
{
    Task<MetadataEnrichmentSummary> EnrichAsync(
        IEnumerable<long> gameIds,
        CancellationToken cancellationToken = default);

    Task<MetadataEnrichmentSummary> EnrichMissingAsync(
        string? systemId = null,
        CancellationToken cancellationToken = default);
}

public sealed class GameMetadataService : IGameMetadataService
{
    private readonly IGameMetadataStore _store;
    private readonly IReadOnlyDictionary<string, MetadataSystemProfile> _profiles;
    private readonly IGameMetadataCatalog _catalog;
    private readonly IRemoteArtworkDownloader _artworkDownloader;
    private readonly IGameCoverService _covers;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public GameMetadataService(
        IGameMetadataStore store,
        IReadOnlyList<MetadataSystemProfile> profiles,
        IGameMetadataCatalog catalog,
        IRemoteArtworkDownloader artworkDownloader,
        IGameCoverService covers,
        IAppLogger? logger = null)
    {
        _store = store;
        _profiles = profiles.ToDictionary(profile => profile.SystemId, StringComparer.Ordinal);
        _catalog = catalog;
        _artworkDownloader = artworkDownloader;
        _covers = covers;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<MetadataEnrichmentSummary> EnrichMissingAsync(
        string? systemId = null,
        CancellationToken cancellationToken = default)
    {
        var ids = await Task.Run(
            () => _store.GetGamesMissingMetadata(systemId)
                .Select(game => game.Id)
                .ToArray(),
            cancellationToken);
        return await EnrichAsync(ids, cancellationToken);
    }

    public async Task<MetadataEnrichmentSummary> EnrichAsync(
        IEnumerable<long> gameIds,
        CancellationToken cancellationToken = default)
    {
        var ids = gameIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new MetadataEnrichmentSummary(0, 0, 0, 0, 0);

        await _runLock.WaitAsync(cancellationToken);
        try
        {
            using var concurrency = new SemaphoreSlim(2, 2);
            var tasks = ids.Select(async id =>
            {
                await concurrency.WaitAsync(cancellationToken);
                try
                {
                    return await EnrichGameAsync(id, cancellationToken);
                }
                finally
                {
                    concurrency.Release();
                }
            });
            var results = await Task.WhenAll(tasks);
            return new MetadataEnrichmentSummary(
                results.Length,
                results.Count(result => result.TitleApplied),
                results.Count(result => result.CoverApplied),
                results.Count(result => result.Unmatched),
                results.Count(result => result.Failed));
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<GameEnrichmentResult> EnrichGameAsync(
        long gameId,
        CancellationToken cancellationToken)
    {
        GameCatalogMatch? match = null;
        DownloadedArtwork? downloaded = null;
        ImportedGameCover? imported = null;
        string? catalogError = null;
        var titleApplied = false;
        var coverApplied = false;
        try
        {
            var game = await Task.Run(() => _store.GetGame(gameId), cancellationToken);
            if (game is null || !_profiles.TryGetValue(game.SystemId, out var profile))
                return new GameEnrichmentResult(false, false, true, false);

            var identifiers = await Task.Run(
                () => profile.IdentifierExtractor.Extract(game),
                cancellationToken);
            await Task.Run(() => _store.ReplaceIdentifiers(gameId, identifiers), cancellationToken);

            if (identifiers.Count > 0)
            {
                try
                {
                    match = await _catalog.FindMatchAsync(profile, identifiers, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Serial-addressed providers such as xlenore do not need the title
                    // catalog. Keep that useful fallback available when the DAT source is
                    // temporarily offline, while retaining the catalog error as provenance.
                    catalogError = ex.Message;
                    _logger.Warning(
                        $"The metadata catalog was unavailable for game id {gameId}.",
                        ex);
                }
            }

            if (match is not null)
            {
                var filenameTitle = Path.GetFileNameWithoutExtension(game.Path);
                var titleChanged = !string.Equals(
                    game.Title,
                    match.CanonicalTitle,
                    StringComparison.Ordinal);
                var titleAccepted = await Task.Run(
                    () => _store.TryApplyCatalogTitle(
                        gameId,
                        match.CanonicalTitle,
                        filenameTitle),
                    cancellationToken);
                titleApplied = titleChanged && titleAccepted;
            }

            var current = await Task.Run(() => _store.GetGame(gameId), cancellationToken);
            if (current is { CoverPath: null } && current.CoverOrigin != GameCoverOrigin.User)
            {
                var candidates = profile.ArtworkProviders
                    .SelectMany(provider => provider.GetCandidates(identifiers, match))
                    .ToArray();
                downloaded = await _artworkDownloader.DownloadFirstAsync(candidates, cancellationToken);
                if (downloaded is not null)
                {
                    imported = await _covers.ImportAsync(
                        gameId,
                        downloaded.TemporaryPath,
                        cancellationToken);
                    coverApplied = await Task.Run(
                        () => _store.TryApplyDownloadedCover(
                            gameId,
                            imported.CoverPath,
                            downloaded.Candidate.ProviderId,
                            downloaded.Candidate.SourceUri.ToString()),
                        cancellationToken);
                    if (!coverApplied)
                    {
                        await _covers.DeleteOwnedCoverAsync(
                            gameId,
                            imported.CoverPath,
                            cancellationToken);
                        imported = null;
                    }
                }
            }

            var unmatched = match is null && !coverApplied && catalogError is null;
            var status = match is not null
                ? GameMetadataStatus.Matched
                : coverApplied
                    ? GameMetadataStatus.Partial
                    : catalogError is not null
                        ? GameMetadataStatus.Failed
                        : GameMetadataStatus.Unmatched;
            await Task.Run(() => _store.RecordAttempt(new GameMetadataAttempt(
                gameId,
                status,
                match,
                coverApplied ? downloaded?.Candidate.ProviderId : null,
                coverApplied ? downloaded?.Candidate.SourceUri.ToString() : null,
                catalogError,
                DateTimeOffset.UtcNow)), cancellationToken);
            return new GameEnrichmentResult(
                titleApplied,
                coverApplied,
                unmatched,
                status == GameMetadataStatus.Failed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Metadata enrichment failed for game id {gameId}.", ex);
            try
            {
                await Task.Run(() => _store.RecordAttempt(new GameMetadataAttempt(
                    gameId,
                    GameMetadataStatus.Failed,
                    match,
                    null,
                    null,
                    ex.Message,
                    DateTimeOffset.UtcNow)), CancellationToken.None);
            }
            catch (Exception recordException)
            {
                _logger.Warning($"Could not record metadata failure for game id {gameId}.", recordException);
            }
            return new GameEnrichmentResult(titleApplied, coverApplied, false, true);
        }
        finally
        {
            if (downloaded is not null)
                File.Delete(downloaded.TemporaryPath);

            // If staging succeeded but the DB switch threw, the staged owned file has
            // no authoritative reference and is safe to remove.
            if (imported is not null && !coverApplied)
            {
                try
                {
                    await _covers.DeleteOwnedCoverAsync(gameId, imported.CoverPath);
                }
                catch (Exception cleanupException)
                {
                    _logger.Warning(
                        $"Could not remove an uncommitted downloaded cover for game id {gameId}.",
                        cleanupException);
                }
            }
        }
    }

    private sealed record GameEnrichmentResult(
        bool TitleApplied,
        bool CoverApplied,
        bool Unmatched,
        bool Failed);
}

internal sealed class NullGameMetadataService : IGameMetadataService
{
    public Task<MetadataEnrichmentSummary> EnrichAsync(
        IEnumerable<long> gameIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MetadataEnrichmentSummary(0, 0, 0, 0, 0));

    public Task<MetadataEnrichmentSummary> EnrichMissingAsync(
        string? systemId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MetadataEnrichmentSummary(0, 0, 0, 0, 0));
}
