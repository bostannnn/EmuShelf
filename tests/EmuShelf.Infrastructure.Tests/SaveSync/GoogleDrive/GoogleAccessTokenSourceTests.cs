using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

public sealed class GoogleAccessTokenSourceTests
{
    private static CancellationToken Cancellation => CancellationToken.None;

    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAccessToken_MintsOnceAndThenServesTheCachedToken()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"access_token":"at","expires_in":3600}""");
        var source = Source(handler, out _);

        Assert.Equal("at", await source.GetAccessTokenAsync(cancellationToken: Cancellation));
        Assert.Equal("at", await source.GetAccessTokenAsync(cancellationToken: Cancellation));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetAccessToken_ForceRefreshMintsAgain()
    {
        var handler = new ScriptedHttpHandler()
            .RespondJson("""{"access_token":"at","expires_in":3600}""")
            .RespondJson("""{"access_token":"at2","expires_in":3600}""");
        var source = Source(handler, out _);

        await source.GetAccessTokenAsync(cancellationToken: Cancellation);

        Assert.Equal("at2", await source.GetAccessTokenAsync(forceRefresh: true, Cancellation));
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetAccessToken_RenewsBeforeTheStatedExpiry()
    {
        // The skew is the point: a token that expires mid-upload costs a retry of whatever was in
        // flight, so it is renewed while it is still technically valid.
        var handler = new ScriptedHttpHandler()
            .RespondJson("""{"access_token":"at","expires_in":3600}""")
            .RespondJson("""{"access_token":"at2","expires_in":3600}""");
        var time = new FakeTimeProvider(Now);
        var source = Source(handler, out _, time);

        await source.GetAccessTokenAsync(cancellationToken: Cancellation);
        time.Now = Now.AddHours(1) - GoogleOAuthClient.ExpirySkew;

        Assert.Equal("at2", await source.GetAccessTokenAsync(cancellationToken: Cancellation));
    }

    [Fact]
    public async Task GetAccessToken_WithNoConnectedAccountAsksForOne()
    {
        var source = Source(new ScriptedHttpHandler(), out var store);
        store.Clear();

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(
            () => source.GetAccessTokenAsync(cancellationToken: Cancellation));
    }

    [Fact]
    public async Task GetAccessToken_PersistsARotatedRefreshToken()
    {
        var handler = new ScriptedHttpHandler().RespondJson(
            """{"access_token":"at","refresh_token":"rotated","expires_in":3600}""");
        var source = Source(handler, out var store);

        await source.GetAccessTokenAsync(cancellationToken: Cancellation);

        Assert.Equal("rotated", store.Read());
    }

    [Fact]
    public async Task GetAccessToken_ConcurrentCallersShareOneRefresh()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"access_token":"at","expires_in":3600}""");
        var source = Source(handler, out _);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => source.GetAccessTokenAsync(cancellationToken: Cancellation)));

        Assert.All(tokens, token => Assert.Equal("at", token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Adopt_UsesTheSignInTokensWithoutARefreshCall()
    {
        var handler = new ScriptedHttpHandler();
        var source = Source(handler, out var store);

        source.Adopt(new GoogleTokens("fresh", "new-refresh", Now.AddHours(1)));

        Assert.Equal("fresh", await source.GetAccessTokenAsync(cancellationToken: Cancellation));
        Assert.Equal("new-refresh", store.Read());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Disconnect_ForgetsTheAccount()
    {
        var source = Source(new ScriptedHttpHandler(), out var store);

        source.Disconnect();

        Assert.Null(store.Read());
        Assert.False(source.IsConnected);
    }

    private static GoogleAccessTokenSource Source(
        ScriptedHttpHandler handler,
        out InMemoryTokenStore store,
        TimeProvider? time = null)
    {
        store = new InMemoryTokenStore { Value = "stored-refresh" };
        var oauth = new GoogleOAuthClient(
            new HttpClient(handler),
            new GoogleOAuthClientCredentials("client-id", "client-secret"),
            timeProvider: time ?? new FakeTimeProvider(Now));
        return new GoogleAccessTokenSource(oauth, store, time ?? new FakeTimeProvider(Now));
    }

    private sealed class InMemoryTokenStore : IGoogleDriveTokenStore
    {
        public string? Value { get; set; }

        public string? Read() => Value;

        public void Write(string refreshToken) => Value = refreshToken;

        public void Clear() => Value = null;
    }
}
