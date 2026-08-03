using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

public enum ScreenScraperConnectionResult
{
    Connected,
    AuthenticationFailed,
    Offline,
    RateLimited,
    QuotaExceeded,
    ServerError,

    /// <summary>Developer credentials are not provisioned, so no live client exists.</summary>
    ProviderUnavailable,

    /// <summary>Validation succeeded but the credentials could not be stored on this machine.</summary>
    LocalStorageFailed,
}

public sealed record ScreenScraperConnectionSummary(
    ScreenScraperConnectionResult Result,
    ScreenScraperAccountInfo? Account = null);

/// <summary>What the Settings ScreenScraper section needs: current state plus connect/disconnect.</summary>
public sealed record ScreenScraperSettingsContext(
    bool IsConnected,
    ScreenScraperAccountInfo? Account,
    Func<string, string, CancellationToken, Task<ScreenScraperConnectionSummary>> ConnectAsync,
    Func<CancellationToken, Task> DisconnectAsync);

/// <summary>
/// Owns the ScreenScraper account connection: validates a username and password against the API,
/// stores both in the platform credential store, and flips the ScreenScraper provider on. Unlike
/// RetroAchievements, the username is part of the secret (it is an authentication field), so nothing
/// account-identifying is written to settings.json — connected state is derived from the store.
/// </summary>
public interface IScreenScraperAccountService
{
    /// <summary>True when usable account credentials are available (a live client and a stored login).</summary>
    bool IsConnected { get; }

    /// <summary>Account level/quota from the last successful validation, for display only.</summary>
    ScreenScraperAccountInfo? LastAccountInfo { get; }

    Task<ScreenScraperConnectionSummary> ConnectAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public sealed class ScreenScraperAccountService : IScreenScraperAccountService
{
    private readonly ISettingsService _settingsService;
    private readonly IScreenScraperCredentialStore _credentialStore;
    private readonly IScreenScraperClient? _client;
    private readonly IAppLogger _logger;
    private ScreenScraperAccountInfo? _lastAccountInfo;

    public ScreenScraperAccountService(
        ISettingsService settingsService,
        IScreenScraperCredentialStore credentialStore,
        IScreenScraperClient? client,
        IAppLogger? logger = null)
    {
        _settingsService = settingsService;
        _credentialStore = credentialStore;
        _client = client;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public bool IsConnected => _client is not null && _credentialStore.GetCredentials() is not null;

    public ScreenScraperAccountInfo? LastAccountInfo => _lastAccountInfo;

    public async Task<ScreenScraperConnectionSummary> ConnectAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var trimmedUsername = username.Trim();
        // The password is stored and sent verbatim — trimming could silently corrupt a valid login.
        if (trimmedUsername.Length == 0 || string.IsNullOrEmpty(password))
            return new ScreenScraperConnectionSummary(ScreenScraperConnectionResult.AuthenticationFailed);
        if (_client is null)
            return new ScreenScraperConnectionSummary(ScreenScraperConnectionResult.ProviderUnavailable);

        var credentials = new ScreenScraperUserCredentials(trimmedUsername, password);
        var response = await _client.GetAccountInfoAsync(credentials, cancellationToken);
        if (response.Status != ScreenScraperRequestStatus.Success)
            return new ScreenScraperConnectionSummary(Map(response.Status));

        try
        {
            _credentialStore.SaveCredentials(credentials);
            await SetProviderEnabledAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.Error("Validated the ScreenScraper account but could not store its credentials locally.", ex);
            TryClearCredentials();
            return new ScreenScraperConnectionSummary(ScreenScraperConnectionResult.LocalStorageFailed);
        }

        _lastAccountInfo = response.Data;
        _logger.Information("Connected a ScreenScraper account.");
        return new ScreenScraperConnectionSummary(ScreenScraperConnectionResult.Connected, response.Data);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        TryClearCredentials();
        _lastAccountInfo = null;
        await SetProviderEnabledAsync(false, cancellationToken);
        _logger.Information("Disconnected the ScreenScraper account.");
    }

    private void TryClearCredentials()
    {
        try
        {
            _credentialStore.ClearCredentials();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Could not remove the stored ScreenScraper credentials.", ex);
        }
    }

    private Task SetProviderEnabledAsync(bool enabled, CancellationToken cancellationToken) =>
        Task.Run(
            () => _settingsService.Update(settings => settings with
            {
                Scraping = settings.Scraping with
                {
                    ScreenScraper = settings.Scraping.ScreenScraper with { Enabled = enabled },
                },
            }),
            cancellationToken);

    private static ScreenScraperConnectionResult Map(ScreenScraperRequestStatus status) => status switch
    {
        ScreenScraperRequestStatus.AuthenticationFailed => ScreenScraperConnectionResult.AuthenticationFailed,
        ScreenScraperRequestStatus.NetworkError => ScreenScraperConnectionResult.Offline,
        ScreenScraperRequestStatus.RateLimited => ScreenScraperConnectionResult.RateLimited,
        ScreenScraperRequestStatus.DailyQuotaExceeded or
            ScreenScraperRequestStatus.FailedLookupQuotaExceeded => ScreenScraperConnectionResult.QuotaExceeded,
        _ => ScreenScraperConnectionResult.ServerError,
    };
}
