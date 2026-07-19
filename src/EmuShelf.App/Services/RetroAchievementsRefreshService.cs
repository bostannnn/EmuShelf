using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Services;

/// <summary>
/// Applies the refresh policy above the individual API requests: a complete summary refresh is
/// bounded to startup freshness, while an emulator exit gets exactly one delayed detail refresh
/// for the launched game. It never observes or polls a running emulator.
/// </summary>
public interface IRetroAchievementsRefreshService
{
    /// <summary>
    /// Refreshes all linked progress only when the last complete summary sync is stale. Returns
    /// null when no call is needed or the account cannot make read-only requests.
    /// </summary>
    Task<RetroAchievementsProgressRefreshSummary?> RefreshSummaryAtStartupIfStaleAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits briefly after a tracked emulator exit, then refreshes the launched game's detail
    /// once. A successful detail response also updates the library summary cache.
    /// </summary>
    Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>?> RefreshAfterTrackedExitAsync(
        int retroAchievementsGameId,
        CancellationToken cancellationToken = default);
}

public sealed class RetroAchievementsRefreshService : IRetroAchievementsRefreshService
{
    public static readonly TimeSpan SummaryRefreshAge = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan PostExitSettleDelay = TimeSpan.FromSeconds(8);

    private readonly IRetroAchievementsAccountService _account;
    private readonly IRetroAchievementsProgressStore _progressStore;
    private readonly IRetroAchievementsProgressService _progress;
    private readonly IRetroAchievementsDetailsService _details;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly IAppLogger _logger;

    public RetroAchievementsRefreshService(
        IRetroAchievementsAccountService account,
        IRetroAchievementsProgressStore progressStore,
        IRetroAchievementsProgressService progress,
        IRetroAchievementsDetailsService details,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        IAppLogger? logger = null)
    {
        _account = account;
        _progressStore = progressStore;
        _progress = progress;
        _details = details;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, _timeProvider, token));
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<RetroAchievementsProgressRefreshSummary?> RefreshSummaryAtStartupIfStaleAsync(
        CancellationToken cancellationToken = default)
    {
        var credentials = _account.CurrentCredentials;
        if (credentials is null || _progressStore.GetLinkedRetroAchievementsGameIds().Count == 0)
            return null;

        var lastRefresh = _progressStore.GetLastSummaryRefreshAt();
        if (lastRefresh is { } refreshed && _timeProvider.GetUtcNow() - refreshed <= SummaryRefreshAge)
            return null;

        return await _progress.RefreshAllAsync(credentials, cancellationToken);
    }

    public async Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>?> RefreshAfterTrackedExitAsync(
        int retroAchievementsGameId,
        CancellationToken cancellationToken = default)
    {
        if (retroAchievementsGameId <= 0)
            return null;

        var credentials = _account.CurrentCredentials;
        if (credentials is null)
            return null;

        await _delay(PostExitSettleDelay, cancellationToken);

        // A disconnect/reconnect while the emulator was settling must not refresh or persist
        // data with either the old or a newly selected account.
        if (!Equals(credentials, _account.CurrentCredentials))
            return null;

        var response = await _details.RefreshAsync(
            credentials,
            retroAchievementsGameId,
            cancellationToken,
            manual: false);
        if (!response.IsSuccess)
        {
            _logger.Information(
                $"RetroAchievements post-exit refresh for game {retroAchievementsGameId} " +
                $"was unavailable ({response.Status}).");
        }
        return response;
    }
}
