using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperRequestCoordinatorTests
{
    [Fact]
    public async Task Coordinator_StartsAtOneRequest_ThenHonorsAccountConcurrencyWithinSafetyCap()
    {
        var coordinator = new ScreenScraperRequestCoordinator(safetyMaximumConcurrency: 4);
        var first = await coordinator.EnterAsync(CancellationToken.None);
        var queued = coordinator.EnterAsync(CancellationToken.None);
        await Task.Delay(25);
        Assert.False(queued.IsCompleted);
        first.Lease!.Dispose();
        (await queued).Lease!.Dispose();

        coordinator.ObserveQuota(new ScreenScraperQuota(10, 1, 1000, 0, 50, null));
        Assert.Equal(4, coordinator.MaximumConcurrency);
        var admissions = await Task.WhenAll(
            coordinator.EnterAsync(CancellationToken.None),
            coordinator.EnterAsync(CancellationToken.None),
            coordinator.EnterAsync(CancellationToken.None),
            coordinator.EnterAsync(CancellationToken.None));
        var fifth = coordinator.EnterAsync(CancellationToken.None);
        await Task.Delay(25);
        Assert.False(fifth.IsCompleted);

        admissions[0].Lease!.Dispose();
        var admittedFifth = await fifth;
        Assert.NotNull(admittedFifth.Lease);
        admittedFifth.Lease.Dispose();
        foreach (var admission in admissions.Skip(1))
            admission.Lease!.Dispose();
    }

    [Fact]
    public async Task Coordinator_DeniesKnownDailyQuotaBeforeOpeningARequestSlot()
    {
        var coordinator = new ScreenScraperRequestCoordinator();
        coordinator.ObserveQuota(new ScreenScraperQuota(3, 100, 100, 1, 50, null));

        var admission = await coordinator.EnterAsync(CancellationToken.None);

        Assert.Equal(ScreenScraperRequestStatus.DailyQuotaExceeded, admission.Status);
        Assert.Null(admission.Lease);
    }

    [Fact]
    public async Task Coordinator_DeniesKnownFailedLookupQuota()
    {
        var coordinator = new ScreenScraperRequestCoordinator();
        coordinator.ObserveQuota(new ScreenScraperQuota(3, 10, 100, 5, 5, null));

        var admission = await coordinator.EnterAsync(CancellationToken.None);

        Assert.Equal(ScreenScraperRequestStatus.FailedLookupQuotaExceeded, admission.Status);
        Assert.Null(admission.Lease);
    }

    [Fact]
    public async Task QueuedAdmission_CanBeCancelledWithoutBlockingTheNextWaiter()
    {
        var coordinator = new ScreenScraperRequestCoordinator();
        var first = await coordinator.EnterAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var cancelled = coordinator.EnterAsync(cancellation.Token);
        var next = coordinator.EnterAsync(CancellationToken.None);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

        first.Lease!.Dispose();
        var nextAdmission = await next;

        Assert.NotNull(nextAdmission.Lease);
        nextAdmission.Lease.Dispose();
    }

    [Fact]
    public async Task DailyQuota_ResetsWhenTheLocalCalendarDayChanges()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var coordinator = new ScreenScraperRequestCoordinator(timeProvider: clock);
        coordinator.ObserveQuota(new ScreenScraperQuota(1, 100, 100, 5, 5, null));
        Assert.Null((await coordinator.EnterAsync(CancellationToken.None)).Lease);

        clock.Advance(TimeSpan.FromDays(1));
        var nextDay = await coordinator.EnterAsync(CancellationToken.None);

        Assert.NotNull(nextDay.Lease);
        nextDay.Lease.Dispose();
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
