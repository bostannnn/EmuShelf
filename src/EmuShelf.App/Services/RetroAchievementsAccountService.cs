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
        _credentialStore.SaveApiKey(trimmedKey);
        _account = new RetroAchievementsAccount(profile.Username, profile.UserUlid);
        await PersistAsync(profile.Username, profile.UserUlid, cancellationToken);
        _logger.Information($"Connected RetroAchievements account {profile.Username}.");
        return RetroAchievementsConnectionResult.Connected;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _credentialStore.ClearApiKey();
        _account = null;
        await PersistAsync(null, null, cancellationToken);
        _logger.Information("Disconnected the RetroAchievements account.");
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
