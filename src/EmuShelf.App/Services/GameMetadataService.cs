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

public sealed record MetadataEnrichmentProgress(int Completed, int Total, string? CurrentGameTitle);

public interface IGameMetadataService
{
    Task<MetadataEnrichmentSummary> EnrichAsync(
        IEnumerable<long> gameIds,
        IProgress<MetadataEnrichmentProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<MetadataEnrichmentSummary> EnrichMissingAsync(
        string? systemId = null,
        IProgress<MetadataEnrichmentProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class GameMetadataService : IGameMetadataService
{
    // Disc identification is disk-bound and reads only a few sectors; cover downloads are
    // network-bound and small. Gating them separately keeps the fast download stage from
    // being throttled behind identification, without stampeding either resource.
    private const int IdentifyParallelism = 4;
    private const int DownloadParallelism = 12;
    private const int ArtworkIndexParallelism = 2;

    private readonly IGameMetadataStore _store;
    private readonly IReadOnlyDictionary<string, MetadataSystemProfile> _profiles;
    private readonly IGameMetadataCatalog _catalog;
    private readonly IRemoteArtworkDownloader _artworkDownloader;
    private readonly IGameCoverService _covers;
    private readonly IAppLogger _logger;
    private readonly IGameArtworkTitleIndex _artworkTitleIndex;
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly SemaphoreSlim _artworkIndexGate = new(ArtworkIndexParallelism, ArtworkIndexParallelism);

    public GameMetadataService(
        IGameMetadataStore store,
        IReadOnlyList<MetadataSystemProfile> profiles,
        IGameMetadataCatalog catalog,
        IRemoteArtworkDownloader artworkDownloader,
        IGameCoverService covers,
        IAppLogger? logger = null,
        IGameArtworkTitleIndex? artworkTitleIndex = null)
    {
        _store = store;
        _profiles = profiles.ToDictionary(profile => profile.SystemId, StringComparer.Ordinal);
        _catalog = catalog;
        _artworkDownloader = artworkDownloader;
        _covers = covers;
        _logger = logger ?? NullAppLogger.Instance;
        _artworkTitleIndex = artworkTitleIndex ?? new NullGameArtworkTitleIndex();
    }

    public async Task<MetadataEnrichmentSummary> EnrichMissingAsync(
        string? systemId = null,
        IProgress<MetadataEnrichmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ids = await Task.Run(
            () => _store.GetGamesMissingMetadata(systemId)
                .Select(game => game.Id)
                .ToArray(),
            cancellationToken);
        return await EnrichAsync(ids, progress, cancellationToken);
    }

    public async Task<MetadataEnrichmentSummary> EnrichAsync(
        IEnumerable<long> gameIds,
        IProgress<MetadataEnrichmentProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var ids = gameIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new MetadataEnrichmentSummary(0, 0, 0, 0, 0);

        await _runLock.WaitAsync(cancellationToken);
        try
        {
            using var identifyGate = new SemaphoreSlim(IdentifyParallelism, IdentifyParallelism);
            using var downloadGate = new SemaphoreSlim(DownloadParallelism, DownloadParallelism);
            var completed = 0;
            var tasks = ids.Select(async id =>
            {
                var title = _store.GetGame(id)?.Title;
                var result = await EnrichGameAsync(id, identifyGate, downloadGate, cancellationToken);
                progress?.Report(new MetadataEnrichmentProgress(
                    Interlocked.Increment(ref completed), ids.Length, title));
                return result;
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
        SemaphoreSlim identifyGate,
        SemaphoreSlim downloadGate,
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
            MetadataSystemProfile? profile = null;
            IReadOnlyList<GameIdentifier> identifiers = [];
            Game? current = null;

            // Identification stage: disk-bound disc reads plus the (cached) title catalog.
            await identifyGate.WaitAsync(cancellationToken);
            try
            {
                var game = await Task.Run(() => _store.GetGame(gameId), cancellationToken);
                if (game is null || !_profiles.TryGetValue(game.SystemId, out profile))
                    return new GameEnrichmentResult(false, false, true, false);

                // Reuse identifiers already extracted for this game; a disc's serial does
                // not change, so a re-run never needs to read the disc again.
                identifiers = await Task.Run(() => _store.GetIdentifiers(gameId), cancellationToken);
                if (identifiers.Count == 0)
                {
                    identifiers = await Task.Run(
                        () => profile.IdentifierExtractor.Extract(game),
                        cancellationToken);
                    await Task.Run(
                        () => _store.ReplaceIdentifiers(gameId, identifiers),
                        cancellationToken);
                }

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

                current = await Task.Run(() => _store.GetGame(gameId), cancellationToken);
            }
            finally
            {
                identifyGate.Release();
            }

            if (profile is not null &&
                current is { CoverPath: null } &&
                current.CoverOrigin != GameCoverOrigin.User)
            {
                var filenameMatch = new GameCatalogMatch(
                    "filename-fallback",
                    Path.GetFileNameWithoutExtension(current.Path),
                    Path.GetFileNameWithoutExtension(current.Path),
                    null);
                var catalogCandidates = profile.ArtworkProviders
                    .SelectMany(provider => provider.GetCandidates(identifiers, match))
                    .DistinctBy(candidate => candidate.SourceUri)
                    .ToArray();
                var filenameCandidates = profile.ArtworkProviders
                    .SelectMany(provider => provider.GetCandidates(identifiers, filenameMatch))
                    .DistinctBy(candidate => candidate.SourceUri)
                    .ToArray();
                var localCandidates = GetLocalArtworkCandidates(current).ToArray();

                // The directory index can be several megabytes. Resolve it before acquiring a
                // cover slot, so one playlist cannot block all small cover downloads.
                var indexedCandidates = match is null
                    ? Array.Empty<ArtworkCandidate>()
                    : await GetIndexedCandidatesAsync(profile, match, cancellationToken);
                var candidates = catalogCandidates
                    .Concat(indexedCandidates)
                    .Concat(filenameCandidates)
                    .Concat(localCandidates)
                    .DistinctBy(candidate => candidate.SourceUri)
                    .ToArray();

                // Download stage: network-bound, gated separately from disc reads.
                if (candidates.Length > 0)
                {
                    await downloadGate.WaitAsync(cancellationToken);
                    try
                    {
                        downloaded = await _artworkDownloader.DownloadFirstAsync(
                            candidates,
                            cancellationToken);
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
                    finally
                    {
                        downloadGate.Release();
                    }
                }
            }

            var hasCover = coverApplied || current?.CoverPath is not null;
            var unmatched = match is null && !hasCover && catalogError is null;
            var status = match is not null
                ? GameMetadataStatus.Matched
                : hasCover
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

    private static IEnumerable<ArtworkCandidate> GetLocalArtworkCandidates(Game game)
    {
        var gamePath = game.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var libraryDirectory = Path.GetDirectoryName(gamePath);
        var filename = Path.GetFileNameWithoutExtension(gamePath);
        if (string.IsNullOrWhiteSpace(libraryDirectory) || string.IsNullOrWhiteSpace(filename))
            return [];

        var imagesDirectory = Path.Combine(libraryDirectory, "images");
        return new[] { ".png", ".jpg", ".jpeg", ".webp" }
            .Select(extension => Path.Combine(imagesDirectory, filename + "-thumb" + extension))
            .Where(File.Exists)
            .Select(path => new ArtworkCandidate(
                "local-sidecar-artwork",
                new Uri(path),
                Path.GetExtension(path)));
    }

    private async Task<IReadOnlyList<ArtworkCandidate>> GetIndexedCandidatesAsync(
        MetadataSystemProfile profile,
        GameCatalogMatch match,
        CancellationToken cancellationToken)
    {
        try
        {
            var providers = profile.ArtworkProviders
                .OfType<IArtworkTitleIndexProvider>()
                .ToArray();
            if (providers.Length == 0)
                return [];

            await _artworkIndexGate.WaitAsync(cancellationToken);
            try
            {
                var tasks = providers
                    .Select(provider => _artworkTitleIndex.FindCandidatesAsync(provider, match, cancellationToken));
                var results = await Task.WhenAll(tasks);
                return results.SelectMany(candidates => candidates).ToArray();
            }
            finally
            {
                _artworkIndexGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning("The Libretro artwork title index was unavailable; using direct candidates only.", ex);
            return [];
        }
    }

    private sealed record GameEnrichmentResult(
        bool TitleApplied,
        bool CoverApplied,
        bool Unmatched,
        bool Failed);
}

internal sealed class NullGameArtworkTitleIndex : IGameArtworkTitleIndex
{
    public Task<IReadOnlyList<ArtworkCandidate>> FindCandidatesAsync(
        IArtworkTitleIndexProvider provider,
        GameCatalogMatch match,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArtworkCandidate>>([]);
}

internal sealed class NullGameMetadataService : IGameMetadataService
{
    public Task<MetadataEnrichmentSummary> EnrichAsync(
        IEnumerable<long> gameIds,
        IProgress<MetadataEnrichmentProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MetadataEnrichmentSummary(0, 0, 0, 0, 0));

    public Task<MetadataEnrichmentSummary> EnrichMissingAsync(
        string? systemId = null,
        IProgress<MetadataEnrichmentProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new MetadataEnrichmentSummary(0, 0, 0, 0, 0));
}
