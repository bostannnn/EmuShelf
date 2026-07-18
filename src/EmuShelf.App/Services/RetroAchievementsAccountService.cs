using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

public enum RetroAchievementsConnectionResult
{
    Connected,
    AuthenticationFailed,
    Offline,
    ServerError,
    RateLimited,

    /// <summary>Validation succeeded but the key or identity could not be stored on this machine.</summary>
    LocalStorageFailed,
}

/// <summary>
/// Owns the RetroAchievements account connection: validates a username and Web API key against
/// the read-only API, then persists the non-secret identity in <c>settings.json</c> and the key in
/// the platform credential store. The key is never logged and never written to settings.json.
/// </summary>
public interface IRetroAchievementsAccountService
{
    /// <summary>The persisted account identity, present even when the session key must be re-entered.</summary>
    RetroAchievementsAccount? Account { get; }

    /// <summary>True when both the identity and a usable API key are available for calls.</summary>
    bool IsConnected { get; }

    /// <summary>Credentials for read-only calls, or null when not usable (key missing).</summary>
    RetroAchievementsCredentials? CurrentCredentials { get; }

    Task<RetroAchievementsConnectionResult> ConnectAsync(
        string username,
        string apiKey,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public sealed class RetroAchievementsAccountService : IRetroAchievementsAccountService
{
    private readonly ISettingsService _settingsService;
    private readonly IRetroAchievementsCredentialStore _credentialStore;
    private readonly IRetroAchievementsClient _client;
    private readonly IAppLogger _logger;
    private RetroAchievementsAccount? _account;

    public RetroAchievementsAccountService(
        ISettingsService settingsService,
        AppSettings settings,
        IRetroAchievementsCredentialStore credentialStore,
        IRetroAchievementsClient client,
        IAppLogger? logger = null)
    {
        _settingsService = settingsService;
        _credentialStore = credentialStore;
        _client = client;
        _logger = logger ?? NullAppLogger.Instance;
        _account = BuildAccount(settings);
    }

    public RetroAchievementsAccount? Account => _account;

    public bool IsConnected => CurrentCredentials is not null;

    public RetroAchievementsCredentials? CurrentCredentials
    {
        get
        {
            if (_account is null)
                return null;
            var apiKey = _credentialStore.GetApiKey();
            return string.IsNullOrEmpty(apiKey)
                ? null
                : new RetroAchievementsCredentials(_account.Username, apiKey);
        }
    }

    public async Task<RetroAchievementsConnectionResult> ConnectAsync(
        string username,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var trimmedUser = username.Trim();
        var trimmedKey = apiKey.Trim();
        if (trimmedUser.Length == 0 || trimmedKey.Length == 0)
            return RetroAchievementsConnectionResult.AuthenticationFailed;

        var credentials = new RetroAchievementsCredentials(trimmedUser, trimmedKey);
        var response = await _client.GetUserProfileAsync(credentials, cancellationToken);
        if (!response.IsSuccess)
            return Map(response.Status);

        var profile = response.Value!;
        try
        {
            _credentialStore.SaveApiKey(trimmedKey);
            await PersistAsync(profile.Username, profile.UserUlid, cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Validation succeeded but storing the key or identity failed (DPAPI error, read-only
            // or full Settings/). Roll back any stored key so a secret is never left without an
            // identity, and report a result instead of throwing out of a result-returning method.
            _logger.Error("Connected to RetroAchievements but could not store credentials locally.", ex);
            TryClearCredential();
            _account = null;
            return RetroAchievementsConnectionResult.LocalStorageFailed;
        }

        _account = new RetroAchievementsAccount(profile.Username, profile.UserUlid);
        _logger.Information($"Connected RetroAchievements account {profile.Username}.");
        return RetroAchievementsConnectionResult.Connected;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        TryClearCredential();
        _account = null;
        await PersistAsync(null, null, cancellationToken);
        _logger.Information("Disconnected the RetroAchievements account.");
    }

    private void TryClearCredential()
    {
        try
        {
            _credentialStore.ClearApiKey();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Could not remove the stored RetroAchievements key.", ex);
        }
    }

    private static RetroAchievementsAccount? BuildAccount(AppSettings settings) =>
        string.IsNullOrEmpty(settings.RetroAchievementsUsername)
            ? null
            : new RetroAchievementsAccount(
                settings.RetroAchievementsUsername,
                settings.RetroAchievementsUserUlid ?? settings.RetroAchievementsUsername);

    private async Task PersistAsync(
        string? username,
        string? ulid,
        CancellationToken cancellationToken)
    {
        // Merge with the latest snapshot so this never reverts an independent theme or metadata
        // change (mirrors MetadataPreferencesService). Only the non-secret identity is written.
        var latest = await Task.Run(_settingsService.Load, cancellationToken);
        var updated = latest with
        {
            RetroAchievementsUsername = username,
            RetroAchievementsUserUlid = ulid,
        };
        await Task.Run(() => _settingsService.Save(updated), cancellationToken);
    }

    private static RetroAchievementsConnectionResult Map(RetroAchievementsRequestStatus status) => status switch
    {
        RetroAchievementsRequestStatus.AuthenticationFailed =>
            RetroAchievementsConnectionResult.AuthenticationFailed,
        RetroAchievementsRequestStatus.Offline => RetroAchievementsConnectionResult.Offline,
        RetroAchievementsRequestStatus.RateLimited => RetroAchievementsConnectionResult.RateLimited,
        _ => RetroAchievementsConnectionResult.ServerError,
    };
}
