using EmuShelf.Core.Metadata.ScreenScraper;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

/// <summary>
/// One process-wide admission gate for ScreenScraper metadata requests. It begins conservatively
/// at one request, then honors the latest account concurrency and quota values up to EmuShelf's
/// own safety ceiling.
/// </summary>
public sealed class ScreenScraperRequestCoordinator
{
    private readonly object _sync = new();
    private readonly int _safetyMaximumConcurrency;
    private readonly TimeProvider _timeProvider;
    private readonly Queue<TaskCompletionSource> _waiters = new();
    private int _activeRequests;
    private int _maximumConcurrency = 1;
    private int _requestsToday;
    private int? _maximumRequestsPerDay;
    private int _failedRequestsToday;
    private int? _maximumFailedRequestsPerDay;
    private bool _dailyQuotaExhausted;
    private bool _failedQuotaExhausted;
    private DateTimeOffset _cooldownUntil;
    private ScreenScraperQuota? _latestQuota;
    private DateOnly _counterDate;

    public ScreenScraperRequestCoordinator(
        int safetyMaximumConcurrency = 4,
        TimeProvider? timeProvider = null)
    {
        if (safetyMaximumConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(safetyMaximumConcurrency));
        _safetyMaximumConcurrency = safetyMaximumConcurrency;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _counterDate = CurrentDate();
    }

    public int MaximumConcurrency
    {
        get
        {
            lock (_sync)
                return _maximumConcurrency;
        }
    }

    public ScreenScraperQuota? LatestQuota
    {
        get
        {
            lock (_sync)
                return _latestQuota;
        }
    }

    internal async Task<ScreenScraperRequestAdmission> EnterAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan cooldown;
            Task? waitForCapacity = null;
            TaskCompletionSource? capacityWaiter = null;
            lock (_sync)
            {
                ResetDailyCountersIfNeeded();
                var deniedStatus = GetDeniedStatus();
                if (deniedStatus is not null)
                    return new ScreenScraperRequestAdmission(deniedStatus.Value, null);

                cooldown = _cooldownUntil - _timeProvider.GetUtcNow();
                if (cooldown <= TimeSpan.Zero)
                {
                    if (_activeRequests < _maximumConcurrency)
                    {
                        _activeRequests++;
                        _requestsToday++;
                        return new ScreenScraperRequestAdmission(
                            ScreenScraperRequestStatus.Success,
                            new Lease(this));
                    }

                    capacityWaiter = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _waiters.Enqueue(capacityWaiter);
                    waitForCapacity = capacityWaiter.Task;
                }
            }

            if (cooldown > TimeSpan.Zero)
                await Task.Delay(cooldown, _timeProvider, cancellationToken);
            else
            {
                using var registration = cancellationToken.Register(
                    static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                    capacityWaiter);
                await waitForCapacity!;
            }
        }
    }

    internal void ObserveQuota(ScreenScraperQuota? quota)
    {
        if (quota is null)
            return;
        lock (_sync)
        {
            ResetDailyCountersIfNeeded();
            _latestQuota = quota;
            if (quota.MaxThreads is > 0)
                _maximumConcurrency = Math.Clamp(quota.MaxThreads.Value, 1, _safetyMaximumConcurrency);
            if (quota.RequestsToday is >= 0)
                _requestsToday = Math.Max(_requestsToday, quota.RequestsToday.Value);
            if (quota.MaxRequestsPerDay is > 0)
                _maximumRequestsPerDay = quota.MaxRequestsPerDay;
            if (quota.FailedRequestsToday is >= 0)
                _failedRequestsToday = Math.Max(_failedRequestsToday, quota.FailedRequestsToday.Value);
            if (quota.MaxFailedRequestsPerDay is > 0)
                _maximumFailedRequestsPerDay = quota.MaxFailedRequestsPerDay;
            WakeWaiters();
        }
    }

    internal void ObserveStatus(
        ScreenScraperRequestStatus status,
        TimeSpan? retryAfter = null)
    {
        lock (_sync)
        {
            ResetDailyCountersIfNeeded();
            switch (status)
            {
                case ScreenScraperRequestStatus.NotFound:
                    _failedRequestsToday++;
                    break;
                case ScreenScraperRequestStatus.DailyQuotaExceeded:
                    _dailyQuotaExhausted = true;
                    break;
                case ScreenScraperRequestStatus.FailedLookupQuotaExceeded:
                    _failedQuotaExhausted = true;
                    break;
                case ScreenScraperRequestStatus.RateLimited:
                    var delay = retryAfter is { } specifiedDelay && specifiedDelay > TimeSpan.Zero
                        ? specifiedDelay
                        : TimeSpan.FromSeconds(1);
                    var proposed = _timeProvider.GetUtcNow() + delay;
                    if (proposed > _cooldownUntil)
                        _cooldownUntil = proposed;
                    break;
            }
        }
    }

    private ScreenScraperRequestStatus? GetDeniedStatus()
    {
        if (_dailyQuotaExhausted ||
            (_maximumRequestsPerDay is { } maxRequests && _requestsToday >= maxRequests))
        {
            return ScreenScraperRequestStatus.DailyQuotaExceeded;
        }
        if (_failedQuotaExhausted ||
            (_maximumFailedRequestsPerDay is { } maxFailed && _failedRequestsToday >= maxFailed))
        {
            return ScreenScraperRequestStatus.FailedLookupQuotaExceeded;
        }
        return null;
    }

    private void ResetDailyCountersIfNeeded()
    {
        var currentDate = CurrentDate();
        if (currentDate == _counterDate)
            return;
        _counterDate = currentDate;
        _requestsToday = 0;
        _failedRequestsToday = 0;
        _dailyQuotaExhausted = false;
        _failedQuotaExhausted = false;
        _latestQuota = null;
    }

    private DateOnly CurrentDate() =>
        DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);

    private void Release()
    {
        lock (_sync)
        {
            _activeRequests--;
            WakeWaiters();
        }
    }

    private void WakeWaiters()
    {
        var available = Math.Max(0, _maximumConcurrency - _activeRequests);
        while (_waiters.Count > 0 && available > 0)
        {
            var waiter = _waiters.Dequeue();
            if (waiter.TrySetResult())
                available--;
        }
    }

    internal sealed class Lease(ScreenScraperRequestCoordinator owner) : IDisposable
    {
        private ScreenScraperRequestCoordinator? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}

internal sealed record ScreenScraperRequestAdmission(
    ScreenScraperRequestStatus Status,
    ScreenScraperRequestCoordinator.Lease? Lease);
