using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Services;

/// <summary>Whether an RA request was caused by background policy or an explicit user action.</summary>
public enum RetroAchievementsRequestMode
{
    Automatic,
    Manual,
}

/// <summary>
/// Serializes the application's authenticated RetroAchievements API calls. It deliberately
/// wraps the client rather than retrying callers: a caller receives the original result and can
/// keep its cache, while later work observes the server's cooldown.
/// </summary>
public sealed class RetroAchievementsRequestCoordinator
{
    public static readonly TimeSpan MinimumAutomaticInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan InitialServerBackoff = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumServerBackoff = TimeSpan.FromMinutes(1);

    private readonly IRetroAchievementsClient _inner;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _jitter;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _requestWorker = new(1, 1);
    private readonly object _inFlightGate = new();
    private readonly Dictionary<RequestKey, Task<RetroAchievementsResponse<object>>> _inFlight = [];

    // Accessed only while _requestWorker is held.
    private DateTimeOffset _nextAutomaticRequestAt = DateTimeOffset.MinValue;
    private DateTimeOffset _serverCooldownUntil = DateTimeOffset.MinValue;
    private int _consecutiveServerFailures;

    public RetroAchievementsRequestCoordinator(
        IRetroAchievementsClient inner,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<double>? jitter = null,
        IAppLogger? logger = null)
    {
        _inner = inner;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delay = delay ?? ((duration, token) => Task.Delay(duration, _timeProvider, token));
        _jitter = jitter ?? Random.Shared.NextDouble;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>
    /// Creates a view of the same shared coordinator. The mode controls only the one-second
    /// automatic pacing; manual actions still serialize and always honor server cooldowns.
    /// </summary>
    public IRetroAchievementsClient CreateClient(RetroAchievementsRequestMode mode) =>
        new CoordinatedClient(this, mode);

    private async Task<RetroAchievementsResponse<T>> ExecuteAsync<T>(
        RequestKey key,
        RetroAchievementsRequestMode mode,
        Func<CancellationToken, Task<RetroAchievementsResponse<T>>> operation,
        CancellationToken cancellationToken)
    {
        Task<RetroAchievementsResponse<object>> shared;
        lock (_inFlightGate)
        {
            if (!_inFlight.TryGetValue(key, out shared!))
            {
                shared = ExecuteCoreAsync(mode, operation);
                _inFlight.Add(key, shared);
                _ = RemoveWhenFinishedAsync(key, shared);
            }
        }

        var response = await shared.WaitAsync(cancellationToken);
        return response.IsSuccess
            ? RetroAchievementsResponse<T>.Success((T)response.Value!)
            : RetroAchievementsResponse<T>.Failure(
                response.Status, response.RetryAfter, response.Error);
    }

    private async Task RemoveWhenFinishedAsync(
        RequestKey key,
        Task<RetroAchievementsResponse<object>> request)
    {
        try
        {
            await request;
        }
        catch
        {
            // The caller receives an unexpected implementation error. This continuation only
            // removes the key so it cannot pin credentials or a failed task in memory.
        }
        finally
        {
            lock (_inFlightGate)
            {
                if (_inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                    _inFlight.Remove(key);
            }
        }
    }

    private async Task<RetroAchievementsResponse<object>> ExecuteCoreAsync<T>(
        RetroAchievementsRequestMode mode,
        Func<CancellationToken, Task<RetroAchievementsResponse<T>>> operation)
    {
        // A shared cache request outlives one view's cancellation. Individual callers use
        // WaitAsync above to stop waiting without aborting a useful request for another caller.
        await _requestWorker.WaitAsync(CancellationToken.None);
        try
        {
            await WaitForPermitAsync(mode);
            var response = await operation(CancellationToken.None);
            ApplyResponseCooldown(response.Status, response.RetryAfter);
            return response.IsSuccess
                ? RetroAchievementsResponse<object>.Success(response.Value!)
                : RetroAchievementsResponse<object>.Failure(
                    response.Status, response.RetryAfter, response.Error);
        }
        finally
        {
            _requestWorker.Release();
        }
    }

    private async Task WaitForPermitAsync(RetroAchievementsRequestMode mode)
    {
        var now = _timeProvider.GetUtcNow();
        var permittedAt = _serverCooldownUntil;
        if (mode == RetroAchievementsRequestMode.Automatic &&
            _nextAutomaticRequestAt > permittedAt)
        {
            permittedAt = _nextAutomaticRequestAt;
        }

        if (permittedAt > now)
            await _delay(permittedAt - now, CancellationToken.None);

        if (mode == RetroAchievementsRequestMode.Automatic)
            _nextAutomaticRequestAt = _timeProvider.GetUtcNow() + MinimumAutomaticInterval;
    }

    private void ApplyResponseCooldown(
        RetroAchievementsRequestStatus status,
        TimeSpan? retryAfter)
    {
        if (status is not (RetroAchievementsRequestStatus.RateLimited or RetroAchievementsRequestStatus.ServerError))
        {
            if (status == RetroAchievementsRequestStatus.Success)
                _consecutiveServerFailures = 0;
            return;
        }

        TimeSpan delay;
        if (status == RetroAchievementsRequestStatus.RateLimited)
        {
            _consecutiveServerFailures = 0;
            // Retry-After is a lower bound, not a retry instruction. We add only positive
            // jitter, so a server-provided wait is always honored in full.
            delay = retryAfter is { } requested && requested > TimeSpan.Zero
                ? requested
                : DefaultRateLimitBackoff;
        }
        else
        {
            _consecutiveServerFailures = Math.Min(_consecutiveServerFailures + 1, 6);
            var multiplier = 1 << (_consecutiveServerFailures - 1);
            var ticks = Math.Min(
                InitialServerBackoff.Ticks * (long)multiplier,
                MaximumServerBackoff.Ticks);
            delay = TimeSpan.FromTicks(ticks);
        }

        var jitter = Math.Clamp(_jitter(), 0d, 1d);
        var jittered = delay + TimeSpan.FromTicks((long)(delay.Ticks * 0.25d * jitter));
        var until = _timeProvider.GetUtcNow() + jittered;
        if (until > _serverCooldownUntil)
            _serverCooldownUntil = until;

        _logger.Information(
            $"RetroAchievements requests paused for {jittered.TotalSeconds:F1}s after {status}.");
    }

    // Credentials remain only in this short-lived, in-memory key so a reconnect using a new key
    // cannot join an old in-flight request. Keys are never logged or persisted.
    private sealed record RequestKey(
        string Endpoint,
        RetroAchievementsCredentials Credentials,
        string Arguments);

    private sealed class CoordinatedClient(
        RetroAchievementsRequestCoordinator owner,
        RetroAchievementsRequestMode mode) : IRetroAchievementsClient
    {
        public Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default) =>
            owner.ExecuteAsync(
                new RequestKey("profile", credentials, string.Empty),
                mode,
                token => owner._inner.GetUserProfileAsync(credentials, token),
                cancellationToken);

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
            RetroAchievementsCredentials credentials,
            int consoleId,
            CancellationToken cancellationToken = default) =>
            owner.ExecuteAsync(
                new RequestKey("game-list", credentials, consoleId.ToString()),
                mode,
                token => owner._inner.GetGameListAsync(credentials, consoleId, token),
                cancellationToken);

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
            RetroAchievementsCredentials credentials,
            IReadOnlyList<int> gameIds,
            CancellationToken cancellationToken = default) =>
            owner.ExecuteAsync(
                new RequestKey(
                    "user-progress",
                    credentials,
                    string.Join(',', gameIds.OrderBy(gameId => gameId))),
                mode,
                token => owner._inner.GetUserProgressAsync(credentials, gameIds, token),
                cancellationToken);

        public Task<RetroAchievementsResponse<RetroAchievementsGameDetails>> GetGameDetailsAsync(
            RetroAchievementsCredentials credentials,
            int gameId,
            CancellationToken cancellationToken = default) =>
            owner.ExecuteAsync(
                new RequestKey("game-details", credentials, gameId.ToString()),
                mode,
                token => owner._inner.GetGameDetailsAsync(credentials, gameId, token),
                cancellationToken);
    }
}
