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
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public ScriptedHttpHandler Respond(HttpStatusCode status, string? json = null)
    {
        _responses.Enqueue(_ => Json(status, json));
        return this;
    }

    public ScriptedHttpHandler RespondJson(string json) => Respond(HttpStatusCode.OK, json);

    public ScriptedHttpHandler RespondBytes(byte[] content)
    {
        _responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        });
        return this;
    }

    public ScriptedHttpHandler RespondResumableSession(string location)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Location = new Uri(location, UriKind.Absolute);
            return response;
        });
        return this;
    }

    /// <summary>
    /// Drive's "chunk received, send the next one" reply. <paramref name="persistedBytes"/> models the
    /// <c>Range</c> header it uses to say how much it actually stored, which can be less than was sent.
    /// </summary>
    public ScriptedHttpHandler RespondResumeIncomplete(long? persistedBytes = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)308);
            if (persistedBytes is { } stored)
                response.Headers.TryAddWithoutValidation("Range", $"bytes=0-{stored - 1}");
            return response;
        });
        return this;
    }

    public ScriptedHttpHandler RespondRetryable(HttpStatusCode status, TimeSpan? retryAfter = null)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            if (retryAfter is { } delta)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delta);
            return response;
        });
        return this;
    }

    /// <summary>A retryable response whose <c>Retry-After</c> is an absolute HTTP-date rather than a delta.</summary>
    public ScriptedHttpHandler RespondRetryableAt(HttpStatusCode status, DateTimeOffset retryAfter)
    {
        _responses.Enqueue(_ =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
            return response;
        });
        return this;
    }

    public ScriptedHttpHandler Throw(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
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

        return _responses.Dequeue()(request);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string? json) =>
        new(status)
        {
            Content = new StringContent(json ?? "{}", Encoding.UTF8, "application/json"),
        };

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
