using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Services;

/// <param name="RequestedGames">Linked RA games the refresh asked about.</param>
/// <param name="UpdatedGames">Games whose current progress the server returned.</param>
/// <param name="ChangedGames">Of those, games whose numbers differed from the cache and were saved.
/// Zero means the library already shows the latest progress and nothing needs redrawing.</param>
public sealed record RetroAchievementsProgressRefreshSummary(
    int RequestedGames,
    int UpdatedGames,
    int ChangedGames,
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
        CancellationToken cancellationToken = default,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress = null);

    /// <summary>Drops all cached progress (e.g. when the account disconnects).</summary>
    void Clear();
}

public sealed class RetroAchievementsProgressService : IRetroAchievementsProgressService
{
    private readonly IRetroAchievementsProgressStore _store;
    private readonly IRetroAchievementsClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger _logger;
    private readonly object _refreshGate = new();
    private Task<RetroAchievementsProgressRefreshSummary>? _inFlightRefresh;

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
        CancellationToken cancellationToken = default,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
    {
        Task<RetroAchievementsProgressRefreshSummary> refresh;
        lock (_refreshGate)
        {
            // A startup check, an import, and a post-session follow-up can all discover the
            // same current account. They share the existing cache refresh instead of queuing
            // duplicate batches behind one another. Individual callers may still cancel only
            // their wait; the shared cache operation completes for the other callers.
            refresh = _inFlightRefresh is { IsCompleted: false }
                ? _inFlightRefresh
                : _inFlightRefresh = RefreshCoreAsync(credentials, progress);
        }

        try
        {
            return await refresh.WaitAsync(cancellationToken);
        }
        finally
        {
            if (refresh.IsCompleted)
            {
                lock (_refreshGate)
                {
                    if (ReferenceEquals(_inFlightRefresh, refresh))
                        _inFlightRefresh = null;
                }
            }
        }
    }

    private async Task<RetroAchievementsProgressRefreshSummary> RefreshCoreAsync(
        RetroAchievementsCredentials credentials,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress)
    {
        // The underlying work is intentionally independent of one UI caller's cancellation;
        // it is shared cache work and callers use WaitAsync above to stop awaiting it.
        var sharedCancellation = CancellationToken.None;
        var ids = _store.GetLinkedRetroAchievementsGameIds();
        progress?.Report(new RetroAchievementsLibrarySyncProgress(
            RetroAchievementsLibrarySyncPhase.RefreshingProgress,
            Completed: 0,
            Total: ids.Count));
        if (ids.Count == 0)
            return new RetroAchievementsProgressRefreshSummary(
                0, 0, 0, RetroAchievementsRequestStatus.Success);

        // Between two launches usually only the games just played have moved, so compare with the
        // cache and write only what differs. A launch with nothing new then writes nothing at all.
        var cached = _store.GetAllProgress();
        var refreshedAt = _timeProvider.GetUtcNow();
        var updated = 0;
        var changed = 0;
        var completed = 0;
        var status = RetroAchievementsRequestStatus.Success;

        for (var offset = 0; offset < ids.Count; offset += RetroAchievementsApi.MaxUserProgressBatchSize)
        {
            var batch = ids
                .Skip(offset)
                .Take(RetroAchievementsApi.MaxUserProgressBatchSize)
                .ToArray();

            var response = await _client.GetUserProgressAsync(
                credentials, batch, sharedCancellation);
            if (!response.IsSuccess)
            {
                // Keep whatever is already cached; stop and report the reason.
                status = response.Status;
                break;
            }

            var differing = response.Value!
                .Where(game => !cached.TryGetValue(game.GameId, out var snapshot) || snapshot.Progress != game)
                .ToArray();
            // Changed games in one write, never one per game (see the store's batch overload).
            if (differing.Length > 0)
                _store.SaveProgress(differing, refreshedAt);
            updated += response.Value!.Count;
            changed += differing.Length;

            completed += batch.Length;
            progress?.Report(new RetroAchievementsLibrarySyncProgress(
                RetroAchievementsLibrarySyncPhase.RefreshingProgress,
                Completed: completed,
                Total: ids.Count));
        }

        if (status == RetroAchievementsRequestStatus.Success)
            _store.SaveLastSummaryRefreshAt(_timeProvider.GetUtcNow());

        _logger.Information(
            $"RetroAchievements progress refresh: updated {updated} of {ids.Count} games, " +
            $"{changed} changed ({status}).");
        return new RetroAchievementsProgressRefreshSummary(ids.Count, updated, changed, status);
    }

    public void Clear()
    {
        _store.ClearProgress();
        lock (_refreshGate)
            _inFlightRefresh = null;
    }
}
