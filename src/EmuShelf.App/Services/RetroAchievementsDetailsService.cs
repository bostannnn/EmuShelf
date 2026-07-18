using System.Collections.Concurrent;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Services;

/// <summary>
/// Loads full achievement detail only for an opened game. The synchronous cache read lets a
/// popup render immediately; callers decide whether its five-minute TTL warrants an asynchronous
/// refresh. No gameplay polling is performed here.
/// </summary>
public interface IRetroAchievementsDetailsService
{
    RetroAchievementsDetailsSnapshot? GetCached(int retroAchievementsGameId);

    Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshAsync(
        RetroAchievementsCredentials credentials,
        int retroAchievementsGameId,
        CancellationToken cancellationToken = default);

    void Clear();
}

public sealed class RetroAchievementsDetailsService : IRetroAchievementsDetailsService
{
    private readonly IRetroAchievementsDetailsStore _detailsStore;
    private readonly IRetroAchievementsProgressStore _progressStore;
    private readonly IRetroAchievementsClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger _logger;
    private readonly object _cacheGate = new();
    private readonly ConcurrentDictionary<int, Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>>>
        _inFlight = new();
    private int _cacheGeneration;

    public RetroAchievementsDetailsService(
        IRetroAchievementsDetailsStore detailsStore,
        IRetroAchievementsProgressStore progressStore,
        IRetroAchievementsClient client,
        TimeProvider? timeProvider = null,
        IAppLogger? logger = null)
    {
        _detailsStore = detailsStore;
        _progressStore = progressStore;
        _client = client;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public RetroAchievementsDetailsSnapshot? GetCached(int retroAchievementsGameId) =>
        _detailsStore.GetDetails(retroAchievementsGameId);

    public async Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshAsync(
        RetroAchievementsCredentials credentials,
        int retroAchievementsGameId,
        CancellationToken cancellationToken = default)
    {
        if (retroAchievementsGameId <= 0)
            throw new ArgumentOutOfRangeException(nameof(retroAchievementsGameId));

        // Multiple views of the same game share a single outbound request. The request itself is
        // intentionally uncancelled once started, so closing one popup cannot cancel another
        // popup's usable cache refresh; each caller can still stop awaiting it.
        var request = _inFlight.GetOrAdd(
            retroAchievementsGameId,
            id => RefreshCoreAsync(credentials, id));
        try
        {
            return await request.WaitAsync(cancellationToken);
        }
        finally
        {
            if (request.IsCompleted)
                _inFlight.TryRemove(new KeyValuePair<int, Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>>>(
                    retroAchievementsGameId,
                    request));
        }
    }

    public void Clear()
    {
        // A detail request that started under the old account may finish after disconnect. The
        // generation check in RefreshCoreAsync prevents it from repopulating account-scoped data.
        lock (_cacheGate)
        {
            _cacheGeneration++;
            _detailsStore.ClearDetails();
            // A newly connected account must never join an old account's pending request. The
            // old request may finish, but its captured generation blocks persistence below.
            _inFlight.Clear();
        }
    }

    private async Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshCoreAsync(
        RetroAchievementsCredentials credentials,
        int retroAchievementsGameId)
    {
        try
        {
            int requestGeneration;
            lock (_cacheGate)
                requestGeneration = _cacheGeneration;

            var response = await _client.GetGameDetailsAsync(credentials, retroAchievementsGameId);
            if (!response.IsSuccess)
                return RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Failure(
                    response.Status,
                    response.RetryAfter,
                    response.Error);

            var refreshedAt = _timeProvider.GetUtcNow();
            var details = response.Value!;
            lock (_cacheGate)
            {
                if (requestGeneration == _cacheGeneration)
                {
                    _detailsStore.SaveDetails(details, refreshedAt);
                    // A manual/detail refresh is also a valid fresh summary for the library. It
                    // still occurs only while a user has the details open, never because
                    // gameplay is running.
                    _progressStore.SaveProgress(
                        new RetroAchievementsGameProgress(
                            details.GameId,
                            details.TotalAchievements,
                            details.UnlockedAchievements,
                            details.UnlockedHardcoreAchievements),
                        refreshedAt);
                }
            }
            return RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Success(
                new RetroAchievementsDetailsSnapshot(details, refreshedAt));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(
                $"RetroAchievements details for game {retroAchievementsGameId} could not be cached.",
                ex);
            return RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Failure(
                RetroAchievementsRequestStatus.ServerError);
        }
    }
}
