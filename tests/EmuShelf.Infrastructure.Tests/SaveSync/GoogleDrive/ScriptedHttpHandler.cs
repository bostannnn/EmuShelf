using System.Diagnostics;
using System.Net;
using System.Text;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

/// <summary>
/// A queue of canned responses plus a transcript of what was asked for. Scripted rather than a
/// single stub because every Drive call here is a sequence — resolve a folder, then list it, then
/// fetch — and the order is what the tests are checking.
/// </summary>
internal sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public ScriptedHttpHandler Respond(HttpStatusCode status, string? json = null)
    {
        _responses.Enqueue((_, _) => Task.FromResult(Json(status, json)));
        return this;
    }

    /// <summary>
    /// A request that never answers: it blocks until its own cancellation token trips, modelling a
    /// connection that has gone silent. The client's per-attempt timeout is what cancels it.
    /// </summary>
    public ScriptedHttpHandler Stall()
    {
        _responses.Enqueue(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });
        return this;
    }

    public ScriptedHttpHandler RespondJson(string json) => Respond(HttpStatusCode.OK, json);

    public ScriptedHttpHandler RespondBytes(byte[] content)
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        }));
        return this;
    }

    /// <summary>
    /// A 200 whose body hands back <paramref name="prefix"/> and then goes silent — every later read
    /// blocks until cancelled. Models a download that stalls mid-stream, which only the read-side idle
    /// timeout can break: the response headers already arrived, so no request-level timeout covers it.
    /// </summary>
    public ScriptedHttpHandler RespondStreamThenStall(byte[] prefix)
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallAfterPrefixStream(prefix)),
        }));
        return this;
    }

    public ScriptedHttpHandler RespondResumableSession(string location)
    {
        _responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Location = new Uri(location, UriKind.Absolute);
            return Task.FromResult(response);
        });
        return this;
    }

    /// <summary>
    /// Drive's "chunk received, send the next one" reply. <paramref name="persistedBytes"/> models the
    /// <c>Range</c> header it uses to say how much it actually stored, which can be less than was sent.
    /// </summary>
    public ScriptedHttpHandler RespondResumeIncomplete(long? persistedBytes = null)
    {
        _responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)308);
            if (persistedBytes is { } stored)
                response.Headers.TryAddWithoutValidation("Range", $"bytes=0-{stored - 1}");
            return Task.FromResult(response);
        });
        return this;
    }

    public ScriptedHttpHandler RespondRetryable(HttpStatusCode status, TimeSpan? retryAfter = null)
    {
        _responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            if (retryAfter is { } delta)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delta);
            return Task.FromResult(response);
        });
        return this;
    }

    /// <summary>A retryable response whose <c>Retry-After</c> is an absolute HTTP-date rather than a delta.</summary>
    public ScriptedHttpHandler RespondRetryableAt(HttpStatusCode status, DateTimeOffset retryAfter)
    {
        _responses.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
            return Task.FromResult(response);
        });
        return this;
    }

    public ScriptedHttpHandler Throw(Exception exception)
    {
        _responses.Enqueue((_, _) => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.Parameter,
            body,
            request.Content?.Headers.ContentRange?.ToString()));

        if (_responses.Count == 0)
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}");

        return await _responses.Dequeue()(request, cancellationToken);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string? json) =>
        new(status)
        {
            Content = new StringContent(json ?? "{}", Encoding.UTF8, "application/json"),
        };

    /// <summary>Yields a fixed prefix once, then blocks every read until the read's token is cancelled.</summary>
    private sealed class StallAfterPrefixStream(byte[] prefix) : Stream
    {
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset < prefix.Length)
            {
                var count = Math.Min(buffer.Length, prefix.Length - _offset);
                prefix.AsMemory(_offset, count).CopyTo(buffer);
                _offset += count;
                return count;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_offset >= prefix.Length)
                throw new NotSupportedException("The async read path is what the idle timeout guards.");

            var n = Math.Min(count, prefix.Length - _offset);
            Array.Copy(prefix, _offset, buffer, offset, n);
            _offset += n;
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? BearerToken,
        byte[]? Body,
        string? ContentRange)
    {
        public string BodyText => Body is null ? string.Empty : Encoding.UTF8.GetString(Body);
    }
}

/// <summary>Hands out a fixed token and counts forced refreshes.</summary>
internal sealed class StubTokenSource(string token = "token-1") : IGoogleAccessTokenSource
{
    public int ForcedRefreshes { get; private set; }

    public string Token { get; set; } = token;

    public Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (forceRefresh)
        {
            ForcedRefreshes++;
            Token = "token-" + (ForcedRefreshes + 1);
        }

        return Task.FromResult(Token);
    }
}
