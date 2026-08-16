using System.Net;
using System.Text;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

public sealed class GoogleDriveApiClientTests
{
    private static CancellationToken Cancellation => CancellationToken.None;

    [Fact]
    public async Task ListChildren_FollowsPagingToTheEnd()
    {
        var handler = new ScriptedHttpHandler()
            .RespondJson("""{"files":[{"id":"1","name":"a"}],"nextPageToken":"page-2"}""")
            .RespondJson("""{"files":[{"id":"2","name":"b"}]}""");

        var files = await Client(handler).ListChildrenAsync("folder-1", Cancellation);

        Assert.Equal(["1", "2"], files.Select(file => file.Id));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("pageToken=page-2", handler.Requests[1].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListChildren_ScopesTheQueryToTheFolderAndExcludesTrash()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"files":[]}""");

        await Client(handler).ListChildrenAsync("folder-1", Cancellation);

        var query = Uri.UnescapeDataString(handler.Requests[0].Uri.Query);
        Assert.Contains("'folder-1' in parents", query, StringComparison.Ordinal);
        Assert.Contains("trashed=false", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListChildren_SendsTheBearerToken()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"files":[]}""");

        await Client(handler).ListChildrenAsync("folder-1", Cancellation);

        Assert.Equal("token-1", handler.Requests[0].BearerToken);
    }

    [Fact]
    public async Task FindChild_PrefersTheOldestOfDuplicateNames()
    {
        // Drive permits two files with the same name in one folder. Which one is returned must not
        // depend on listing order, or two machines would write to different blobs.
        var handler = new ScriptedHttpHandler().RespondJson(
            """
            {"files":[
              {"id":"new","name":"index.json","modifiedTime":"2026-08-15T12:00:00Z"},
              {"id":"old","name":"index.json","modifiedTime":"2026-01-01T12:00:00Z"}
            ]}
            """);

        var found = await Client(handler).FindChildAsync("folder-1", "index.json", null, Cancellation);

        Assert.Equal("old", found!.Id);
    }

    [Fact]
    public async Task FindChild_FiltersByFolderness()
    {
        var handler = new ScriptedHttpHandler().RespondJson(
            $$"""
            {"files":[
              {"id":"file","name":"pcsx2","mimeType":"application/octet-stream"},
              {"id":"dir","name":"pcsx2","mimeType":"{{DriveFile.FolderMimeType}}"}
            ]}
            """);

        var found = await Client(handler).FindChildAsync("folder-1", "pcsx2", isFolder: true, Cancellation);

        Assert.Equal("dir", found!.Id);
    }

    [Fact]
    public async Task ResolveFolderPath_WalksExistingSegmentsWithoutCreating()
    {
        var handler = new ScriptedHttpHandler()
            .RespondJson($$"""{"files":[{"id":"a","name":"EmuShelf","mimeType":"{{DriveFile.FolderMimeType}}"}]}""")
            .RespondJson($$"""{"files":[{"id":"b","name":"Saves","mimeType":"{{DriveFile.FolderMimeType}}"}]}""");

        var folderId = await Client(handler).ResolveFolderPathAsync("root", "EmuShelf/Saves", create: false, Cancellation);

        Assert.Equal("b", folderId);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task ResolveFolderPath_ReturnsNullWhenASegmentIsMissingAndCreateIsOff()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"files":[]}""");

        var folderId = await Client(handler).ResolveFolderPathAsync("root", "EmuShelf/Saves", create: false, Cancellation);

        Assert.Null(folderId);
    }

    [Fact]
    public async Task ResolveFolderPath_CreatesMissingSegmentsWhenAsked()
    {
        var handler = new ScriptedHttpHandler()
            .RespondJson("""{"files":[]}""")
            .RespondJson("""{"id":"created-1"}""")
            .RespondJson("""{"files":[]}""")
            .RespondJson("""{"id":"created-2"}""");

        var folderId = await Client(handler).ResolveFolderPathAsync("root", "EmuShelf/Saves", create: true, Cancellation);

        Assert.Equal("created-2", folderId);
        Assert.Contains("\"name\":\"EmuShelf\"", handler.Requests[1].BodyText, StringComparison.Ordinal);
        Assert.Contains(DriveFile.FolderMimeType, handler.Requests[1].BodyText, StringComparison.Ordinal);
        Assert.Contains("\"parents\":[\"created-1\"]", handler.Requests[3].BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_ReturnsTheContentStream()
    {
        var handler = new ScriptedHttpHandler().RespondBytes("save-bytes"u8.ToArray());

        await using var stream = await Client(handler).DownloadAsync("file-1", Cancellation);

        using var reader = new StreamReader(stream!);
        Assert.Equal("save-bytes", await reader.ReadToEndAsync(Cancellation));
        Assert.Contains("alt=media", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_ReturnsNullWhenTheBlobIsGone()
    {
        // An index entry can outlive its payload. That is a recoverable condition the transport
        // repairs, not a failure that should stop the whole sync.
        var handler = new ScriptedHttpHandler().Respond(HttpStatusCode.NotFound);

        Assert.Null(await Client(handler).DownloadAsync("file-1", Cancellation));
    }

    [Fact]
    public async Task Upload_SmallPayload_CreatesWithMultipartMetadata()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"id":"new-file"}""");

        var id = await Client(handler).UploadAsync(
            "folder-1", "pcsx2.payload", null, Stream("hello"), null, Cancellation);

        Assert.Equal("new-file", id);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("uploadType=multipart", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("\"parents\":[\"folder-1\"]", handler.Requests[0].BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_ExistingFile_ReplacesContentInPlace()
    {
        var handler = new ScriptedHttpHandler().RespondJson("""{"id":"existing"}""");

        var id = await Client(handler).UploadAsync(
            "folder-1", "pcsx2.payload", "existing", Stream("hello"), null, Cancellation);

        Assert.Equal("existing", id);
        Assert.Equal(HttpMethod.Patch, handler.Requests[0].Method);
        Assert.Contains("files/existing", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("uploadType=media", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Equal("hello", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task Upload_LargePayload_UsesAResumableSessionInChunks()
    {
        var content = new byte[GoogleDriveApiClient.ResumableChunkBytes + 1024];
        Random.Shared.NextBytes(content);
        var handler = new ScriptedHttpHandler()
            .RespondResumableSession("https://upload.example/session-1")
            .RespondResumeIncomplete()
            .RespondJson("""{"id":"big-file"}""");

        var reported = new List<long>();
        var id = await Client(handler).UploadAsync(
            "folder-1",
            "rpcs3.payload",
            null,
            new MemoryStream(content),
            new Progress<long>(reported.Add),
            Cancellation);

        Assert.Equal("big-file", id);
        Assert.Contains("uploadType=resumable", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Equal(new Uri("https://upload.example/session-1"), handler.Requests[1].Uri);
        Assert.Equal(
            $"bytes 0-{GoogleDriveApiClient.ResumableChunkBytes - 1}/{content.Length}",
            handler.Requests[1].ContentRange);
        Assert.Equal(
            $"bytes {GoogleDriveApiClient.ResumableChunkBytes}-{content.Length - 1}/{content.Length}",
            handler.Requests[2].ContentRange);
    }

    [Fact]
    public async Task Send_RetriesOn429AndHonoursRetryAfter()
    {
        var handler = new ScriptedHttpHandler()
            .RespondRetryable(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(7))
            .RespondJson("""{"files":[]}""");
        var delays = new List<TimeSpan>();

        await Client(handler, delays).ListChildrenAsync("folder-1", Cancellation);

        Assert.Equal([TimeSpan.FromSeconds(7)], delays);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Send_RetriesOnServerErrorAndOnTransportFailure()
    {
        var handler = new ScriptedHttpHandler()
            .RespondRetryable(HttpStatusCode.ServiceUnavailable)
            .Throw(new HttpRequestException("connection reset"))
            .RespondJson("""{"files":[]}""");
        var delays = new List<TimeSpan>();

        await Client(handler, delays).ListChildrenAsync("folder-1", Cancellation);

        Assert.Equal(2, delays.Count);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Send_RetriesOn403RateLimit()
    {
        // Drive uses 403 for rate limiting too; the reason in the body is the only thing that says so.
        var handler = new ScriptedHttpHandler()
            .Respond((HttpStatusCode)403, """{"error":{"errors":[{"reason":"userRateLimitExceeded"}],"code":403,"message":"Rate Limit Exceeded"}}""")
            .RespondJson("""{"files":[]}""");
        var delays = new List<TimeSpan>();

        await Client(handler, delays).ListChildrenAsync("folder-1", Cancellation);

        Assert.Single(delays);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Send_ExhaustedRateLimit403SurfacesAsRetryableFailureNotReconnect()
    {
        var handler = new ScriptedHttpHandler();
        for (var i = 0; i < 5; i++)
        {
            handler.Respond(
                (HttpStatusCode)403,
                """{"error":{"errors":[{"reason":"userRateLimitExceeded"}],"message":"Rate Limit Exceeded"}}""");
        }
        var delays = new List<TimeSpan>();

        // Exact IOException, not GoogleAuthorizationRequiredException (a subclass), so the user is not
        // told to reconnect an account that is perfectly fine.
        await Assert.ThrowsAsync<IOException>(() => Client(handler, delays).ListChildrenAsync("folder-1", Cancellation));

        Assert.Equal(4, delays.Count);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task Send_TreatsAPermission403AsReconnect()
    {
        var handler = new ScriptedHttpHandler().Respond(
            (HttpStatusCode)403,
            """{"error":{"errors":[{"reason":"insufficientPermissions"}],"code":403,"message":"Insufficient Permission"}}""");

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(
            () => Client(handler).ListChildrenAsync("folder-1", Cancellation));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Send_TreatsAStorageQuota403AsAnOrdinaryFailure()
    {
        var handler = new ScriptedHttpHandler().Respond(
            (HttpStatusCode)403,
            """{"error":{"errors":[{"reason":"storageQuotaExceeded"}],"code":403,"message":"The user's Drive storage quota has been exceeded."}}""");

        var failure = await Assert.ThrowsAsync<IOException>(
            () => Client(handler).ListChildrenAsync("folder-1", Cancellation));
        Assert.Contains("space", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{\"error\":\"not-an-object\"}")]
    [InlineData("{\"error\":{\"message\":123}}")]
    [InlineData("<html>502 Bad Gateway</html>")]
    public void ParseError_WithMalformedOrWrongShapeBody_ReturnsNoDetailInsteadOfThrowing(string body)
    {
        // Valid-JSON-but-wrong-shape bodies (a bare scalar/array root, or a numeric "message") must not
        // escape as InvalidOperationException — the whole point of reading the body is to classify a
        // 403, and that classification runs unguarded on the retry hot path.
        var (message, reason) = GoogleDriveApiClient.ParseError(body);

        Assert.Equal(string.Empty, message);
        Assert.Null(reason);
    }

    [Fact]
    public async Task Send_MalformedForbiddenBody_MapsToReconnectRatherThanEscaping()
    {
        // A proxy/CDN answering a 403 with a bare JSON array used to throw out of SendAsync's 403
        // branch — neither retried nor classified. It must degrade to the reconnect mapping instead.
        var handler = new ScriptedHttpHandler().Respond((HttpStatusCode)403, "[1,2,3]");

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(
            () => Client(handler).ListChildrenAsync("folder-1", Cancellation));
    }

    [Fact]
    public async Task Send_HonoursADateFormRetryAfter()
    {
        var handler = new ScriptedHttpHandler()
            .RespondRetryableAt(HttpStatusCode.TooManyRequests, DateTimeOffset.UtcNow.AddSeconds(30))
            .RespondJson("""{"files":[]}""");
        var delays = new List<TimeSpan>();

        await Client(handler, delays).ListChildrenAsync("folder-1", Cancellation);

        var delay = Assert.Single(delays);
        Assert.InRange(delay, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(31));
    }

    [Fact]
    public async Task Upload_ResumableThatNeverAdvancesFailsInsteadOfLoopingForever()
    {
        // Two chunks: the first advances to the chunk boundary, the second reports the same persisted
        // count — no forward progress. Without the guard this re-sends the same chunk forever.
        var payload = new byte[GoogleDriveApiClient.ResumableChunkBytes + (1024 * 1024)];
        var handler = new ScriptedHttpHandler()
            .RespondResumableSession("https://upload.example/session")
            .RespondResumeIncomplete(persistedBytes: GoogleDriveApiClient.ResumableChunkBytes)
            .RespondResumeIncomplete(persistedBytes: GoogleDriveApiClient.ResumableChunkBytes);

        await Assert.ThrowsAsync<IOException>(() => Client(handler).UploadAsync(
            "folder-1", "big.payload", null, new MemoryStream(payload), null, Cancellation));

        // Session + two chunks, then it gave up — it did not keep asking for more responses.
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Send_DoesNotRetryAClientError()
    {
        var handler = new ScriptedHttpHandler().Respond(HttpStatusCode.BadRequest, """{"error":{"message":"bad"}}""");

        var failure = await Assert.ThrowsAsync<IOException>(
            () => Client(handler).ListChildrenAsync("folder-1", Cancellation));

        Assert.Contains("bad", failure.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Send_RefreshesTheTokenOnceOnUnauthorized()
    {
        var handler = new ScriptedHttpHandler()
            .Respond(HttpStatusCode.Unauthorized)
            .RespondJson("""{"files":[]}""");
        var tokens = new StubTokenSource();

        await Client(handler, tokens: tokens).ListChildrenAsync("folder-1", Cancellation);

        Assert.Equal(1, tokens.ForcedRefreshes);
        Assert.Equal("token-2", handler.Requests[1].BearerToken);
    }

    [Fact]
    public async Task Send_SurfacesARepeatedUnauthorizedAsReconnect()
    {
        var handler = new ScriptedHttpHandler()
            .Respond(HttpStatusCode.Unauthorized)
            .Respond(HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<GoogleAuthorizationRequiredException>(
            () => Client(handler).ListChildrenAsync("folder-1", Cancellation));
    }

    [Fact]
    public async Task Upload_RejectsANonSeekableStream()
    {
        // A retried request re-sends from the start; a stream that cannot rewind would silently
        // upload a truncated save on the second attempt.
        await Assert.ThrowsAsync<ArgumentException>(() => Client(new ScriptedHttpHandler()).UploadAsync(
            "folder-1", "a.payload", null, new NonSeekableStream(), null, Cancellation));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("it's", @"it\'s")]
    [InlineData(@"back\slash", @"back\\slash")]
    public void EscapeQueryLiteral_EscapesWhatWouldCloseTheLiteral(string value, string expected) =>
        Assert.Equal(expected, GoogleDriveApiClient.EscapeQueryLiteral(value));

    [Fact]
    public void SplitPath_DropsEmptyAndTraversalSegments() =>
        Assert.Equal(["a", "b"], GoogleDriveApiClient.SplitPath("/a//../b/./"));

    [Fact]
    public void BackoffFor_PrefersRetryAfterAndOtherwiseGrowsWithinACap()
    {
        Assert.Equal(TimeSpan.FromSeconds(9), GoogleDriveApiClient.BackoffFor(1, TimeSpan.FromSeconds(9)));
        Assert.True(GoogleDriveApiClient.BackoffFor(1, null) < GoogleDriveApiClient.BackoffFor(4, null));
        Assert.True(GoogleDriveApiClient.BackoffFor(10, null) <= TimeSpan.FromSeconds(16.5));
    }

    private static GoogleDriveApiClient Client(
        ScriptedHttpHandler handler,
        List<TimeSpan>? delays = null,
        StubTokenSource? tokens = null) =>
        new(
            new HttpClient(handler),
            tokens ?? new StubTokenSource(),
            logger: null,
            delay: (duration, _) =>
            {
                delays?.Add(duration);
                return Task.CompletedTask;
            });

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
