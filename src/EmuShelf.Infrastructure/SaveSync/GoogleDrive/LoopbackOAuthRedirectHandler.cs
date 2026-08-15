using System.Net;
using System.Net.Sockets;
using System.Text;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Receives the authorization code Google sends back after the user consents. Abstracted because the
/// mechanism is per-platform: a loopback HTTP listener on desktop, and a custom-scheme activity
/// intent on Android, where nothing can bind a port for the browser to reach.
/// </summary>
public interface IOAuthRedirectHandler : IDisposable
{
    /// <summary>The redirect URI to register in the authorization request.</summary>
    string RedirectUri { get; }

    /// <summary>
    /// Waits for the redirect and returns its authorization code. Throws
    /// <see cref="GoogleAuthorizationRequiredException"/> when the user declined.
    /// </summary>
    Task<string> WaitForCodeAsync(string expectedState, CancellationToken cancellationToken = default);
}

/// <summary>
/// Desktop redirect handler: binds an ephemeral loopback port, serves the one request the browser
/// makes after consent, and shows the user a page telling them to go back to EmuShelf.
/// </summary>
/// <remarks>
/// The port is chosen per sign-in rather than fixed. rclone binds 53682 every time, and an abandoned
/// sign-in holding that port is a documented failure mode with its own exception type and UI copy
/// (DECISIONS 2026-08-06). Google exempts loopback redirects from exact port matching, so nothing
/// has to be registered for this to work — which removes that whole class of failure.
/// </remarks>
public sealed class LoopbackOAuthRedirectHandler : IOAuthRedirectHandler
{
    private readonly HttpListener _listener;
    private readonly IAppLogger _logger;
    private bool _disposed;

    public LoopbackOAuthRedirectHandler(IAppLogger? logger = null)
    {
        _logger = logger ?? NullAppLogger.Instance;

        var port = FindFreePort();
        RedirectUri = $"http://127.0.0.1:{port}/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(RedirectUri);
        _listener.Start();
    }

    public string RedirectUri { get; }

    public async Task<string> WaitForCodeAsync(
        string expectedState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // GetContextAsync ignores cancellation, so cancelling has to stop the listener out from
        // under it; that surfaces as the HttpListenerException handled below.
        await using var registration = cancellationToken.Register(Stop);

        string? error;
        string? code;
        string? state;
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var query = context.Request.QueryString;
            error = query["error"];
            code = query["code"];
            state = query["state"];

            // Browsers ask for /favicon.ico unprompted, and some prefetch a URL before navigating to
            // it. Treating whatever arrives first as the redirect would fail the sign-in with
            // "Google returned no authorization code" for a request Google never sent. Only a request
            // actually carrying the flow's parameters counts; anything else is answered and ignored.
            if (error is null && code is null)
            {
                await TryRespondAsync(context, succeeded: false);
                continue;
            }

            await TryRespondAsync(context, error is null && StateMatches(expectedState, state));
            break;
        }

        if (error is not null)
        {
            throw new GoogleAuthorizationRequiredException(
                string.Equals(error, "access_denied", StringComparison.Ordinal)
                    ? "The Google sign-in was declined."
                    : $"Google refused the sign-in ({error}).");
        }

        if (!StateMatches(expectedState, state))
        {
            // A redirect whose state does not match did not come from the request this app started.
            throw new GoogleAuthorizationRequiredException(
                "The Google sign-in response did not match this request and was ignored.");
        }

        return string.IsNullOrWhiteSpace(code)
            ? throw new GoogleAuthorizationRequiredException("Google returned no authorization code.")
            : code;
    }

    /// <summary>Fixed-time compare: the state is the only thing tying a redirect to this request.</summary>
    private static bool StateMatches(string expected, string? actual) =>
        actual is not null &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));

    private async Task TryRespondAsync(HttpListenerContext context, bool succeeded)
    {
        try
        {
            await RespondAsync(context, succeeded);
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or IOException)
        {
            // The browser closing the tab first is not a failure of the sign-in — the code is
            // already in hand. Only the reply was lost.
            _logger.Warning($"Could not render the sign-in confirmation page: {ex.Message}");
        }
    }

    private static async Task RespondAsync(HttpListenerContext context, bool succeeded)
    {
        var body = Encoding.UTF8.GetBytes(succeeded
            ? "<html><body style='font-family:sans-serif;text-align:center;padding-top:3em'>" +
              "<h2>EmuShelf is connected</h2><p>You can close this tab and go back to EmuShelf.</p></body></html>"
            : "<html><body style='font-family:sans-serif;text-align:center;padding-top:3em'>" +
              "<h2>Sign-in was not completed</h2><p>Go back to EmuShelf and try again.</p></body></html>");

        context.Response.StatusCode = succeeded ? 200 : 400;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }

    /// <summary>
    /// Binds port 0 to let the OS pick a free port, then releases it. A short race with another
    /// process is possible and acceptable — the listener would simply fail to start and the user
    /// retries — whereas a fixed port fails the same way every time.
    /// </summary>
    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
        }
    }

    private void Stop()
    {
        try
        {
            if (_listener.IsListening)
                _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
