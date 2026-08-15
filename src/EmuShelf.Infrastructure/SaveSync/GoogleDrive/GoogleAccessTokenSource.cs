namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Turns the stored refresh token into access tokens, caching the current one until it is close to
/// expiring. One sync makes many Drive calls; minting a token per call would triple the request
/// count and the latency that a launch waits on.
/// </summary>
public sealed class GoogleAccessTokenSource : IGoogleAccessTokenSource
{
    private readonly GoogleOAuthClient _oauth;
    private readonly IGoogleDriveTokenStore _tokens;
    private readonly TimeProvider _time;

    // Serialized because several units can ask for a token at once at the start of a sync, and
    // without this each would run its own refresh against the same rate-limited endpoint.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAtUtc;

    public GoogleAccessTokenSource(
        GoogleOAuthClient oauth,
        IGoogleDriveTokenStore tokens,
        TimeProvider? timeProvider = null)
    {
        _oauth = oauth ?? throw new ArgumentNullException(nameof(oauth));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Whether an account is connected at all, without making a network call.</summary>
    public bool IsConnected => _tokens.Read() is not null;

    public async Task<string> GetAccessTokenAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _accessToken is not null && _time.GetUtcNow() < _expiresAtUtc)
                return _accessToken;

            var refreshToken = _tokens.Read() ??
                throw new GoogleAuthorizationRequiredException(
                    "No Google account is connected. Connect one in Settings to sync saves.");

            var refreshed = await _oauth.RefreshAsync(refreshToken, cancellationToken);
            _accessToken = refreshed.AccessToken;
            _expiresAtUtc = refreshed.ExpiresAtUtc - GoogleOAuthClient.ExpirySkew;

            // Google normally returns the same refresh token, but it may rotate it. Persisting the
            // returned one keeps the connection alive across that rotation.
            if (refreshed.RefreshToken is { } rotated && !string.Equals(rotated, refreshToken, StringComparison.Ordinal))
                _tokens.Write(rotated);

            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adopts the tokens from a just-completed sign-in, so the connect flow does not immediately
    /// spend a refresh call to get an access token it was already handed.
    /// </summary>
    public void Adopt(GoogleTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.RefreshToken is { } refreshToken)
            _tokens.Write(refreshToken);

        _accessToken = tokens.AccessToken;
        _expiresAtUtc = tokens.ExpiresAtUtc - GoogleOAuthClient.ExpirySkew;
    }

    /// <summary>Drops both the cached access token and the stored account.</summary>
    public void Disconnect()
    {
        _accessToken = null;
        _expiresAtUtc = default;
        _tokens.Clear();
    }
}
