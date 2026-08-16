using System.Net;
using System.Text;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

/// <summary>
/// Retrying an upload is the main thing a hand-written Drive client has to get right — a rate-limited
/// provider returns 429 on exactly the large sync that most needs to survive it. These pin that the
/// body is re-sent intact rather than the retry failing on a spent stream.
/// </summary>
public sealed class GoogleDriveUploadRetryTests
{
    private static CancellationToken Cancellation => CancellationToken.None;

    [Fact]
    public async Task Upload_Create_RetriesAfterRateLimitAndResendsTheWholeBody()
    {
        var handler = new ScriptedHttpHandler()
            .RespondRetryable(HttpStatusCode.TooManyRequests)
            .RespondJson("""{"id":"new-file"}""");

        var id = await Client(handler).UploadAsync(
            "folder-1", "pcsx2.payload", null, Stream("the-save-bytes"), null, Cancellation);

        Assert.Equal("new-file", id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("the-save-bytes", handler.Requests[1].BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_Replace_RetriesAfterServerErrorAndResendsTheWholeBody()
    {
        var handler = new ScriptedHttpHandler()
            .RespondRetryable(HttpStatusCode.ServiceUnavailable)
            .RespondJson("""{"id":"existing"}""");

        var id = await Client(handler).UploadAsync(
            "folder-1", "pcsx2.payload", "existing", Stream("the-save-bytes"), null, Cancellation);

        Assert.Equal("existing", id);
        Assert.Equal("the-save-bytes", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task Upload_RetriesAfterATransportFailure()
    {
        var handler = new ScriptedHttpHandler()
            .Throw(new HttpRequestException("connection reset"))
            .RespondJson("""{"id":"new-file"}""");

        var id = await Client(handler).UploadAsync(
            "folder-1", "pcsx2.payload", null, Stream("the-save-bytes"), null, Cancellation);

        Assert.Equal("new-file", id);
    }

    [Fact]
    public async Task Upload_LeavesTheCallersStreamUsableForItsOwnDisposal()
    {
        // The caller owns the stream. If the client disposes it, a caller that reads or seeks after
        // a successful upload gets an ObjectDisposedException from code that did nothing wrong.
        var handler = new ScriptedHttpHandler().RespondJson("""{"id":"new-file"}""");
        var content = Stream("the-save-bytes");

        await Client(handler).UploadAsync("folder-1", "pcsx2.payload", null, content, null, Cancellation);

        Assert.Equal(0, content.Seek(0, SeekOrigin.Begin));
    }

    [Fact]
    public async Task Upload_Resumable_ResumesFromWhatDriveSaysItStoredNotWhatWasSent()
    {
        // Drive can persist less than a chunk. Advancing by the chunk length would skip the
        // remainder and write a save that is silently corrupt but reports success.
        var content = new byte[GoogleDriveApiClient.ResumableChunkBytes + 4096];
        Random.Shared.NextBytes(content);
        const long stored = GoogleDriveApiClient.ResumableChunkBytes - 1024;
        var handler = new ScriptedHttpHandler()
            .RespondResumableSession("https://upload.example/session-1")
            .RespondResumeIncomplete(persistedBytes: stored)
            .RespondJson("""{"id":"big-file"}""");

        await Client(handler).UploadAsync(
            "folder-1", "rpcs3.payload", null, new MemoryStream(content), null, Cancellation);

        Assert.Equal($"bytes {stored}-{content.Length - 1}/{content.Length}", handler.Requests[2].ContentRange);
    }

    [Fact]
    public async Task Upload_Resumable_FallsBackToTheChunkLengthWhenDriveSendsNoRange()
    {
        var content = new byte[GoogleDriveApiClient.ResumableChunkBytes + 4096];
        var handler = new ScriptedHttpHandler()
            .RespondResumableSession("https://upload.example/session-1")
            .RespondResumeIncomplete()
            .RespondJson("""{"id":"big-file"}""");

        await Client(handler).UploadAsync(
            "folder-1", "rpcs3.payload", null, new MemoryStream(content), null, Cancellation);

        Assert.Equal(
            $"bytes {GoogleDriveApiClient.ResumableChunkBytes}-{content.Length - 1}/{content.Length}",
            handler.Requests[2].ContentRange);
    }

    private static GoogleDriveApiClient Client(ScriptedHttpHandler handler) =>
        new(
            new HttpClient(handler),
            new StubTokenSource(),
            logger: null,
            delay: (_, _) => Task.CompletedTask);

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));
}
