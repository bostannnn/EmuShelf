using System.Threading;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Updates;
using Xunit;

namespace EmuShelf.App.Tests;

public class AppUpdateCoordinatorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly UpdateAsset Payload = new("EmuShelf-x.zip", "https://example.test/x.zip", 10);
    private static readonly UpdateAsset Checksum = new("EmuShelf-x.sha256", "https://example.test/x.sha256", 70);

    private static UpdateCheckResult.UpdateAvailable Available(int major, int minor, int patch) =>
        new(new SemanticVersion(major, minor, patch), $"v{major}.{minor}.{patch}", "notes", Payload, Checksum);

    [Fact]
    public async Task CheckOnLaunch_WhenAutoDisabled_DoesNotCheck()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var coordinator = Create(updates, new AppSettings { Updates = new UpdateSettings { AutomaticallyCheck = false } });

        await coordinator.CheckOnLaunchAsync(Token);

        Assert.Equal(0, updates.CheckCalls);
        Assert.False(coordinator.IsBannerVisible);
    }

    [Fact]
    public async Task CheckOnLaunch_WhenCheckedRecently_IsThrottled()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var settings = new AppSettings
        {
            Updates = new UpdateSettings { AutomaticallyCheck = true, LastCheckUtc = DateTimeOffset.UtcNow },
        };
        var coordinator = Create(updates, settings);

        await coordinator.CheckOnLaunchAsync(Token);

        Assert.Equal(0, updates.CheckCalls);
    }

    [Fact]
    public async Task CheckOnLaunch_UpdateAvailable_ShowsBannerAndRecordsCheckTime()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var settingsService = new InMemorySettingsService(new AppSettings());
        var coordinator = Create(updates, settingsService.Load(), settingsService);

        await coordinator.CheckOnLaunchAsync(Token);

        Assert.Equal(1, updates.CheckCalls);
        Assert.True(coordinator.IsBannerVisible);
        Assert.True(coordinator.HasAvailableUpdate);
        Assert.Equal("2.0.0", coordinator.AvailableVersion);
        Assert.NotNull(settingsService.Current.Updates.LastCheckUtc);
    }

    [Fact]
    public async Task CheckOnLaunch_SkippedVersion_StaysHiddenButRemembersUpdate()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var settings = new AppSettings { Updates = new UpdateSettings { SkippedVersion = "2.0.0" } };
        var coordinator = Create(updates, settings);

        await coordinator.CheckOnLaunchAsync(Token);

        Assert.False(coordinator.IsBannerVisible);
        Assert.True(coordinator.HasAvailableUpdate);
    }

    [Fact]
    public async Task CheckManually_SkippedVersion_StillShowsBanner()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var settings = new AppSettings { Updates = new UpdateSettings { SkippedVersion = "2.0.0" } };
        var coordinator = Create(updates, settings);

        var message = await coordinator.CheckManuallyAsync(Token);

        Assert.True(coordinator.IsBannerVisible);
        Assert.Contains("2.0.0", message);
    }

    [Fact]
    public async Task SkipVersion_PersistsSkippedVersion()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var settingsService = new InMemorySettingsService(new AppSettings());
        var coordinator = Create(updates, settingsService.Load(), settingsService);
        await coordinator.CheckManuallyAsync(Token);

        coordinator.SkipVersionCommand.Execute(null);

        Assert.False(coordinator.IsBannerVisible);
        Assert.Equal("2.0.0", settingsService.Current.Updates.SkippedVersion);
    }

    [Fact]
    public async Task InstallAsync_AppliesUpdateAndRequestsExit()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var applier = new FakeUpdateApplier { CanApplyResult = true };
        var exited = false;
        var coordinator = Create(updates, new AppSettings(), applier: applier, requestExit: () => exited = true);
        await coordinator.CheckManuallyAsync(Token);

        await coordinator.InstallAsync();

        Assert.True(applier.Applied);
        Assert.True(exited);
    }

    [Fact]
    public async Task InstallAsync_WhenPlatformCannotApply_ReportsErrorWithoutExiting()
    {
        var updates = new FakeUpdateService { Result = Available(2, 0, 0) };
        var applier = new FakeUpdateApplier { CanApplyResult = false, Reason = "no can do" };
        var exited = false;
        var coordinator = Create(updates, new AppSettings(), applier: applier, requestExit: () => exited = true);
        await coordinator.CheckManuallyAsync(Token);

        await coordinator.InstallAsync();

        Assert.False(applier.Applied);
        Assert.False(exited);
        Assert.True(coordinator.HasError);
        Assert.Equal("no can do", coordinator.StatusText);
    }

    private static AppUpdateCoordinator Create(
        FakeUpdateService updates,
        AppSettings settings,
        InMemorySettingsService? settingsService = null,
        FakeUpdateApplier? applier = null,
        Action? requestExit = null) =>
        new(
            updates,
            applier ?? new FakeUpdateApplier { CanApplyResult = true },
            settingsService ?? new InMemorySettingsService(settings),
            settings,
            NullAppLogger.Instance,
            requestExit ?? (() => { }));

    private sealed class FakeUpdateService : IUpdateService
    {
        public UpdateCheckResult Result { get; set; } = new UpdateCheckResult.UpToDate(SemanticVersion.Zero);
        public int CheckCalls { get; private set; }

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            return Task.FromResult(Result);
        }

        public Task<StagedUpdate> DownloadAndStageAsync(
            UpdateCheckResult.UpdateAvailable update,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(1.0);
            return Task.FromResult(new StagedUpdate(update.Version, "/tmp/staged"));
        }
    }

    private sealed class FakeUpdateApplier : IUpdateApplier
    {
        public bool CanApplyResult { get; set; } = true;
        public string? Reason { get; set; }
        public bool Applied { get; private set; }

        public bool CanApply(out string? reason)
        {
            reason = Reason;
            return CanApplyResult;
        }

        public void ApplyAndRelaunch(StagedUpdate staged) => Applied = true;
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        public InMemorySettingsService(AppSettings settings) => Current = settings;

        public AppSettings Current { get; private set; }

        public AppSettings Load() => Current;

        public void Save(AppSettings settings) => Current = settings;

        public AppSettings Update(Func<AppSettings, AppSettings> update)
        {
            Current = update(Current);
            return Current;
        }
    }
}
