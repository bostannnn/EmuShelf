using EmuShelf.App.Services;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;

namespace EmuShelf.App.Tests;

public class ScreenScraperAccountServiceTests
{
    [Fact]
    public async Task Connect_Success_StoresCredentials_EnablesProvider_AndKeepsAccountInfo()
    {
        var settings = new FakeSettingsService(new AppSettings());
        var store = new SessionOnlyScreenScraperCredentialStore();
        var client = new FakeClient { AccountResult = SuccessAccount() };
        var service = new ScreenScraperAccountService(settings, store, client);

        var summary = await service.ConnectAsync("bostan", "secret", TestContext.Current.CancellationToken);

        Assert.Equal(ScreenScraperConnectionResult.Connected, summary.Result);
        Assert.True(service.IsConnected);
        Assert.NotNull(store.GetCredentials());
        Assert.Equal("bostan", store.GetCredentials()!.Username);
        Assert.True(settings.Current.Scraping.ScreenScraper.Enabled);
        Assert.Equal("1", service.LastAccountInfo!.Tier);
    }

    [Fact]
    public async Task Connect_AuthenticationFailure_StoresNothing_AndLeavesProviderOff()
    {
        var settings = new FakeSettingsService(new AppSettings());
        var store = new SessionOnlyScreenScraperCredentialStore();
        var client = new FakeClient
        {
            AccountResult = new ScreenScraperResult<ScreenScraperAccountInfo>(
                ScreenScraperRequestStatus.AuthenticationFailed, null, null, "no"),
        };
        var service = new ScreenScraperAccountService(settings, store, client);

        var summary = await service.ConnectAsync("bostan", "wrong", TestContext.Current.CancellationToken);

        Assert.Equal(ScreenScraperConnectionResult.AuthenticationFailed, summary.Result);
        Assert.False(service.IsConnected);
        Assert.Null(store.GetCredentials());
        Assert.False(settings.Current.Scraping.ScreenScraper.Enabled);
    }

    [Fact]
    public async Task Connect_WithoutLiveClient_ReportsProviderUnavailable()
    {
        var service = new ScreenScraperAccountService(
            new FakeSettingsService(new AppSettings()),
            new SessionOnlyScreenScraperCredentialStore(),
            client: null);

        var summary = await service.ConnectAsync("bostan", "secret", TestContext.Current.CancellationToken);

        Assert.Equal(ScreenScraperConnectionResult.ProviderUnavailable, summary.Result);
        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task Connect_WithEmptyCredentials_FailsWithoutCallingTheApi()
    {
        var client = new FakeClient { AccountResult = SuccessAccount() };
        var service = new ScreenScraperAccountService(
            new FakeSettingsService(new AppSettings()),
            new SessionOnlyScreenScraperCredentialStore(),
            client);

        var summary = await service.ConnectAsync("   ", "", TestContext.Current.CancellationToken);

        Assert.Equal(ScreenScraperConnectionResult.AuthenticationFailed, summary.Result);
        Assert.Equal(0, client.AccountCalls);
    }

    [Fact]
    public async Task Disconnect_ClearsCredentials_AndDisablesProvider()
    {
        var settings = new FakeSettingsService(new AppSettings());
        var store = new SessionOnlyScreenScraperCredentialStore();
        var service = new ScreenScraperAccountService(settings, store, new FakeClient { AccountResult = SuccessAccount() });
        await service.ConnectAsync("bostan", "secret", TestContext.Current.CancellationToken);

        await service.DisconnectAsync(TestContext.Current.CancellationToken);

        Assert.False(service.IsConnected);
        Assert.Null(store.GetCredentials());
        Assert.Null(service.LastAccountInfo);
        Assert.False(settings.Current.Scraping.ScreenScraper.Enabled);
    }

    private static ScreenScraperResult<ScreenScraperAccountInfo> SuccessAccount() =>
        new(
            ScreenScraperRequestStatus.Success,
            new ScreenScraperAccountInfo("42", "bostan", "1", new ScreenScraperQuota(1, 5, 20000, 0, 2000, null)),
            new ScreenScraperQuota(1, 5, 20000, 0, 2000, null),
            null);

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current;

        public FakeSettingsService(AppSettings current) => Current = current;

        public AppSettings Load() => Current;

        public void Save(AppSettings settings) => Current = settings;
    }

    private sealed class FakeClient : IScreenScraperClient
    {
        public ScreenScraperResult<ScreenScraperAccountInfo> AccountResult { get; set; } =
            new(ScreenScraperRequestStatus.ServiceUnavailable, null, null, null);

        public int AccountCalls { get; private set; }

        public Task<ScreenScraperResult<ScreenScraperAccountInfo>> GetAccountInfoAsync(
            ScreenScraperUserCredentials userCredentials,
            CancellationToken cancellationToken = default)
        {
            AccountCalls++;
            return Task.FromResult(AccountResult);
        }

        public Task<ScreenScraperResult<ScreenScraperGameInfo>> GetGameInfoAsync(
            ScreenScraperUserCredentials userCredentials,
            ScreenScraperGameRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperSystem>>> GetSystemsAsync(
            ScreenScraperUserCredentials userCredentials,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchGamesAsync(
            ScreenScraperUserCredentials userCredentials,
            int systemId,
            string query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
