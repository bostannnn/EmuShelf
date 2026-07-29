using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Tests;

public class AchievementDetailsWindowTests
{
    [AvaloniaFact]
    public async Task CachedDetails_RenderInDisplayOrderWithSoftcoreAndHardcoreStates()
    {
        var refreshedAt = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var cached = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(
                1234, "Canonical title", 3, 2, 1,
                [
                    new RetroAchievementsAchievement(
                        9,
                        "Last",
                        "A deliberately long description that must use only its text column and never displace the points or lock state rail.",
                        3,
                        "000009",
                        3,
                        null,
                        null),
                    new RetroAchievementsAchievement(7, "First", "First description", 5, "000007", 1,
                        refreshedAt, refreshedAt),
                    new RetroAchievementsAchievement(8, "Second", "Second description", 10, "000008", 2,
                        refreshedAt, null),
                ]),
            refreshedAt);
        var viewModel = new AchievementDetailsViewModel(
            "Local game title",
            1234,
            new FakeDetailsService(),
            new FakeAccount(),
            cached: cached);
        var window = new AchievementDetailsWindow
        {
            DataContext = viewModel,
            RequestedThemeVariant = ThemeVariant.Dark,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var progress = window.FindControl<ProgressBar>("AchievementProgress");
            var list = window.FindControl<ItemsControl>("AchievementList");

            Assert.NotNull(progress);
            Assert.NotNull(list);
            Assert.Equal(2d, progress.Value);
            Assert.Equal(3d, progress.Maximum);
            Assert.Equal(["First", "Second", "Last"], viewModel.Achievements.Select(row => row.Title));
            Assert.Equal("Hardcore", viewModel.Achievements[0].UnlockStateText);
            Assert.Equal("Softcore", viewModel.Achievements[1].UnlockStateText);
            Assert.Equal("Locked", viewModel.Achievements[2].UnlockStateText);
            Assert.Equal("15 / 18 points", viewModel.PointsText);
            Assert.Contains("Last refreshed", viewModel.LastRefreshText);
            var cards = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("achievement-row"))
                .ToArray();
            var rewardPanels = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("achievement-reward-panel"))
                .ToArray();
            Assert.Equal(3, cards.Length);
            Assert.Equal(3, rewardPanels.Length);
            Assert.All(cards, card => Assert.True(card.Bounds.X > 0));
            Assert.All(cards, card => Assert.True(card.Bounds.Right < window.Bounds.Width));
            Assert.All(rewardPanels, panel => Assert.Equal(152, panel.Bounds.Width));
            Assert.All(rewardPanels, panel =>
                Assert.Equal(rewardPanels[0].Bounds.X, panel.Bounds.X));

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(660, 590), frame.PixelSize);
            var snapshotDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
            if (snapshotDirectory is not null)
            {
                Directory.CreateDirectory(snapshotDirectory);
                await using var output = File.Create(Path.Combine(
                    snapshotDirectory,
                    "achievement-details-window.png"));
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            viewModel.Dispose();
        }
    }

    [Fact]
    public async Task ManualRefresh_ReplacesCachedRowsEvenWhenTheyAreFresh()
    {
        var stale = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(
                1234, "Game", 1, 0, 0,
                [new RetroAchievementsAchievement(7, "Old", "", 1, "", 1, null, null)]),
            DateTimeOffset.UtcNow);
        var details = new FakeDetailsService
        {
            Response = new RetroAchievementsDetailsSnapshot(
                new RetroAchievementsGameDetails(
                    1234, "Game", 1, 1, 1,
                    [new RetroAchievementsAchievement(
                        7, "New", "", 1, "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)]),
                DateTimeOffset.UtcNow),
        };
        var viewModel = new AchievementDetailsViewModel(
            "Game", 1234, details, new FakeAccount(), cached: stale);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(1, details.RefreshCalls);
        Assert.Equal("New", Assert.Single(viewModel.Achievements).Title);
        viewModel.Dispose();
    }

    [AvaloniaFact]
    public async Task SharedDetailRefresh_UpdatesAnOpenPopup()
    {
        var refreshedAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var details = new FakeDetailsService();
        var viewModel = new AchievementDetailsViewModel(
            "Game",
            1234,
            details,
            new FakeAccount(),
            cached: new RetroAchievementsDetailsSnapshot(
                new RetroAchievementsGameDetails(
                    1234, "Game", 1, 0, 0,
                    [new RetroAchievementsAchievement(1, "Old", "", 5, "", 1, null, null)]),
                refreshedAt));

        details.Publish(new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(
                1234, "Game", 1, 1, 1,
                [new RetroAchievementsAchievement(1, "New", "", 5, "", 1, refreshedAt, refreshedAt)]),
            refreshedAt.AddMinutes(1)));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Equal("New", Assert.Single(viewModel.Achievements).Title);
        Assert.Equal(1, viewModel.UnlockedCount);
        viewModel.Dispose();
    }

    [Fact]
    public void EmptyState_ExplainsThatThisGamesDetailsHaveNotBeenCached()
    {
        var viewModel = new AchievementDetailsViewModel(
            "Game", 1234, new FakeDetailsService(), new FakeAccount(isConnected: false));

        Assert.Equal("No achievement details cached", viewModel.EmptyStateTitle);
        Assert.Equal(
            "Reconnect to load this game's achievement list. Once loaded, it will remain available offline.",
            viewModel.EmptyStateDescription);

        viewModel.Dispose();
    }

    [Fact]
    public void LoadedSnapshotWithoutRows_IsReportedAsAValidEmptyAchievementSet()
    {
        var cached = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(1234, "Game", 0, 0, 0, []),
            DateTimeOffset.UtcNow);
        var viewModel = new AchievementDetailsViewModel(
            "Game", 1234, new FakeDetailsService(), new FakeAccount(), cached: cached);

        Assert.True(viewModel.HasLoadedSnapshot);
        Assert.False(viewModel.HasAchievements);
        Assert.Equal("No achievements available", viewModel.EmptyStateTitle);
        Assert.Equal(
            "RetroAchievements did not return any achievements for this game.",
            viewModel.EmptyStateDescription);

        viewModel.Dispose();
    }

    [AvaloniaFact]
    public async Task UnexpectedRefreshFailure_LeavesAUsefulUncachedState()
    {
        var details = new FakeDetailsService
        {
            RefreshException = new InvalidOperationException("broken detail store"),
        };
        var logger = new RecordingLogger();
        var viewModel = new AchievementDetailsViewModel(
            "Game", 1234, details, new FakeAccount(), logger: logger);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsRefreshing);
        Assert.Equal(
            "Achievement details could not be loaded and no cached copy is available.",
            viewModel.StatusText);
        Assert.Equal("No achievement details cached", viewModel.EmptyStateTitle);
        Assert.IsType<InvalidOperationException>(logger.LastException);
        Assert.Contains("game id 1234", logger.LastMessage);
        viewModel.Dispose();
    }

    [Fact]
    public void DeferredBadgeRows_DoNotRequestArtworkUntilTheViewportAsksForIt()
    {
        var badgeCache = new RecordingBadgeCache();
        var row = new AchievementRowViewModel(
            new RetroAchievementsAchievement(1, "First", "", 5, "000001", 1, null, null),
            badgeCache,
            loadBadge: false);

        Assert.Equal("000001", row.BadgeName);
        Assert.Equal(0, badgeCache.Requests);
        row.Dispose();
    }

    private sealed class FakeDetailsService : IRetroAchievementsDetailsService
    {
        public RetroAchievementsDetailsSnapshot? Response { get; set; }
        public Exception? RefreshException { get; set; }
        public int RefreshCalls { get; private set; }
        public event Action<RetroAchievementsDetailsSnapshot>? DetailsRefreshed;
        public RetroAchievementsDetailsSnapshot? GetCached(int retroAchievementsGameId) => Response;
        public Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshAsync(
            RetroAchievementsCredentials credentials,
            int retroAchievementsGameId,
            CancellationToken cancellationToken = default,
            bool manual = false)
        {
            RefreshCalls++;
            if (RefreshException is { } exception)
                return Task.FromException<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>>(exception);
            return Task.FromResult(Response is { } response
                ? RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Success(response)
                : RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Failure(
                    RetroAchievementsRequestStatus.Offline));
        }
        public void Publish(RetroAchievementsDetailsSnapshot snapshot)
        {
            Response = snapshot;
            DetailsRefreshed?.Invoke(snapshot);
        }
        public void Clear() { }
    }

    private sealed class FakeAccount(bool isConnected = true) : IRetroAchievementsAccountService
    {
        public RetroAchievementsAccount? Account => isConnected ? new("Player", "ULID-9") : null;
        public bool IsConnected => isConnected;
        public RetroAchievementsCredentials? CurrentCredentials =>
            isConnected ? new("Player", "KEY", "ULID-9") : null;
        public Task<RetroAchievementsConnectionResult> ConnectAsync(
            string username,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RetroAchievementsConnectionResult.Connected);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public string LastMessage { get; private set; } = string.Empty;
        public Exception? LastException { get; private set; }

        public void Information(string message) { }
        public void Warning(string message, Exception? exception = null) { }
        public void Error(string message, Exception? exception = null)
        {
            LastMessage = message;
            LastException = exception;
        }
    }

    private sealed class RecordingBadgeCache : IRetroAchievementsBadgeCache
    {
        public int Requests { get; private set; }

        public Task<string?> GetBadgePathAsync(
            string badgeName,
            CancellationToken cancellationToken = default)
        {
            Requests++;
            return Task.FromResult<string?>(null);
        }

        public void Clear() { }
    }
}
