using System.Diagnostics;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

/// <summary>
/// A connection that goes silent — no reply, or a body that stops mid-stream — is the failure the
/// rclone transport handled for free and the direct client first did not: it hung on the 5-minute
/// HttpClient ceiling and then surfaced the timeout as a cancellation. These pin that a stall is now
/// retried, that an exhausted stall is an <see cref="IOException"/> and never a bare cancellation, that
/// a genuine caller cancellation still cancels — and that the short per-attempt timeout is what does it,
/// by bounding each test's elapsed time well under the default 100-second backstop.
/// </summary>
public sealed class GoogleDriveStallTimeoutTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    // Far below the default 100s HttpClient.Timeout / 100s per-attempt default, so a test that only
    // passes because a *slow* backstop fired would blow this budget and fail.
    private static readonly TimeSpan MustBeQuick = TimeSpan.FromSeconds(5);

    private static CancellationToken Cancellation => CancellationToken.None;

    [Fact]
    public async Task Request_RetriesAStalledConnectionThenSucceeds()
    {
        var handler = new ScriptedHttpHandler()
            .Stall()
            .RespondJson("""{"files":[]}""");

        var elapsed = Stopwatch.StartNew();
        var files = await Client(handler).ListChildrenAsync("folder-1", Cancellation);
        elapsed.Stop();

        Assert.Empty(files);
        Assert.Equal(2, handler.Requests.Count);
        Assert.True(elapsed.Elapsed < MustBeQuick, $"Took {elapsed.Elapsed}; the per-attempt timeout was not what fired.");
    }

    [Fact]
    public async Task Request_SurfacesAnExhaustedStallAsIOException_NotCancellation()
    {
        // Every attempt stalls. The client must give up as an IOException — the pipeline maps that to
        // a recorded per-platform failure, whereas a bare OperationCanceledException would be mistaken
        // for the user pressing Stop and skip that handling entirely.
        var handler = new ScriptedHttpHandler()
            .Stall().Stall().Stall().Stall().Stall();

        var elapsed = Stopwatch.StartNew();
        var failure = await Assert.ThrowsAsync<IOException>(
            () => Client(handler).ListChildrenAsync("folder-1", Cancellation));
        elapsed.Stop();

        Assert.Contains("stopped responding", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsNotType<OperationCanceledException>(failure);
        Assert.True(elapsed.Elapsed < MustBeQuick, $"Took {elapsed.Elapsed}; the per-attempt timeout was not what fired.");
    }

    [Fact]
    public async Task Request_CallerCancellationStillCancels()
    {
        // A stall the client would otherwise retry, but the caller cancels first: that must propagate
        // as cancellation, promptly — not be converted to IOException, retried away, or left to wait
        // out the (deliberately long) per-attempt timeout. The elapsed bound is what proves the caller
        // token aborts the in-flight request rather than the 30s attempt timeout eventually firing.
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHttpHandler().Stall().Stall().Stall().Stall().Stall();

        var elapsed = Stopwatch.StartNew();
        var pending = Client(handler, timeout: TimeSpan.FromSeconds(30))
            .ListChildrenAsync("folder-1", cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < MustBeQuick, $"Took {elapsed.Elapsed}; the caller cancel did not abort the request.");
    }

    [Fact]
    public async Task Upload_RetriesAStalledConnectionAndResendsTheWholeBody()
    {
        // The stall-retry path rebuilds the request and re-seeks the payload; a body-carrying request
        // (unlike the GET stalls above) must arrive intact on the second attempt.
        var handler = new ScriptedHttpHandler()
            .Stall()
            .RespondJson("""{"id":"new-file"}""");

        var id = await Client(handler).UploadAsync(
            "folder-1", "pcsx2.payload", null, new MemoryStream("the-save-bytes"u8.ToArray()), null, Cancellation);

        Assert.Equal("new-file", id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("the-save-bytes", handler.Requests[1].BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Download_BreaksAStreamThatStallsMidBody_OnTheSynchronousReadPathTheRestoreUses()
    {
        // The save-restore path copies the body synchronously (FileSystemLocalSaveEndpoint runs
        // Stream.Read on a Task.Run thread, never ReadAsync). Guarding only the async path would leave
        // the large-restore hang unbounded, so this drives the SYNCHRONOUS Stream.CopyTo the way the
        // endpoint does and asserts the idle timeout still breaks it.
        var handler = new ScriptedHttpHandler().RespondStreamThenStall("first-chunk"u8.ToArray());
        await using var stream = await Client(handler).DownloadAsync("file-1", Cancellation);

        var elapsed = Stopwatch.StartNew();
        var failure = Assert.Throws<IOException>(() => stream!.CopyTo(System.IO.Stream.Null));
        elapsed.Stop();

        Assert.Contains("stopped sending", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(elapsed.Elapsed < MustBeQuick, $"Took {elapsed.Elapsed}; the sync read path was not idle-bounded.");
    }

    [Fact]
    public async Task Download_BreaksAStreamThatStallsMidBody_OnTheAsyncReadPath()
    {
        var handler = new ScriptedHttpHandler().RespondStreamThenStall("first-chunk"u8.ToArray());
        await using var stream = await Client(handler).DownloadAsync("file-1", Cancellation);

        var failure = await Assert.ThrowsAsync<IOException>(
            () => stream!.CopyToAsync(System.IO.Stream.Null, Cancellation));
        Assert.Contains("stopped sending", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_CallerCancelMidBodyPropagatesAsCancellation_NotAnIdleFailure()
    {
        // The other half of the idle guard: a genuine Stop mid-restore must surface as cancellation,
        // not the "stopped sending" IOException. A long idle budget guarantees the caller cancel — not
        // the idle timer — is what ends the read.
        using var cancellation = new CancellationTokenSource();
        var handler = new ScriptedHttpHandler().RespondStreamThenStall("first-chunk"u8.ToArray());
        await using var stream = await Client(handler, timeout: TimeSpan.FromSeconds(30))
            .DownloadAsync("file-1", cancellation.Token);

        var copy = stream!.CopyToAsync(System.IO.Stream.Null, cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => copy);
    }

    private static GoogleDriveApiClient Client(ScriptedHttpHandler handler, TimeSpan? timeout = null) =>
        new(
            new HttpClient(handler),
            new StubTokenSource(),
            logger: null,
            delay: (_, _) => Task.CompletedTask,
            networkTimeout: timeout ?? ShortTimeout,
            // Upload requests use their own (longer) budget in production; pin it to the same short
            // value here so the upload stall test does not wait out the 120s default.
            uploadTimeout: timeout ?? ShortTimeout);
}
