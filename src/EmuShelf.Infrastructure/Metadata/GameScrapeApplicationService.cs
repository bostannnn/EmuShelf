using System.Globalization;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>
/// Applies a provider scrape result to a single game. Metadata precedence and media-ownership
/// rules are enforced by <see cref="IGameDetailsStore"/>; this service orchestrates the safe
/// download, atomic file placement under portable <c>Data/Media/</c>, cover projection, and
/// provider-match recording. It never reads or writes the game's own files, and the caller is
/// expected to invoke it off the UI thread.
/// </summary>
public sealed class GameScrapeApplicationService : IGameScrapeApplicationService
{
    private readonly IGameDetailsStore _details;
    private readonly IGameMetadataStore _games;
    private readonly IRemoteArtworkDownloader _downloader;
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;

    public GameScrapeApplicationService(
        IGameDetailsStore details,
        IGameMetadataStore games,
        IRemoteArtworkDownloader downloader,
        IAppPaths paths,
        IAppLogger? logger = null)
    {
        _details = details;
        _games = games;
        _downloader = downloader;
        _paths = paths;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<GameScrapeApplyResult> ApplyAsync(
        GameScrapeApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Game ID must be positive.");

        var metadataApplied = 0;
        var metadataSkipped = 0;
        foreach (var value in request.Metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (value.GameId != request.GameId)
                throw new ArgumentException("Metadata value targets a different game.", nameof(request));
            if (_details.TryApplyMetadata(value, request.Mode))
                metadataApplied++;
            else
                metadataSkipped++;
        }

        var existing = _details.GetDetails(request.GameId);
        var mediaResults = new List<GameMediaApplyResult>();
        var coverProjected = false;
        foreach (var import in request.Media)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (result, saved) = await ImportMediaAsync(request, existing, import, cancellationToken);
            mediaResults.Add(result);

            if (saved is { IsSelected: true } &&
                request.CoverKind is { } coverKind &&
                import.Kind == coverKind &&
                _games.TryApplyDownloadedCover(
                    request.GameId,
                    saved.LocalPath,
                    import.ProviderId,
                    import.SourceUri.ToString()))
            {
                coverProjected = true;
            }
        }

        _details.UpsertProviderMatch(request.Match);

        return new GameScrapeApplyResult(
            request.GameId,
            metadataApplied,
            metadataSkipped,
            mediaResults,
            coverProjected);
    }

    private async Task<(GameMediaApplyResult Result, GameMediaAsset? Saved)> ImportMediaAsync(
        GameScrapeApplyRequest request,
        GameDetails existing,
        GameMediaImport import,
        CancellationToken cancellationToken)
    {
        // Fill-missing never replaces a media kind that already has an active (selected) asset — unless
        // the caller explicitly opted in (OverwriteExistingMedia), which the single-game scraper does so a
        // ticked media row means "use this art" and replaces the current one instead of being skipped.
        var activeForKind = existing.Media.FirstOrDefault(media => media.Kind == import.Kind && media.IsSelected);
        if (request.Mode == GameMetadataApplyMode.FillMissing &&
            !request.OverwriteExistingMedia &&
            activeForKind is not null)
        {
            return (new GameMediaApplyResult(import.Kind, GameMediaApplyOutcome.SkippedExisting), null);
        }

        var extension = NormalizeExtension(import.FileExtension);
        var finalPath = MediaFilePath(request.GameId, import.Kind, import.ProviderId, extension);

        // One file per (provider, kind). Same-provider media at this path is our own and may be
        // refreshed; a user-owned or foreign-provider file here is off-limits, so bail out BEFORE
        // downloading or moving anything — never overwrite a file this provider does not own.
        var blocking = existing.Media.FirstOrDefault(media =>
            media.Kind == import.Kind &&
            PathsEqual(media.LocalPath, finalPath) &&
            (media.Origin != GameMediaOrigin.Provider ||
             !string.Equals(media.ProviderId, import.ProviderId, StringComparison.OrdinalIgnoreCase)));
        if (blocking is not null)
        {
            return (new GameMediaApplyResult(
                import.Kind,
                GameMediaApplyOutcome.SkippedProtected,
                "A user-owned or another provider's asset already holds this media."), null);
        }

        var mediaKind = import.Kind == GameMediaKind.Video ? RemoteMediaKind.Video : RemoteMediaKind.Image;
        var candidate = new ArtworkCandidate(import.ProviderId, import.SourceUri, extension, mediaKind);
        DownloadedArtwork? downloaded;
        try
        {
            downloaded = await _downloader.DownloadFirstAsync([candidate], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        if (downloaded is null)
        {
            return (new GameMediaApplyResult(
                import.Kind,
                GameMediaApplyOutcome.DownloadFailed,
                "The media could not be downloaded."), null);
        }

        try
        {
            PlaceMediaFile(finalPath, downloaded.TemporaryPath);
        }
        catch (IOException ex)
        {
            SafeDelete(downloaded.TemporaryPath);
            _logger.Warning($"Could not store scraped media for game {request.GameId} ({import.Kind}).", ex);
            return (new GameMediaApplyResult(
                import.Kind,
                GameMediaApplyOutcome.DownloadFailed,
                "The media could not be stored."), null);
        }

        var existingProviderAsset = existing.Media.FirstOrDefault(media =>
            media.Kind == import.Kind &&
            media.Origin == GameMediaOrigin.Provider &&
            string.Equals(media.ProviderId, import.ProviderId, StringComparison.OrdinalIgnoreCase));

        var asset = new GameMediaAsset(
            existingProviderAsset?.Id ?? 0,
            request.GameId,
            import.Kind,
            finalPath,
            IsSelected: true,
            SelectionOrigin: GameMediaSelectionOrigin.Provider,
            Origin: GameMediaOrigin.Provider,
            ProviderId: import.ProviderId,
            ProviderItemId: import.ProviderItemId,
            SourceUri: import.SourceUri.ToString(),
            Region: import.Region,
            Language: import.Language,
            FileExtension: extension,
            Width: import.Width,
            Height: import.Height,
            Crc32: import.Crc32,
            Md5: import.Md5,
            Sha1: import.Sha1,
            UpdatedAt: DateTimeOffset.UtcNow);

        try
        {
            var saved = _details.SaveMedia(asset);
            return (new GameMediaApplyResult(import.Kind, GameMediaApplyOutcome.Imported), saved);
        }
        catch (InvalidOperationException ex)
        {
            // Provider media may not overwrite user-owned or another provider's asset for this kind.
            SafeDelete(finalPath);
            return (new GameMediaApplyResult(import.Kind, GameMediaApplyOutcome.SkippedProtected, ex.Message), null);
        }
    }

    private string MediaFilePath(long gameId, GameMediaKind kind, string providerId, string extension) =>
        Path.Combine(
            _paths.DataDirectory,
            "Media",
            gameId.ToString(CultureInfo.InvariantCulture),
            $"{SanitizeProviderId(providerId)}-{kind}{extension}");

    private void PlaceMediaFile(string finalPath, string temporaryPath)
    {
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);
        File.Move(temporaryPath, finalPath, overwrite: true);

        // A refresh whose extension changed leaves a stale file (e.g. screenscraper-BoxFront.png
        // beside the new screenscraper-BoxFront.jpg); remove other-extension files for this
        // provider+kind so exactly one file backs one asset.
        var keepName = Path.GetFileName(finalPath);
        var prefix = keepName[..keepName.LastIndexOf('.')];
        foreach (var stale in Directory.EnumerateFiles(directory, $"{prefix}.*"))
        {
            if (!string.Equals(Path.GetFileName(stale), keepName, StringComparison.OrdinalIgnoreCase))
                SafeDelete(stale);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string SanitizeProviderId(string providerId)
    {
        Span<char> buffer = stackalloc char[providerId.Length];
        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < providerId.Length; i++)
            buffer[i] = Array.IndexOf(invalid, providerId[i]) >= 0 ? '_' : providerId[i];
        return new string(buffer);
    }

    private static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim();
        if (normalized.Length == 0)
            return ".img";
        if (!normalized.StartsWith('.'))
            normalized = "." + normalized;
        return normalized.ToLowerInvariant();
    }

    private void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not remove a staged media file: {path}", ex);
        }
    }
}
