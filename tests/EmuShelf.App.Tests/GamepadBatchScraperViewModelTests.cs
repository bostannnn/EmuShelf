using EmuShelf.App.ViewModels;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Tests;

public sealed class GamepadBatchScraperViewModelTests
{
    [Fact]
    public void Configuring_LandsOnStart_AndUpReachesTheReplaceToggle()
    {
        var vm = new GamepadBatchScraperViewModel(
            new GameBatchScraperViewModel([1, 2], "PlayStation 2", new FakeBatch(), Enabled()));

        // The common case (accept defaults, scrape) is one A press away.
        Assert.Equal(GamepadBatchScraperTargetKind.Start, vm.FocusedKind);
        Assert.True(vm.IsStartFocused);

        vm.MoveFocus(-1);
        Assert.Equal(GamepadBatchScraperTargetKind.RefreshToggle, vm.FocusedKind);
        Assert.True(vm.IsRefreshFocused);
        Assert.False(vm.IsStartFocused);
    }

    [Fact]
    public void ActivatingTheReplaceToggle_FlipsTheBatchOption()
    {
        var vm = new GamepadBatchScraperViewModel(
            new GameBatchScraperViewModel([1], "PSP", new FakeBatch(), Enabled()));
        vm.MoveFocus(-1); // onto the replace-values toggle

        Assert.False(vm.Batch.RefreshOwnedValues);
        vm.Activate();
        Assert.True(vm.Batch.RefreshOwnedValues);
    }

    [Fact]
    public async Task WhenTheRunFinishes_FocusMovesToClose_AndActivatingItRequestsClose()
    {
        var batch = new GameBatchScraperViewModel([1], "PSP", new FakeBatch { Result = Applied(1) }, Enabled());
        var vm = new GamepadBatchScraperViewModel(batch);
        var closed = false;
        batch.CloseRequested += () => closed = true;

        await batch.StartCommand.ExecuteAsync(null);

        Assert.Equal(GameBatchScraperState.Done, batch.State);
        Assert.Equal(GamepadBatchScraperTargetKind.Close, vm.FocusedKind);
        Assert.True(vm.IsCloseFocused);

        vm.Activate();
        Assert.True(closed);
    }

    private static ScreenScraperSettings Enabled() => new() { Enabled = true };

    private static GameScrapeBatchSummary Applied(int count) => new(
        count,
        GameScrapeBatchStopReason.Completed,
        [.. Enumerable.Range(1, count).Select(id =>
            new GameScrapeBatchItemResult(id, $"g{id}", GameScrapeBatchOutcome.Applied, 3, 1))]);

    private sealed class FakeBatch : IScreenScraperBatchService
    {
        public GameScrapeBatchSummary Result { get; set; } = new(0, GameScrapeBatchStopReason.Completed, []);

        public Task<GameScrapeBatchSummary> RunAsync(
            IReadOnlyList<long> gameIds,
            ScreenScraperSettings settings,
            GameMetadataApplyMode mode,
            IReadOnlySet<GameMetadataField>? includeFields,
            IReadOnlySet<GameMediaKind>? includeMedia,
            IProgress<GameScrapeBatchProgress>? progress,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new GameScrapeBatchProgress(gameIds.Count, gameIds.Count, "last"));
            return Task.FromResult(Result);
        }
    }
}
