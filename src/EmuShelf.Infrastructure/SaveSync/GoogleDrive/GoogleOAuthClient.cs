using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>The build's Google OAuth identity. The secret is absent for public (Android) clients.</summary>
/// <remarks>
/// Google issues one client per platform type. A desktop client carries a secret — which Google
/// itself documents as non-confidential for installed apps, and which EmuShelf already ships (see
/// DECISIONS 2026-08-04 and 2026-08-06) — while an Android client has none and is bound instead to
/// the package name and signing certificate. PKCE is what actually secures the exchange in both
/// cases, so the flow is identical apart from whether the secret is sent.
/// </remarks>
public sealed record GoogleOAuthClientCredentials(string ClientId, string? ClientSecret)
{
    public bool IsPublicClient => string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>What a successful token call returned.</summary>
public sealed record GoogleTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// The Google half of the OAuth authorization-code flow with PKCE: build the authorization URL,
/// exchange the returned code, and refresh an expired access token.
/// </summary>
/// <remarks>
/// EmuShelf requests <c>drive.file</c> rather than full <c>drive</c>. It is the least privilege that
/// does the job — the app can only ever see files it created itself, never the rest of the user's
/// Drive — and it keeps EmuShelf out of Google's restricted-scope verification regime. The cost is
/// that a folder created under a different app's full-Drive scope is invisible here, so a save folder
/// set up by some other tool cannot be adopted; each machine re-uploads its own copies. That is safe,
/// because the transport is copy-only, but it is not silent: the caller must say so.
/// </remarks>
public sealed class GoogleOAuthClient
{
    public const string DefaultAuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string DefaultTokenEndpoint = "https://oauth2.googleapis.com/token";

    /// <summary>Per-file Drive access: only what this app creates.</summary>
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";

    /// <summary>
    /// Refresh this long before the stated expiry. A token that expires while a large upload is in
    /// flight costs a retry; renewing early costs nothing.
    /// </summary>
    internal static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly GoogleOAuthClientCredentials _credentials;
    private readonly Uri _authorizationEndpoint;
    private readonly Uri _tokenEndpoint;
    private readonly TimeProvider _time;

    public GoogleOAuthClient(
        HttpClient httpClient,
        GoogleOAuthClientCredentials credentials,
        string authorizationEndpoint = DefaultAuthorizationEndpoint,
        string tokenEndpoint = DefaultTokenEndpoint,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        if (string.IsNullOrWhiteSpace(credentials.ClientId))
            throw new ArgumentException("The Google OAuth client id is missing from this build.", nameof(credentials));
        _authorizationEndpoint = new Uri(authorizationEndpoint, UriKind.Absolute);
        _tokenEndpoint = new Uri(tokenEndpoint, UriKind.Absolute);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Builds the URL to open in the browser, and the PKCE/state values that must survive until the redirect.</summary>
    public GoogleAuthorizationRequest CreateAuthorizationRequest(string redirectUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        var verifier = CreateCodeVerifier();
        var state = CreateCodeVerifier();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = _credentials.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = DriveFileScope,
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            // Without offline access Google returns no refresh token, and sync would stop working
            // the first time the access token expired.
            ["access_type"] = "offline",
            // Google only re-issues a refresh token on an explicit consent. A user who reconnects
            // after a revoke would otherwise get an access token and no way to renew it.
            ["prompt"] = "consent",
            ["state"] = state,
        };

        var query = string.Join('&', parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new GoogleAuthorizationRequest(
            new Uri($"{_authorizationEndpoint}?{query}"),
            verifier,
            state,
            redirectUri);
    }

    /// <summary>Trades an authorization code for tokens.</summary>
    public Task<GoogleTokens> ExchangeCodeAsync(
        GoogleAuthorizationRequest request,
        string code,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return PostAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = request.RedirectUri,
                ["code_verifier"] = request.CodeVerifier,
            },
            requireRefreshToken: true,
            cancellationToken);
    }

    /// <summary>
    /// Renews an access token. Google does not re-issue the refresh token here, so the result carries
    /// the one that was passed in and the caller's stored token stays valid.
    /// </summary>
    public async Task<GoogleTokens> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var tokens = await PostAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            requireRefreshToken: false,
            cancellationToken);
        return tokens with { RefreshToken = tokens.RefreshToken ?? refreshToken };
    }

    private async Task<GoogleTokens> PostAsync(
        Dictionary<string, string> parameters,
        bool requireRefreshToken,
        CancellationToken cancellationToken)
    {
        parameters["client_id"] = _credentials.ClientId;
        if (!_credentials.IsPublicClient)
            parameters["client_secret"] = _credentials.ClientSecret!;

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw DescribeFailure(response.StatusCode, body);

        TokenResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TokenResponse>(body);
        }
        catch (JsonException ex)
        {
            throw new IOException("Google returned a sign-in response EmuShelf could not read.", ex);
        }

        if (parsed is null || string.IsNullOrWhiteSpace(parsed.AccessToken))
            throw new IOException("Google returned a sign-in response with no access token.");
        if (requireRefreshToken && string.IsNullOrWhiteSpace(parsed.RefreshToken))
        {
            throw new IOException(
                "Google did not return a refresh token, so the connection could not be saved. Try connecting again.");
        }

        return new GoogleTokens(
            parsed.AccessToken,
            string.IsNullOrWhiteSpace(parsed.RefreshToken) ? null : parsed.RefreshToken,
            _time.GetUtcNow().AddSeconds(parsed.ExpiresInSeconds <= 0 ? 3600 : parsed.ExpiresInSeconds));
    }

    /// <summary>
    /// Maps a token-endpoint failure onto the distinction that matters to the user: whether the
    /// authorization is gone for good (reconnect) or the call merely failed (retry).
    /// </summary>
    internal static Exception DescribeFailure(System.Net.HttpStatusCode statusCode, string body)
    {
        var error = TryReadError(body);
        // invalid_grant is Google's answer for a revoked, expired, or already-used grant. No amount
        // of retrying fixes it; only a fresh consent does.
        if (string.Equals(error, "invalid_grant", StringComparison.Ordinal) ||
            string.Equals(error, "invalid_client", StringComparison.Ordinal))
        {
            return new GoogleAuthorizationRequiredException(
                "Google no longer accepts this connection. Reconnect the account in Settings.");
        }

        var detail = string.IsNullOrWhiteSpace(error) ? string.Empty : $" ({error})";
        return new IOException($"Google refused the sign-in request{detail}. Status {(int)statusCode}.");
    }

    private static string? TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A high-entropy PKCE verifier, base64url with no padding as RFC 7636 requires.</summary>
    internal static string CreateCodeVerifier() => Base64Url(RandomNumberGenerator.GetBytes(32));

    internal static string CreateCodeChallenge(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresInSeconds { get; init; }
    }
}

/// <summary>
/// One in-flight authorization. The verifier and state must be held until the redirect arrives:
/// the verifier proves this app started the flow, and the state proves the redirect belongs to it.
/// </summary>
public sealed record GoogleAuthorizationRequest(
    Uri AuthorizationUri,
    string CodeVerifier,
    string State,
    string RedirectUri);
