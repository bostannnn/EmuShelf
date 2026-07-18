using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Services;

public sealed record RetroAchievementsProgressRefreshSummary(
    int RequestedGames,
    int UpdatedGames,
    RetroAchievementsRequestStatus Status);

/// <summary>
/// Refreshes account-scoped progress summaries for the RA games linked to the local library and
/// caches them so the library and popup stay useful offline. Requests are split into batches no
/// larger than <see cref="RetroAchievementsApi.MaxUserProgressBatchSize"/>. On any request failure
/// it stops and reports the reason, keeping whatever is already cached.
/// </summary>
public interface IRetroAchievementsProgressService
{
    Task<RetroAchievementsProgressRefreshSummary> RefreshAllAsync(
        RetroAchievementsCredentials credentials,
        CancellationToken cancellationToken = default);

    /// <summary>Drops all cached progress (e.g. when the account disconnects).</summary>
    void Clear();
}

public sealed class RetroAchievementsProgressService : IRetroAchievementsProgressService
{
    private readonly IRetroAchievementsProgressStore _store;
    private readonly IRetroAchievementsClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _worker = new(1, 1);

    public RetroAchievementsProgressService(
        IRetroAchievementsProgressStore store,
        IRetroAchievementsClient client,
        TimeProvider? timeProvider = null,
        IAppLogger? logger = null)
    {
        _store = store;
        _client = client;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<RetroAchievementsProgressRefreshSummary> RefreshAllAsync(
        RetroAchievementsCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        await _worker.WaitAsync(cancellationToken);
        try
        {
            var ids = _store.GetLinkedRetroAchievementsGameIds();
            if (ids.Count == 0)
                return new RetroAchievementsProgressRefreshSummary(
                    0, 0, RetroAchievementsRequestStatus.Success);

            var refreshedAt = _timeProvider.GetUtcNow();
            var updated = 0;
            var status = RetroAchievementsRequestStatus.Success;

            for (var offset = 0; offset < ids.Count; offset += RetroAchievementsApi.MaxUserProgressBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = ids
                    .Skip(offset)
                    .Take(RetroAchievementsApi.MaxUserProgressBatchSize)
                    .ToArray();

                var response = await _client.GetUserProgressAsync(credentials, batch, cancellationToken);
                if (!response.IsSuccess)
                {
                    // Keep whatever is already cached; stop and report the reason.
                    status = response.Status;
                    break;
                }

                foreach (var progress in response.Value!)
                {
                    _store.SaveProgress(progress, refreshedAt);
                    updated++;
                }
            }

            _logger.Information(
                $"RetroAchievements progress refresh: updated {updated} of {ids.Count} games ({status}).");
            return new RetroAchievementsProgressRefreshSummary(ids.Count, updated, status);
        }
        finally
        {
            _worker.Release();
        }
    }

    public void Clear() => _store.ClearProgress();
}
