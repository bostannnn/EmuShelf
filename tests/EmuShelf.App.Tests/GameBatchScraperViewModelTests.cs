using EmuShelf.App.ViewModels;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Tests;

public class GameBatchScraperViewModelTests
{
    [Fact]
    public async Task Start_WithEverythingSelected_RunsFillMissing_AllFields_AllMedia()
    {
        var batch = new FakeBatch { Result = AppliedSummary(3) };
        var vm = new GameBatchScraperViewModel([1, 2, 3], "PlayStation 2", batch, Enabled());

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(GameBatchScraperState.Done, vm.State);
        Assert.Equal(GameMetadataApplyMode.FillMissing, batch.Mode);
        Assert.Null(batch.IncludeFields); // metadata on -> all fields
        Assert.Equal(9, batch.IncludeMedia!.Count);
        Assert.True(vm.AppliedChanges);
        Assert.Contains("3 scraped", vm.StatusMessage);
    }

    [Fact]
    public async Task Start_WithSelections_MapsToFieldAndMediaFilters_AndRefreshMode()
    {
        var batch = new FakeBatch { Result = AppliedSummary(1) };
        var vm = new GameBatchScraperViewModel([1], "PSP", batch, Enabled())
        {
            IncludeMetadata = false,
            IncludeScreenshot = false,
            IncludeWheel = false,
            IncludeFanart = false,
            IncludeTitleScreen = false,
            IncludeBoxBack = false,
            IncludeBoxSpine = false,
            IncludePhysicalMedia = false,
            IncludePhysicalMediaTexture = false,
            RefreshOwnedValues = true,
        };

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal(GameMetadataApplyMode.RefreshProviderOwned, batch.Mode);
        Assert.Empty(batch.IncludeFields!); // metadata off -> no fields
        Assert.Equal(GameMediaKind.BoxFront, Assert.Single(batch.IncludeMedia!));
    }

    [Fact]
    public async Task Done_SummaryReportsMixedOutcomes_AndEarlyStop()
    {
        var batch = new FakeBatch
        {
            Result = new GameScrapeBatchSummary(
                5,
                GameScrapeBatchStopReason.QuotaExhausted,
                [
                    new GameScrapeBatchItemResult(1, "a", GameScrapeBatchOutcome.Applied, 4, 2),
                    new GameScrapeBatchItemResult(2, "b", GameScrapeBatchOutcome.NoMatch),
                ]),
        };
        var vm = new GameBatchScraperViewModel([1, 2, 3, 4, 5], "GBA", batch, Enabled());

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Contains("Stopped — ScreenScraper quota reached", vm.StatusMessage);
        Assert.Contains("1 scraped", vm.StatusMessage);
        Assert.Contains("1 no match", vm.StatusMessage);
        Assert.Contains("3 not reached", vm.StatusMessage);
    }

    [Fact]
    public async Task Done_SummaryReportsAlreadyCompleteGames_InsteadOfHidingThem()
    {
        var batch = new FakeBatch
        {
            Result = new GameScrapeBatchSummary(
                4,
                GameScrapeBatchStopReason.Completed,
                [
                    new GameScrapeBatchItemResult(1, "a", GameScrapeBatchOutcome.Applied, 2, 1),
                    new GameScrapeBatchItemResult(2, "b", GameScrapeBatchOutcome.AlreadyScraped),
                    new GameScrapeBatchItemResult(3, "c", GameScrapeBatchOutcome.NothingToApply),
                    new GameScrapeBatchItemResult(4, "d", GameScrapeBatchOutcome.NoMatch),
                ]),
        };
        var vm = new GameBatchScraperViewModel([1, 2, 3, 4], "PS1", batch, Enabled());

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Contains("1 scraped", vm.StatusMessage);
        Assert.Contains("2 already complete", vm.StatusMessage);
        Assert.Contains("1 no match", vm.StatusMessage);
    }

    [Fact]
    public async Task Done_SummaryOfAllAlreadyComplete_DoesNotReadAsNothingScraped()
    {
        var batch = new FakeBatch
        {
            Result = new GameScrapeBatchSummary(
                2,
                GameScrapeBatchStopReason.Completed,
                [
                    new GameScrapeBatchItemResult(1, "a", GameScrapeBatchOutcome.AlreadyScraped),
                    new GameScrapeBatchItemResult(2, "b", GameScrapeBatchOutcome.AlreadyScraped),
                ]),
        };
        var vm = new GameBatchScraperViewModel([1, 2], "PS1", batch, Enabled());

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal("2 already complete.", vm.StatusMessage);
        Assert.DoesNotContain("0 scraped", vm.StatusMessage);
        Assert.False(vm.AppliedChanges);
    }

    [Fact]
    public async Task Done_SummaryOfAllFailed_ReadsAsFailed_NotZeroScraped()
    {
        var batch = new FakeBatch
        {
            Result = new GameScrapeBatchSummary(
                2,
                GameScrapeBatchStopReason.Completed,
                [
                    new GameScrapeBatchItemResult(1, "a", GameScrapeBatchOutcome.Failed),
                    new GameScrapeBatchItemResult(2, "b", GameScrapeBatchOutcome.Failed),
                ]),
        };
        var vm = new GameBatchScraperViewModel([1, 2], "PS1", batch, Enabled());

        await vm.StartCommand.ExecuteAsync(null);

        Assert.Equal("2 failed.", vm.StatusMessage);
        Assert.DoesNotContain("scraped", vm.StatusMessage);
        Assert.DoesNotContain("already complete", vm.StatusMessage);
    }

    [Fact]
    public async Task Cancel_WhileRunning_StopsTheRun_AndEndsCancelled()
    {
        var batch = new FakeBatch { BlockUntilCancelled = true };
        var vm = new GameBatchScraperViewModel([1, 2], "Wii", batch, Enabled());

        var run = vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(GameBatchScraperState.Running, vm.State);

        vm.CancelCommand.Execute(null);
        await run;

        Assert.Equal(GameBatchScraperState.Done, vm.State);
        Assert.Contains("Cancelled", vm.StatusMessage);
    }

    [Fact]
    public void Cancel_WhileConfiguring_ClosesTheWindow()
    {
        var vm = new GameBatchScraperViewModel([1], "PS1", new FakeBatch(), Enabled());
        var closed = false;
        vm.CloseRequested += () => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closed);
    }

    private static ScreenScraperSettings Enabled() => new() { Enabled = true };

    private static GameScrapeBatchSummary AppliedSummary(int count) => new(
        count,
        GameScrapeBatchStopReason.Completed,
        Enumerable.Range(1, count)
            .Select(i => new GameScrapeBatchItemResult(i, $"g{i}", GameScrapeBatchOutcome.Applied, 1, 0))
            .ToList());

    private sealed class FakeBatch : IScreenScraperBatchService
    {
        public bool BlockUntilCancelled { get; set; }

        public GameScrapeBatchSummary Result { get; set; } =
            new(0, GameScrapeBatchStopReason.Completed, []);

        public GameMetadataApplyMode Mode { get; private set; }

        public IReadOnlySet<GameMetadataField>? IncludeFields { get; private set; }

        public IReadOnlySet<GameMediaKind>? IncludeMedia { get; private set; }

        public async Task<GameScrapeBatchSummary> RunAsync(
            IReadOnlyList<long> gameIds,
            ScreenScraperSettings settings,
            GameMetadataApplyMode mode,
            IReadOnlySet<GameMetadataField>? includeFields,
            IReadOnlySet<GameMediaKind>? includeMedia,
            IProgress<GameScrapeBatchProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            Mode = mode;
            IncludeFields = includeFields;
            IncludeMedia = includeMedia;
            if (BlockUntilCancelled)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            progress?.Report(new GameScrapeBatchProgress(gameIds.Count, gameIds.Count, "last"));
            return Result;
        }
    }
}
