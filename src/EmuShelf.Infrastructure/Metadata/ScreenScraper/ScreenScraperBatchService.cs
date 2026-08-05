using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

/// <summary>
/// Runs a hash/serial/file-name-only batch scrape (file name for arcade sets). Games are processed one
/// at a time; the shared request coordinator already paces API calls to the account's concurrency and
/// quota, so a sequential loop cannot overshoot. The batch never title-searches (that stays a manual,
/// single-game action) and halts as soon as the provider reports quota exhaustion, leaving finished
/// work intact.
/// </summary>
public sealed class ScreenScraperBatchService : IScreenScraperBatchService
{
    private readonly IScreenScraperPreviewService _preview;
    private readonly IGameScrapeApplicationService _apply;
    private readonly IGameMetadataStore _games;
    private readonly IAppLogger _logger;

    public ScreenScraperBatchService(
        IScreenScraperPreviewService preview,
        IGameScrapeApplicationService apply,
        IGameMetadataStore games,
        IAppLogger? logger = null)
    {
        _preview = preview;
        _apply = apply;
        _games = games;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<GameScrapeBatchSummary> RunAsync(
        IReadOnlyList<long> gameIds,
        ScreenScraperSettings settings,
        GameMetadataApplyMode mode,
        IReadOnlySet<GameMetadataField>? includeFields,
        IReadOnlySet<GameMediaKind>? includeMedia,
        IProgress<GameScrapeBatchProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameIds);
        var results = new List<GameScrapeBatchItemResult>();
        var total = gameIds.Count;
        var stopReason = GameScrapeBatchStopReason.Completed;

        foreach (var gameId in gameIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                stopReason = GameScrapeBatchStopReason.Cancelled;
                break;
            }

            var title = await Task.Run(() => _games.GetGame(gameId)?.Title, cancellationToken)
                ?? gameId.ToString();
            progress?.Report(new GameScrapeBatchProgress(results.Count, total, title));

            ScreenScraperPreviewResult preview;
            try
            {
                // The batch start itself is the consent to read bytes for a hash.
                preview = await _preview.PreviewAsync(gameId, settings, allowFingerprinting: true, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopReason = GameScrapeBatchStopReason.Cancelled;
                break;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Batch scrape preview failed for game {gameId}.", ex);
                results.Add(new GameScrapeBatchItemResult(
                    gameId, title, GameScrapeBatchOutcome.Failed, Error: ex.Message));
                progress?.Report(new GameScrapeBatchProgress(results.Count, total, title, results[^1]));
                continue;
            }

            // Fail-safe stops: quota/connection problems end the run rather than churning through it.
            if (TryGetStopReason(preview, out var earlyStop))
            {
                stopReason = earlyStop;
                break;
            }

            var item = preview.IsSuccess
                ? await ApplyOneAsync(gameId, title, preview.Preview!, mode, includeFields, includeMedia, cancellationToken)
                : new GameScrapeBatchItemResult(gameId, title, MapMissOutcome(preview), Error: preview.Error);

            results.Add(item);
            progress?.Report(new GameScrapeBatchProgress(results.Count, total, title, item));
        }

        return new GameScrapeBatchSummary(total, stopReason, results);
    }

    private async Task<GameScrapeBatchItemResult> ApplyOneAsync(
        long gameId,
        string title,
        ScreenScraperGamePreview preview,
        GameMetadataApplyMode mode,
        IReadOnlySet<GameMetadataField>? includeFields,
        IReadOnlySet<GameMediaKind>? includeMedia,
        CancellationToken cancellationToken)
    {
        var request = ScreenScraperApplyMapper.BuildRequest(preview, mode, includeFields, includeMedia);
        var applyResult = await _apply.ApplyAsync(request, cancellationToken);
        var outcome = applyResult.MetadataApplied > 0 || applyResult.MediaImported > 0
            ? GameScrapeBatchOutcome.Applied
            : GameScrapeBatchOutcome.NothingToApply;
        return new GameScrapeBatchItemResult(
            gameId, title, outcome, applyResult.MetadataApplied, applyResult.MediaImported);
    }

    private static bool TryGetStopReason(ScreenScraperPreviewResult preview, out GameScrapeBatchStopReason reason)
    {
        reason = preview.Status switch
        {
            ScreenScraperPreviewStatus.NotConnected => GameScrapeBatchStopReason.NotConnected,
            ScreenScraperPreviewStatus.ProviderDisabled => GameScrapeBatchStopReason.ProviderDisabled,
            ScreenScraperPreviewStatus.ProviderFailure => preview.RequestStatus switch
            {
                ScreenScraperRequestStatus.DailyQuotaExceeded or
                    ScreenScraperRequestStatus.FailedLookupQuotaExceeded => GameScrapeBatchStopReason.QuotaExhausted,
                ScreenScraperRequestStatus.RateLimited => GameScrapeBatchStopReason.RateLimited,
                ScreenScraperRequestStatus.AuthenticationFailed => GameScrapeBatchStopReason.NotConnected,
                _ => GameScrapeBatchStopReason.Completed,
            },
            _ => GameScrapeBatchStopReason.Completed,
        };
        return reason != GameScrapeBatchStopReason.Completed;
    }

    private static GameScrapeBatchOutcome MapMissOutcome(ScreenScraperPreviewResult preview) => preview.Status switch
    {
        ScreenScraperPreviewStatus.ProviderFailure when
            preview.RequestStatus == ScreenScraperRequestStatus.NotFound => GameScrapeBatchOutcome.NoMatch,
        ScreenScraperPreviewStatus.UnsupportedSystem or
            ScreenScraperPreviewStatus.UnsupportedFormat or
            ScreenScraperPreviewStatus.FingerprintConsentRequired => GameScrapeBatchOutcome.Unsupported,
        ScreenScraperPreviewStatus.SourceMissing or
            ScreenScraperPreviewStatus.SourceChanged => GameScrapeBatchOutcome.SourceProblem,
        _ => GameScrapeBatchOutcome.Failed,
    };
}
