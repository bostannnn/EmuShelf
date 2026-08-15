using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

public sealed class GoogleOAuthClientTests
{
    private static CancellationToken Cancellation => CancellationToken.None;

    private static readonly GoogleOAuthClientCredentials Desktop = new("client-id", "client-secret");
    private static readonly GoogleOAuthClientCredentials Android = new("android-client-id", null);

    [Fact]
    public void CreateAuthorizationRequest_CarriesPkceOfflineAccessAndTheLeastScope()
    {
        var request = Client(new ScriptedHttpHandler()).CreateAuthorizationRequest("http://127.0.0.1:5000/");

        var query = HttpUtility.ParseQueryString(request.AuthorizationUri.Query);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(GoogleOAuthClient.DriveFileScope, query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal(request.State, query["state"]);
        Assert.Equal("http://127.0.0.1:5000/", query["redirect_uri"]);
    }

    [Fact]
    public void CreateAuthorizationRequest_ChallengeIsTheSha256OfTheVerifier()
    {
        var request = Client(new ScriptedHttpHandler()).CreateAuthorizationRequest("http://127.0.0.1:5000/");

        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(request.CodeVerifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, HttpUtility.ParseQueryString(request.AuthorizationUri.Query)["code_challenge"]);
    }

    [Fact]
    public void CreateAuthorizationRequest_UsesAFreshVerifierAndStateEachTime()
    {
        var client = Client(new ScriptedHttpHandler());

        var first = client.CreateAuthorizationRequest("http://127.0.0.1:5000/");
        var second = client.CreateAuthorizationRequest("http://127.0.0.1:5000/");

        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
        Assert.NotEqual(first.State, second.State);
    }

    [Fact]
    public async Task ExchangeCode_SendsTheVerifierAndSecretAndReturnsBothTokens()
    {
        var handler = new ScriptedHttpHandler().RespondJson(
            """{"access_token":"at","refresh_token":"rt","expires_in":3600}""");
        var client = Client(handler);
        var request = client.CreateAuthorizationRequest("http://127.0.0.1:5000/");

        var tokens = await client.ExchangeCodeAsync(request, "the-code", Cancellation);

        Assert.Equal("at", tokens.AccessToken);
        Assert.Equal("rt", tokens.RefreshToken);
        var body = HttpUtility.ParseQueryString(handler.Requests[0].BodyText);
        Assert.Equal("authorization_code", body["grant_type"]);
        Assert.Equal("the-code", body["code"]);
        Assert.Equal(request.CodeVerifier, body["code_verifier"]);
        Assert.Equal("client-secret", body["client_secret"]);
    }

    [Fact]
    public async Task ExchangeCode_PublicClientSendsNoSecret()
    {
        // An Android OAuth client has no secret; sending an empty one is rejected outright.
        var handler = new ScriptedHttpHandler().RespondJson(
            """{"access_token":"at","refresh_token":"rt","expires_in":3600}""");
        var client = Client(handler, Android);
        var request = client.CreateAuthorizationRequest("com.emushelf.app:/oauth");

        await client.ExchangeCodeAsync(request, "the-code", Cancellation);

        var body = HttpUtility.ParseQueryString(handler.Requests[0].BodyText);
        Assert.Null(body["client_secret"]);
        Assert.Equal("android-client-id", body["client_id"]);
    }

    [Fact]
    public async Task ExchangeCode_WithoutARefreshTokenFails()
    {
        // Connecting without a refresh token would appear to work and then stop syncing an hour
        // later, with nothing to renew from.
        var handler = new ScriptedHttpHandler().RespondJson("""{"access_token":"at","expires_in":3600}""");
        var client = Client(handler);
        var request = client.CreateAuthorizationRequest("http://127.0.0.1:5000/");

        await Assert.ThrowsAsync<IOException>(() => client.ExchangeCodeAsync(request, "the-code", Cancellation));
    }

    [Fact]
    public async Task Refresh_KeepsTheExistingRefreshTokenWhenGoogleReturnsNone()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"access_token":"at2","expires_in":3600}""");

        var tokens = await Client(handler).RefreshAsync("rt", Cancellation);

        Assert.Equal("at2", tokens.AccessToken);
        Assert.Equal("rt", tokens.RefreshToken);
    }

    [Fact]
    public async Task Refresh_AdoptsARotatedRefreshToken()
    {
        var handler = new ScriptedHttpHandler().RespondJson(
            """{"access_token":"at2","refresh_token":"rt2","expires_in":3600}""");

        var tokens = await Client(handler).RefreshAsync("rt", Cancellation);

        Assert.Equal("rt2", tokens.RefreshToken);
    }

    [Fact]
    public async Task Refresh_ExpiryIsMeasuredFromNow()
    {
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var handler = new ScriptedHttpHandler().RespondJson("""{"access_token":"at","expires_in":3600}""");

        var tokens = await Client(handler, time: new FakeTimeProvider(now)).RefreshAsync("rt", Cancellation);

        Assert.Equal(now.AddHours(1), tokens.ExpiresAtUtc);
    }

    [Fact]
    public async Task Refresh_InvalidGrantAsksForAReconnectRatherThanARetry()
    {
        var handler = new ScriptedHttpHandler()
            .Respond(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(
            () => Client(handler).RefreshAsync("rt", Cancellation));
    }

    [Fact]
    public async Task Refresh_ServerErrorIsATransportFailure()
    {
        var handler = new ScriptedHttpHandler()
            .Respond(HttpStatusCode.ServiceUnavailable, """{"error":"backend_error"}""");

        await Assert.ThrowsAsync<IOException>(() => Client(handler).RefreshAsync("rt", Cancellation));
    }

    [Fact]
    public void Constructor_RejectsABuildWithNoClientId() =>
        Assert.Throws<ArgumentException>(() => new GoogleOAuthClient(
            new HttpClient(new ScriptedHttpHandler()),
            new GoogleOAuthClientCredentials(string.Empty, "secret")));

    private static GoogleOAuthClient Client(
        ScriptedHttpHandler handler,
        GoogleOAuthClientCredentials? credentials = null,
        TimeProvider? time = null) =>
        new(new HttpClient(handler), credentials ?? Desktop, timeProvider: time);
}

/// <summary>A clock the tests move by hand.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
