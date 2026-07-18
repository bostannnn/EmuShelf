using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Achievements;

namespace EmuShelf.App.Tests;

public class RetroAchievementsAccountServiceTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Connect_OnSuccess_PersistsIdentityAndStoresKey()
    {
        var settings = new InMemorySettingsService();
        var store = new SessionOnlyCredentialStore();
        var client = new FakeClient(RetroAchievementsResponse<RetroAchievementsProfile>.Success(
            new RetroAchievementsProfile("Player", "ULID-9", 100, 10)));
        var service = new RetroAchievementsAccountService(settings, settings.Load(), store, client);

        var result = await service.ConnectAsync("Player", "SECRETKEY", Cancellation);

        Assert.Equal(RetroAchievementsConnectionResult.Connected, result);
        Assert.True(service.IsConnected);
        Assert.Equal("ULID-9", service.Account!.UserUlid);
        Assert.Equal("SECRETKEY", store.GetApiKey());
        Assert.Equal("Player", settings.Load().RetroAchievementsUsername);
        Assert.Equal("ULID-9", settings.Load().RetroAchievementsUserUlid);
        Assert.Equal("SECRETKEY", service.CurrentCredentials!.ApiKey);
        Assert.Equal("ULID-9", service.CurrentCredentials.UserUlid);
    }

    [Fact]
    public async Task Connect_OnAuthFailure_DoesNotStoreAnything()
    {
        var settings = new InMemorySettingsService();
        var store = new SessionOnlyCredentialStore();
        var client = new FakeClient(RetroAchievementsResponse<RetroAchievementsProfile>.Failure(
            RetroAchievementsRequestStatus.AuthenticationFailed));
        var service = new RetroAchievementsAccountService(settings, settings.Load(), store, client);

        var result = await service.ConnectAsync("Player", "WRONG", Cancellation);

        Assert.Equal(RetroAchievementsConnectionResult.AuthenticationFailed, result);
        Assert.False(service.IsConnected);
        Assert.Null(service.Account);
        Assert.Null(store.GetApiKey());
        Assert.Null(settings.Load().RetroAchievementsUsername);
    }

    [Fact]
    public async Task Connect_OnOffline_MapsResultWithoutStoring()
    {
        var settings = new InMemorySettingsService();
        var store = new SessionOnlyCredentialStore();
        var client = new FakeClient(RetroAchievementsResponse<RetroAchievementsProfile>.Failure(
            RetroAchievementsRequestStatus.Offline));
        var service = new RetroAchievementsAccountService(settings, settings.Load(), store, client);

        var result = await service.ConnectAsync("Player", "SECRETKEY", Cancellation);

        Assert.Equal(RetroAchievementsConnectionResult.Offline, result);
        Assert.Null(store.GetApiKey());
    }

    [Fact]
    public async Task Connect_WhenKeyStorageFails_ReturnsLocalStorageFailedAndRollsBack()
    {
        var settings = new InMemorySettingsService();
        var store = new ThrowingCredentialStore();
        var client = new FakeClient(RetroAchievementsResponse<RetroAchievementsProfile>.Success(
            new RetroAchievementsProfile("Player", "ULID-9", 100, 10)));
        var service = new RetroAchievementsAccountService(settings, settings.Load(), store, client);

        var result = await service.ConnectAsync("Player", "SECRETKEY", Cancellation);

        Assert.Equal(RetroAchievementsConnectionResult.LocalStorageFailed, result);
        Assert.False(service.IsConnected);
        Assert.Null(service.Account);
        Assert.Null(settings.Load().RetroAchievementsUsername);
        Assert.True(store.WasCleared); // the partially stored key was rolled back
    }

    [Fact]
    public async Task Disconnect_ClearsIdentityAndKey()
    {
        var settings = new InMemorySettingsService();
        var store = new SessionOnlyCredentialStore();
        var client = new FakeClient(RetroAchievementsResponse<RetroAchievementsProfile>.Success(
            new RetroAchievementsProfile("Player", "ULID-9", 100, 10)));
        var service = new RetroAchievementsAccountService(settings, settings.Load(), store, client);
        await service.ConnectAsync("Player", "SECRETKEY", Cancellation);

        await service.DisconnectAsync(Cancellation);

        Assert.False(service.IsConnected);
        Assert.Null(service.Account);
        Assert.Null(store.GetApiKey());
        Assert.Null(settings.Load().RetroAchievementsUsername);
    }

    [Fact]
    public void SessionRestart_IdentityPersistsButKeyGone_IsNotConnected()
    {
        // Simulates a macOS restart: settings kept the identity, the session-only key is gone.
        var settings = new InMemorySettingsService();
        var persisted = settings.Load() with
        {
            RetroAchievementsUsername = "Player",
            RetroAchievementsUserUlid = "ULID-9",
        };
        settings.Save(persisted);
        var store = new SessionOnlyCredentialStore();
        var service = new RetroAchievementsAccountService(
            settings, persisted, store, new FakeClient(null));

        Assert.NotNull(service.Account);
        Assert.False(service.IsConnected);
        Assert.Null(service.CurrentCredentials);
    }

    private sealed class InMemorySettingsService : ISettingsService
    {
        private AppSettings _settings = new();

        public AppSettings Load() => _settings;
        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class ThrowingCredentialStore : IRetroAchievementsCredentialStore
    {
        public bool WasCleared { get; private set; }

        public string? GetApiKey() => null;
        public void SaveApiKey(string apiKey) => throw new IOException("no space left on device");
        public void ClearApiKey() => WasCleared = true;
    }

    private sealed class FakeClient(RetroAchievementsResponse<RetroAchievementsProfile>? profile)
        : IRetroAchievementsClient
    {
        public Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(profile!);

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
            RetroAchievementsCredentials credentials,
            int consoleId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>.Success([]));

        public Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
            RetroAchievementsCredentials credentials,
            IReadOnlyList<int> gameIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>.Success([]));

        public Task<RetroAchievementsResponse<RetroAchievementsGameDetails>> GetGameDetailsAsync(
            RetroAchievementsCredentials credentials,
            int gameId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RetroAchievementsResponse<RetroAchievementsGameDetails>.Failure(
                RetroAchievementsRequestStatus.ServerError));
    }
}
