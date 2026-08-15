using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

/// <summary>
/// Drives the real loopback listener over real HTTP. The browser is the untrusted party here — it
/// decides what it requests and in what order — so these exercise what a browser actually does rather
/// than what the happy path assumes.
/// </summary>
public sealed class LoopbackOAuthRedirectHandlerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task WaitForCode_ReturnsTheCodeFromTheRedirect()
    {
        using var handler = new LoopbackOAuthRedirectHandler();
        using var cancellation = new CancellationTokenSource(Timeout);

        var waiting = handler.WaitForCodeAsync("the-state", cancellation.Token);
        await GetAsync($"{handler.RedirectUri}?code=the-code&state=the-state", cancellation.Token);

        Assert.Equal("the-code", await waiting);
    }

    [Fact]
    public async Task WaitForCode_IgnoresABrowserSideRequestThatIsNotTheRedirect()
    {
        // Browsers fetch /favicon.ico unprompted, and some prefetch the URL before navigating.
        // Treating the first request that arrives as the redirect fails the sign-in for no reason.
        using var handler = new LoopbackOAuthRedirectHandler();
        using var cancellation = new CancellationTokenSource(Timeout);

        var waiting = handler.WaitForCodeAsync("the-state", cancellation.Token);
        await GetAsync($"{handler.RedirectUri}favicon.ico", cancellation.Token);
        await GetAsync($"{handler.RedirectUri}?code=the-code&state=the-state", cancellation.Token);

        Assert.Equal("the-code", await waiting);
    }

    [Fact]
    public async Task WaitForCode_SurfacesADeclinedSignIn()
    {
        using var handler = new LoopbackOAuthRedirectHandler();
        using var cancellation = new CancellationTokenSource(Timeout);

        var waiting = handler.WaitForCodeAsync("the-state", cancellation.Token);
        await GetAsync($"{handler.RedirectUri}?error=access_denied&state=the-state", cancellation.Token);

        var failure = await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(() => waiting);
        Assert.Contains("declined", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WaitForCode_RejectsARedirectWhoseStateDoesNotMatch()
    {
        // A redirect carrying someone else's state did not come from the request this app started.
        using var handler = new LoopbackOAuthRedirectHandler();
        using var cancellation = new CancellationTokenSource(Timeout);

        var waiting = handler.WaitForCodeAsync("the-state", cancellation.Token);
        await GetAsync($"{handler.RedirectUri}?code=the-code&state=other-state", cancellation.Token);

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(() => waiting);
    }

    [Fact]
    public async Task WaitForCode_ObservesCancellation()
    {
        using var handler = new LoopbackOAuthRedirectHandler();
        using var cancellation = new CancellationTokenSource();

        var waiting = handler.WaitForCodeAsync("the-state", cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    [Fact]
    public void RedirectUri_IsLoopbackOnlyAndDiffersPerSignIn()
    {
        using var first = new LoopbackOAuthRedirectHandler();
        using var second = new LoopbackOAuthRedirectHandler();

        Assert.StartsWith("http://127.0.0.1:", first.RedirectUri, StringComparison.Ordinal);
        Assert.NotEqual(first.RedirectUri, second.RedirectUri);
    }

    private static async Task GetAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(url, cancellationToken);
        _ = await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
