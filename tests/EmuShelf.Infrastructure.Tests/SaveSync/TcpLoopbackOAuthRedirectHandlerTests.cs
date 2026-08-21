using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

// The Android loopback redirect handler is built on TcpListener (HttpListener is unsupported on
// Android), so it can be exercised on the dev host: drive a real HTTP GET at its loopback port and
// assert it parses the redirect exactly as the desktop handler does.
public sealed class TcpLoopbackOAuthRedirectHandlerTests
{
    private static readonly HttpClient Http = new();

    [Fact]
    public async Task ReturnsTheAuthorizationCode_WhenStateMatches()
    {
        using var handler = new TcpLoopbackOAuthRedirectHandler();
        var wait = handler.WaitForCodeAsync("the-state");

        var response = await Http.GetAsync($"{handler.RedirectUri}?code=auth-code-123&state=the-state");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("auth-code-123", await wait);
    }

    [Fact]
    public async Task IgnoresFaviconAndPrefetchRequests_ThenAcceptsTheRedirect()
    {
        using var handler = new TcpLoopbackOAuthRedirectHandler();
        var wait = handler.WaitForCodeAsync("s");

        // A parameterless request (favicon/prefetch) must be answered and ignored, not treated as the
        // redirect — otherwise the sign-in fails on a request Google never sent.
        (await Http.GetAsync($"{handler.RedirectUri}favicon.ico")).Dispose();
        (await Http.GetAsync($"{handler.RedirectUri}?code=real&state=s")).Dispose();

        Assert.Equal("real", await wait);
    }

    [Fact]
    public async Task Throws_WhenTheUserDeclines()
    {
        using var handler = new TcpLoopbackOAuthRedirectHandler();
        var wait = handler.WaitForCodeAsync("s");

        (await Http.GetAsync($"{handler.RedirectUri}?error=access_denied&state=s")).Dispose();

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(() => wait);
    }

    [Fact]
    public async Task Throws_WhenTheStateDoesNotMatch()
    {
        using var handler = new TcpLoopbackOAuthRedirectHandler();
        var wait = handler.WaitForCodeAsync("expected-state");

        (await Http.GetAsync($"{handler.RedirectUri}?code=x&state=forged-state")).Dispose();

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(() => wait);
    }

    [Fact]
    public async Task ParsesCode_WhenTheRequestLineArrivesInMultipleTcpSegments()
    {
        // TCP is a stream: the request line can be delivered in pieces. A single un-looped read would
        // truncate the code/state and fail the sign-in with a misleading "did not match" error.
        using var handler = new TcpLoopbackOAuthRedirectHandler();
        var wait = handler.WaitForCodeAsync("s");

        var uri = new Uri(handler.RedirectUri);
        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, uri.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("GET /?code=split-"));
        await stream.FlushAsync();
        await Task.Delay(150);
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("value&state=s HTTP/1.1\r\nHost: localhost\r\n\r\n"));
        await stream.FlushAsync();

        Assert.Equal("split-value", await wait);
    }

    [Fact]
    public async Task ToleratesAJunkConnection_ThenResolvesTheRealRedirect()
    {
        // A local peer that sends an incomplete request line and closes must not abort the sign-in — it
        // is tolerated like a favicon request, and the real redirect on the next connection resolves.
        using var handler = new TcpLoopbackOAuthRedirectHandler();
        var wait = handler.WaitForCodeAsync("s");

        var uri = new Uri(handler.RedirectUri);
        using (var junk = new System.Net.Sockets.TcpClient())
        {
            await junk.ConnectAsync(System.Net.IPAddress.Loopback, uri.Port);
            await junk.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes("GET /partial-no-newline"));
            await junk.GetStream().FlushAsync();
        } // graceful close: the server read sees the partial line, then EOF, and moves on

        (await Http.GetAsync($"{handler.RedirectUri}?code=after-junk&state=s")).Dispose();

        Assert.Equal("after-junk", await wait);
    }

    [Fact]
    public void BindsOnlyLoopback_OnAFreshPerInstanceEphemeralPort()
    {
        using var a = new TcpLoopbackOAuthRedirectHandler();
        using var b = new TcpLoopbackOAuthRedirectHandler();
        Assert.StartsWith("http://127.0.0.1:", a.RedirectUri);
        Assert.NotEqual(a.RedirectUri, b.RedirectUri);
    }

    [Fact]
    public async Task WaitForCode_IsCancellable()
    {
        using var handler = new TcpLoopbackOAuthRedirectHandler();
        using var cts = new CancellationTokenSource();
        var wait = handler.WaitForCodeAsync("s", cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
