using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// A loopback OAuth redirect handler built on a raw <see cref="TcpListener"/> rather than
/// <see cref="HttpListener"/>. Android can bind a loopback TCP port with no permission and the system
/// browser's redirect to <c>http://127.0.0.1:port/</c> reaches it — but .NET does not support
/// <see cref="HttpListener"/> on Android, so the same loopback flow the desktop uses needs a listener
/// built from the sockets API that is supported everywhere. This lets Android reuse the desktop OAuth
/// client and its loopback redirect (Google exempts loopback from exact-port matching) instead of a
/// second client with a custom URI scheme. See docs/android-save-sync-model.md.
/// </summary>
public sealed class TcpLoopbackOAuthRedirectHandler : IOAuthRedirectHandler
{
    private readonly TcpListener _listener;
    private readonly IAppLogger _logger;
    private bool _disposed;

    public TcpLoopbackOAuthRedirectHandler(IAppLogger? logger = null)
    {
        _logger = logger ?? NullAppLogger.Instance;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        RedirectUri = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
    }

    public string RedirectUri { get; }

    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string? error;
        string? code;
        string? state;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            using (client)
            {
                var query = ParseQuery(await ReadRequestTargetAsync(client, cancellationToken));
                error = query.GetValueOrDefault("error");
                code = query.GetValueOrDefault("code");
                state = query.GetValueOrDefault("state");

                // Browsers request /favicon.ico unprompted and some prefetch before navigating. Only a
                // request actually carrying the flow's parameters is the redirect; answer and ignore the
                // rest rather than failing the sign-in on a request Google never sent.
                if (error is null && code is null)
                {
                    await TryRespondAsync(client, succeeded: false);
                    continue;
                }

                await TryRespondAsync(client, error is null && StateMatches(expectedState, state));
                break;
            }
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
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));

    // Longest we will read for a request line before giving up on a peer, and how long we will wait on a
    // single connection. Bounds both a malformed/oversized request and a stalled local peer: on Android
    // any app shares 127.0.0.1, so a connection that opens and never sends must not hold the accept loop.
    private const int MaxRequestLineBytes = 16 * 1024;
    private static readonly TimeSpan PerConnectionReadTimeout = TimeSpan.FromSeconds(10);

    // The request target is the second token of the HTTP request line ("GET /?code=… HTTP/1.1"). TCP is a
    // stream, so the line can arrive in pieces — read until a newline is seen (or the cap/deadline is
    // hit). This never throws except for real (outer-token) cancellation: a per-connection timeout or a
    // read error yields "/", which the caller treats as a junk connection and tolerates like a favicon
    // request, rather than aborting the whole sign-in.
    private static async Task<string> ReadRequestTargetAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(PerConnectionReadTimeout);
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[8192];
            var accumulated = new StringBuilder();
            while (accumulated.Length < MaxRequestLineBytes)
            {
                var read = await stream.ReadAsync(buffer, deadline.Token);
                if (read <= 0)
                    break;
                accumulated.Append(Encoding.ASCII.GetString(buffer, 0, read));
                var newline = accumulated.ToString().IndexOf('\n');
                if (newline >= 0)
                {
                    var firstLine = accumulated.ToString(0, newline).TrimEnd('\r');
                    var tokens = firstLine.Split(' ');
                    return tokens.Length >= 2 ? tokens[1] : "/";
                }
            }
            return "/";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            // Per-connection timeout or a broken/reset peer — not the redirect. Tolerate and move on.
            return "/";
        }
    }

    private static Dictionary<string, string> ParseQuery(string requestTarget)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var mark = requestTarget.IndexOf('?');
        if (mark < 0)
            return result;
        foreach (var pair in requestTarget[(mark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0)
                continue;
            result[WebUtility.UrlDecode(pair[..equals])] = WebUtility.UrlDecode(pair[(equals + 1)..]);
        }
        return result;
    }

    private async Task TryRespondAsync(TcpClient client, bool succeeded)
    {
        try
        {
            var body = Encoding.UTF8.GetBytes(succeeded
                ? "<html><body style='font-family:sans-serif;text-align:center;padding-top:3em'>" +
                  "<h2>EmuShelf is connected</h2><p>You can close this tab and go back to EmuShelf.</p></body></html>"
                : "<html><body style='font-family:sans-serif;text-align:center;padding-top:3em'>" +
                  "<h2>Sign-in was not completed</h2><p>Go back to EmuShelf and try again.</p></body></html>");
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(succeeded ? "200 OK" : "400 Bad Request")}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");

            // Deliberately not cancellable: this runs after the authorization code is in hand, so a
            // cancellation racing the confirmation page must not discard a code we already have. The
            // desktop handler writes without a token for the same reason.
            var stream = client.GetStream();
            await stream.WriteAsync(header);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // The browser closing the tab first is not a failure — the code is already in hand; only
            // the confirmation page was lost.
            _logger.Warning($"Could not render the sign-in confirmation page: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _listener.Stop();
    }
}
