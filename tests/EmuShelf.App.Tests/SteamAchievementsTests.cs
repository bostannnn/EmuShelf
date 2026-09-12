using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Achievements;

namespace EmuShelf.App.Tests;

public sealed class SteamAchievementsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "emushelf-steam-tests-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient _http = new();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly AchievementGameRef Game = new("steam", "381780");
    private const string Key = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task ConnectPersistsOnlyIdentity_AndAccountSwitchCannotReadOtherProgress()
    {
        var settings = new Settings(); var client = new Client(); var keys = new SessionSteamCredentialStore();
        var service = new SteamAchievementsService(client, keys, settings, _root, _http);
        Assert.Equal(AchievementStatus.Success, await service.ConnectAsync("76561198000000001", Key, Token));
        Assert.Equal("76561198000000001", settings.Load().SteamAchievementsSteamId);
        Assert.False(keys.IsPersistent);
        Assert.True((await service.RefreshAsync(Game, Token)).IsSuccess);
        Assert.NotNull(service.GetCached(Game));
        Assert.Equal(AchievementStatus.Success, await service.ConnectAsync("76561198000000002", Key, Token));
        Assert.Null(service.GetCached(Game));
        service.Disconnect();
        Assert.Null(keys.Read()); Assert.Null(settings.Load().SteamAchievementsSteamId);
        Assert.Null(service.GetCached(Game));
    }

    [Fact]
    public async Task DisconnectRejectsInFlightResultAndPreventsCacheRepopulation()
    {
        var client = new Client { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new SteamAchievementsService(client, new SessionSteamCredentialStore(), new Settings(), _root, _http);
        await service.ConnectAsync("76561198000000001", Key, Token);
        var request = service.RefreshAsync(Game, Token);
        await client.Started.Task.WaitAsync(Token);
        service.Disconnect();
        client.Pending.SetResult();
        Assert.Equal(AchievementStatus.AccountChanged, (await request).Status);
        Assert.Null(service.GetCached(Game));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task OfflineRefreshKeepsCachedProgressAndReconnectReadsDisk()
    {
        var client = new Client(); var settings = new Settings(); var keys = new SessionSteamCredentialStore();
        var service = new SteamAchievementsService(client, keys, settings, _root, _http);
        await service.ConnectAsync("76561198000000001", Key, Token);
        await service.RefreshAsync(Game, Token);
        client.Status = AchievementStatus.Offline;
        var result = await service.RefreshAsync(Game, Token, manual: true);
        Assert.Equal(AchievementStatus.Offline, result.Status);
        Assert.True(result.Snapshot!.ProgressKnown);
        var restarted = new SteamAchievementsService(client, keys, settings, _root, _http);
        Assert.True(restarted.GetCached(Game)!.Achievements[0].IsUnlocked);
    }

    [Fact]
    public void SteamRowsHaveNoPointsOrHardcore_UnknownIsNotLocked_AndHiddenDetailsNeedReveal()
    {
        var provider = new SteamAchievementsService(new Client(), new SessionSteamCredentialStore(), new Settings(), _root, _http);
        using var row = new AchievementRowViewModel(new("SECRET", "Ending", "Spoiler", "", 0, null, IsHidden: true), provider, false);
        Assert.False(row.HasPoints); Assert.False(row.IsHardcore);
        Assert.False(row.IsLocked); Assert.False(row.IsUnlocked);
        Assert.Equal("Unknown", row.UnlockStateText);
        Assert.Equal("Hidden achievement", row.Title);
        row.RevealCommand.Execute(null);
        Assert.Equal("Ending", row.Title);
        Assert.Equal("Spoiler", row.Description);
    }

    [Fact]
    public void SteamViewerSkipsPointsSortAndHasConnectState()
    {
        var provider = new SteamAchievementsService(new Client(), new SessionSteamCredentialStore(), new Settings(), _root, _http);
        using var viewer = new AchievementDetailsViewModel("Fixture", Game, provider);
        viewer.CycleSortCommand.Execute(null);
        Assert.Equal(AchievementDisplaySort.UnlockedFirst, viewer.SelectedSort);
        Assert.False(viewer.SupportsPoints); Assert.False(viewer.SupportsHardcore);
        Assert.Contains("Steam", viewer.ProviderText);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task SteamWindowRendersHiddenAndUnlockedRows()
    {
        var provider = new SteamAchievementsService(new Client(), new SessionSteamCredentialStore(), new Settings(), _root, _http);
        await provider.ConnectAsync("76561198000000001", Key, Token);
        using var viewer = new AchievementDetailsViewModel("Steam achievement fixture", Game, provider,
            new(Game, provider.AccountId!, "Fixture", [
                new("FIRST", "First steps", "Begin the adventure", "", 0, true, DateTimeOffset.UtcNow),
                new("SECRET", "The ending", "Hidden story details", "", 1, false, IsHidden: true)], DateTimeOffset.UtcNow));
        var window = new EmuShelf.App.Views.AchievementDetailsWindow { DataContext = viewer };
        window.Show();
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
            using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            Assert.NotNull(frame);
            Assert.Equal("1 / 2 unlocked", viewer.ProgressText);
            using var output = File.Create(Path.Combine(Path.GetTempPath(), "emushelf-steam-achievements.png"));
            frame.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task BulkSyncDeduplicatesGamesAndPublishesOneLibraryUpdate()
    {
        var client = new Client();
        var service = new SteamAchievementsService(client, new SessionSteamCredentialStore(), new Settings(), _root, _http);
        await service.ConnectAsync("76561198000000001", Key, Token);
        var changes = 0; service.Changed += () => changes++;
        var result = await service.SyncLibraryAsync([Game, Game, new("steam", "123"), new("retroachievements", "123")], cancellationToken: Token);
        Assert.Equal(2, result.Total); Assert.Equal(2, result.Updated);
        Assert.Equal(2, client.Calls.Count); Assert.Equal(1, changes);
        Assert.NotNull(service.GetCached(new("steam", "123")));
    }

    [Theory]
    [InlineData(AchievementStatus.RateLimited)]
    [InlineData(AchievementStatus.Offline)]
    [InlineData(AchievementStatus.AuthenticationFailed)]
    public async Task BulkSyncStopsOnServiceFailures(AchievementStatus failure)
    {
        var client = new Client { Status = failure };
        var service = new SteamAchievementsService(client, new SessionSteamCredentialStore(), new Settings(), _root, _http);
        await service.ConnectAsync("76561198000000001", Key, Token);
        var result = await service.SyncLibraryAsync([Game, new("steam", "123")], cancellationToken: Token);
        Assert.Equal(failure, result.Status); Assert.Equal(1, result.Completed);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task BulkSyncCancellationStopsPendingRequest()
    {
        var client = new Client { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new SteamAchievementsService(client, new SessionSteamCredentialStore(), new Settings(), _root, _http);
        await service.ConnectAsync("76561198000000001", Key, Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var sync = service.SyncLibraryAsync([Game, new("steam", "123")], cancellationToken: cancellation.Token);
        await client.Started.Task.WaitAsync(Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync);
        Assert.Single(client.Calls); Assert.Null(service.GetCached(Game));
    }

    [Fact]
    public async Task BulkSyncStopsWhenAccountChangesDuringRequest()
    {
        var client = new Client { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new SteamAchievementsService(client, new SessionSteamCredentialStore(), new Settings(), _root, _http);
        await service.ConnectAsync("76561198000000001", Key, Token);
        var sync = service.SyncLibraryAsync([Game, new("steam", "123")], cancellationToken: Token);
        await client.Started.Task.WaitAsync(Token);
        await service.ConnectAsync("76561198000000002", Key, Token);
        client.Pending.SetResult();
        Assert.Equal(AchievementStatus.AccountChanged, (await sync).Status);
        Assert.Single(client.Calls); Assert.Null(service.GetCached(Game));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task SettingsSyncCommandUpdatesTheWholeLibraryAndRendersProviderGroups()
    {
        var client = new Client();
        var service = new SteamAchievementsService(client, new SessionSteamCredentialStore(), new Settings(), _root, _http);
        await service.ConnectAsync("76561198000000001", Key, Token);
        var context = new RetroAchievementsSettingsContext(null, false,
            (_, _, _, _) => throw new NotSupportedException(), _ => Task.CompletedTask,
            Steam: service, GetSteamGamesAsync: _ => Task.FromResult<IReadOnlyList<AchievementGameRef>>([Game, new("steam", "123")]));
        var settings = new EmulatorSettingsViewModel([], [], new Dictionary<string, EmulatorConfiguration?>(),
            new Configurations(), new FakeDialogService(), retroAchievements: context)
        { SelectedSection = SettingsSection.RetroAchievements, IsSteamAchievementsExpanded = true };
        Assert.True(settings.SyncSteamAchievementsCommand.CanExecute(null));
        await settings.SyncSteamAchievementsCommand.ExecuteAsync(null);
        Assert.Equal(2, client.Calls.Count);
        Assert.Contains("2 updated", settings.SteamStatusText);
        Assert.False(settings.IsSteamBusy);
        settings.IsRetroAchievementsExpanded = true;
        Assert.False(settings.IsSteamAchievementsExpanded);
        settings.IsSteamAchievementsExpanded = true;
        Assert.False(settings.IsRetroAchievementsExpanded);
        var window = new EmuShelf.App.Views.EmulatorSettingsWindow { DataContext = settings, Width = 1100, Height = 800 };
        window.Show();
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
            using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            Assert.NotNull(frame);
            using var output = File.Create(Path.Combine(Path.GetTempPath(), "emushelf-steam-grouped-settings.png"));
            frame.Save(output, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [Theory]
    [InlineData("https://steamcdn-a.akamaihd.net")]
    [InlineData("http://media.steampowered.com")]
    [InlineData("https://cdn.akamai.steamstatic.com")]
    [InlineData("https://cdn.cloudflare.steamstatic.com")]
    public async Task LegacySteamIconsUseCurrentCdnAndReuseTheDiskCache(string host)
    {
        using var handler = new IconResponse();
        using var http = new HttpClient(handler);
        var service = new SteamAchievementsService(new Client(), new SessionSteamCredentialStore(), new Settings(), _root, http);
        const string asset = "2142790/fc7112873f2eb80bf26225c48efadf7ba61e7363.jpg";
        var oldUrl = host + "/steamcommunity/public/images/apps/" + asset;
        var path = await service.GetIconPathAsync(oldUrl, Token);
        Assert.NotNull(path); Assert.True(File.Exists(path));
        Assert.Equal("https://shared.fastly.steamstatic.com/community_assets/images/apps/" + asset, handler.Requests.Single());
        Assert.Equal(path, await service.GetIconPathAsync(oldUrl, Token));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("https://steamcdn-a.akamaihd.net.example.org/steamcommunity/public/images/apps/1/a.jpg")]
    [InlineData("https://unrelated.akamaihd.net/steamcommunity/public/images/apps/1/a.jpg")]
    [InlineData("https://steamcdn-a.akamaihd.net/anything-else")]
    public async Task UntrustedOrUnrecognizedLegacyIconsAreNotRequested(string url)
    {
        using var handler = new IconResponse(); using var http = new HttpClient(handler);
        var service = new SteamAchievementsService(new Client(), new SessionSteamCredentialStore(), new Settings(), _root, http);
        Assert.Null(await service.GetIconPathAsync(url, Token));
        Assert.Empty(handler.Requests);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task IconRowDecodesCdnImageAndCanRetryAfterAFailedDownload()
    {
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(2, 2));
        using var imageBytes = new MemoryStream();
        bitmap.Save(imageBytes, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        using var handler = new IconResponse { Bytes = imageBytes.ToArray(), FailFirst = true };
        using var http = new HttpClient(handler);
        var provider = new SteamAchievementsService(new Client(), new SessionSteamCredentialStore(), new Settings(), _root, http);
        const string icon = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/2142790/fc7112873f2eb80bf26225c48efadf7ba61e7363.jpg";
        using var row = new AchievementRowViewModel(new("FIRST", "First", "Start", icon, 0, true), provider, loadBadge: false);
        await row.LoadBadgeAsync(icon, Token);
        Assert.False(row.HasBadge);
        var publishedOnUiThread = false;
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(row.Badge))
                publishedOnUiThread = Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
        };
        await Task.Run(() => row.LoadBadgeAsync(icon, Token), Token);
        Assert.True(publishedOnUiThread);
        Assert.True(row.HasBadge);
        Assert.Equal(2, row.Badge!.PixelSize.Width);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task GamepadViewerRequestsIconsForVisibleDeferredRows()
    {
        using var handler = new IconResponse(); using var http = new HttpClient(handler);
        var provider = new SteamAchievementsService(new Client(), new SessionSteamCredentialStore(), new Settings(), _root, http);
        await provider.ConnectAsync("76561198000000001", Key, Token);
        const string icon = "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/2142790/fc7112873f2eb80bf26225c48efadf7ba61e7363.jpg";
        using var viewer = new AchievementDetailsViewModel("Fixture", Game, provider,
            new(Game, provider.AccountId!, "Fixture", [new("FIRST", "First", "Start", icon, 0, true)], DateTimeOffset.UtcNow), deferBadgeLoading: true);
        var main = new MainViewModel { IsGamepadMode = true, GamepadAchievementDetails = viewer, GamepadOverlay = GamepadOverlayKind.Achievements };
        main.FocusedGamepadAchievement = viewer.VisibleAchievements[0];
        await Task.Delay(100, Token);
        Assert.NotEmpty(handler.Requests); // Focus loads icons even before any Android tile attachment event.
        var window = new EmuShelf.App.Views.MainWindow { DataContext = main, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
            await Task.Delay(100, Token);
            Assert.NotEmpty(handler.Requests);
        }
        finally { window.Close(); }
    }

    private sealed class IconResponse : HttpMessageHandler
    {
        public byte[] Bytes { get; init; } = [1, 2, 3];
        public bool FailFirst { get; init; }
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(FailFirst && Requests.Count == 1 ? System.Net.HttpStatusCode.ServiceUnavailable : System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(Bytes) });
        }
    }

    private sealed class Configurations : IEmulatorConfigurationStore
    {
        public EmulatorConfiguration? Get(string systemId) => null;
        public void Save(EmulatorConfiguration configuration) { }
        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations) { }
    }

    private sealed class Settings : ISettingsService
    {
        private AppSettings _value = new();
        public AppSettings Load() => _value;
        public void Save(AppSettings value) => _value = value;
    }

    private sealed class Client : ISteamAchievementsClient
    {
        public List<string> Calls { get; } = [];
        public TaskCompletionSource? Pending { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AchievementStatus Status { get; set; } = AchievementStatus.Success;
        public Task<SteamProfileResult> ResolveProfileAsync(string profile, string apiKey, CancellationToken cancellationToken) =>
            Task.FromResult(new SteamProfileResult(AchievementStatus.Success, new(profile, "Fixture")));
        public async Task<AchievementResult> GetAchievementsAsync(string steamId, string appId, string apiKey, CancellationToken cancellationToken)
        {
            Calls.Add(appId);
            Started.TrySetResult();
            if (Pending is not null) await Pending.Task.WaitAsync(cancellationToken);
            return Status == AchievementStatus.Success ? new(Status,
                new(new("steam", appId), steamId, "Fixture", [new("FIRST", "First", "Start", "", 0, true)], DateTimeOffset.UtcNow)) : new(Status);
        }
    }

    public void Dispose() { _http.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
