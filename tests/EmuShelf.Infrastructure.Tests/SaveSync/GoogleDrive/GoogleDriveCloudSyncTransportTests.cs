using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

public sealed class GoogleDriveCloudSyncTransportTests : TempAppDirectoryTestBase
{
    private const string CloudFolder = "EmuShelf/Saves";

    private static CancellationToken Cancellation => CancellationToken.None;

    private static readonly DateTimeOffset Modified = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_OnAnEmptyAccountReportsNoUnits()
    {
        var drive = new FakeDriveServer();

        Assert.Empty(await Transport(drive).ListAsync(Cancellation));
    }

    [Fact]
    public async Task List_ReadsTheIndexTheOtherTransportWouldHaveWritten()
    {
        var drive = new FakeDriveServer();
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([new SaveUnitSnapshot("pcsx2/shared/Mcd001.ps2", "hash-a", Modified, "file-card")]));

        var units = await Transport(drive).ListAsync(Cancellation);

        var unit = Assert.Single(units);
        Assert.Equal("pcsx2/shared/Mcd001.ps2", unit.UnitId);
        Assert.Equal("hash-a", unit.ContentHash);
        Assert.Equal("file-card", unit.Compatibility);
    }

    [Fact]
    public async Task Upload_ProducesTheNestedLayoutTheRcloneTransportProduces()
    {
        // The layout is the contract between transports. If this drifts, a user who switches
        // transports sees an empty remote and re-uploads everything.
        var drive = new FakeDriveServer();
        var transport = Transport(drive);

        await transport.ListAsync(Cancellation);
        await transport.UploadAsync(
            "pcsx2/shared/Mcd001.ps2", Bytes("save"), "hash-a", Modified, Cancellation);
        await transport.FlushAsync(cancellationToken: Cancellation);

        Assert.Contains($"{CloudFolder}/pcsx2/shared", drive.Folders);
        Assert.Contains($"{CloudFolder}/pcsx2/shared/Mcd001.ps2.payload", drive.Files.Keys);
        Assert.Contains($"{CloudFolder}/{CloudSaveIndex.FileName}", drive.Files.Keys);
        Assert.Equal("save", Text(drive.Files[$"{CloudFolder}/pcsx2/shared/Mcd001.ps2.payload"]));
    }

    [Fact]
    public async Task Upload_WritesAnIndexThatParsesBackToTheSameUnits()
    {
        var drive = new FakeDriveServer();
        var transport = Transport(drive);

        await transport.ListAsync(Cancellation);
        await transport.UploadAsync("pcsx2/a", Bytes("one"), "hash-a", Modified, Cancellation, "file-card");
        await transport.UploadAsync("duckstation/b", Bytes("two"), "hash-b", Modified.AddHours(1), Cancellation);
        await transport.FlushAsync(cancellationToken: Cancellation);

        var index = CloudSaveIndex.Parse(drive.Files[$"{CloudFolder}/{CloudSaveIndex.FileName}"]);
        Assert.Equal(2, index.Count);
        Assert.Equal("hash-a", index["pcsx2/a"].ContentHash);
        Assert.Equal("file-card", index["pcsx2/a"].Compatibility);
        Assert.Equal(Modified.AddHours(1), index["duckstation/b"].ModifiedUtc);
    }

    [Fact]
    public async Task Upload_ThenDownload_RoundTripsTheExactBytes()
    {
        var drive = new FakeDriveServer();
        var writer = Transport(drive);
        await writer.ListAsync(Cancellation);
        await writer.UploadAsync("pcsx2/shared/Mcd001.ps2", Bytes("payload-bytes"), "hash", Modified, Cancellation);
        await writer.FlushAsync(cancellationToken: Cancellation);

        var reader = Transport(drive);
        await reader.ListAsync(Cancellation);
        await using var content = await reader.DownloadAsync("pcsx2/shared/Mcd001.ps2", Cancellation);

        using var streamReader = new StreamReader(content);
        Assert.Equal("payload-bytes", await streamReader.ReadToEndAsync(Cancellation));
    }

    [Fact]
    public async Task Upload_ReplacesAnExistingPayloadInPlaceRatherThanDuplicatingIt()
    {
        // Drive allows two files with the same name in one folder. Creating instead of replacing
        // would leave the old blob behind and make which one wins depend on listing order.
        var drive = new FakeDriveServer();
        var first = Transport(drive);
        await first.ListAsync(Cancellation);
        await first.UploadAsync("pcsx2/a", Bytes("v1"), "hash-1", Modified, Cancellation);
        await first.FlushAsync(cancellationToken: Cancellation);

        var second = Transport(drive);
        await second.ListAsync(Cancellation);
        await second.DownloadAsync("pcsx2/a", Cancellation);
        await second.UploadAsync("pcsx2/a", Bytes("v2"), "hash-2", Modified.AddHours(1), Cancellation);
        await second.FlushAsync(cancellationToken: Cancellation);

        Assert.Single(drive.Files.Keys, path => path.EndsWith("pcsx2/a.payload", StringComparison.Ordinal));
        Assert.Equal("v2", Text(drive.Files[$"{CloudFolder}/pcsx2/a.payload"]));
    }

    [Fact]
    public async Task Upload_WithoutDownloadingFirst_StillReplacesRatherThanDuplicating()
    {
        // The common repeat sync never downloads: the local save is newer, so the pass only uploads.
        // If that path cannot see the existing blob it creates a second file with the same name, and
        // which one a later sync reads becomes a matter of listing order.
        var drive = new FakeDriveServer();
        var first = Transport(drive);
        await first.ListAsync(Cancellation);
        await first.UploadAsync("pcsx2/a", Bytes("v1"), "hash-1", Modified, Cancellation);
        await first.FlushAsync(cancellationToken: Cancellation);

        var second = Transport(drive);
        await second.ListAsync(Cancellation);
        await second.UploadAsync("pcsx2/a", Bytes("v2"), "hash-2", Modified.AddHours(1), Cancellation);
        await second.FlushAsync(cancellationToken: Cancellation);

        Assert.Single(drive.Files.Keys, path => path.EndsWith("pcsx2/a.payload", StringComparison.Ordinal));
        Assert.Equal("v2", Text(drive.Files[$"{CloudFolder}/pcsx2/a.payload"]));
    }

    [Fact]
    public async Task Flush_WritesOneIndexNotASecondCopy()
    {
        var drive = new FakeDriveServer();
        var first = Transport(drive);
        await first.ListAsync(Cancellation);
        await first.UploadAsync("pcsx2/a", Bytes("v1"), "hash-1", Modified, Cancellation);
        await first.FlushAsync(cancellationToken: Cancellation);

        var second = Transport(drive);
        await second.ListAsync(Cancellation);
        await second.UploadAsync("pcsx2/b", Bytes("v2"), "hash-2", Modified, Cancellation);
        await second.FlushAsync(cancellationToken: Cancellation);

        Assert.Single(drive.Files.Keys, path => path.EndsWith(CloudSaveIndex.FileName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_RecoversFromACachedFolderIdThatNoLongerPointsAtTheSavesFolder()
    {
        // Drive answers a listing for a folder that is not there with an empty list, not an error.
        // Believing a stale id would report an empty cloud, re-upload everything, and never reconcile
        // with the machine whose saves are sitting in the real folder — all without looking wrong.
        var drive = new FakeDriveServer();
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([new SaveUnitSnapshot("pcsx2/a", "hash-a", Modified, null)]));

        var transport = Transport(drive, cloudFolderId: "id-that-no-longer-exists");
        var units = await transport.ListAsync(Cancellation);

        Assert.Equal(["pcsx2/a"], units.Select(unit => unit.UnitId));
        Assert.NotEqual("id-that-no-longer-exists", transport.CloudFolderId);
    }

    [Fact]
    public async Task List_AfterRecoveringTheFolderIdStillServesDownloads()
    {
        // Anything cached against the wrong folder has to be discarded with it, or a download would
        // look up a payload id that belongs to a folder this transport is no longer using.
        var drive = new FakeDriveServer();
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([new SaveUnitSnapshot("pcsx2/a", "hash-a", Modified, null)]));
        drive.AddFile($"{CloudFolder}/pcsx2/a.payload", "recovered-bytes");

        var transport = Transport(drive, cloudFolderId: "id-that-no-longer-exists");
        await transport.ListAsync(Cancellation);
        await using var content = await transport.DownloadAsync("pcsx2/a", Cancellation);

        using var reader = new StreamReader(content);
        Assert.Equal("recovered-bytes", await reader.ReadToEndAsync(Cancellation));
    }

    [Fact]
    public async Task List_WhenTheFolderGenuinelyDoesNotExistStillReportsAnEmptyRemote()
    {
        // The recovery must not invent a folder. A first sync before anything was ever uploaded is a
        // legitimately empty remote, and reporting anything else would block the upload that follows.
        var drive = new FakeDriveServer();

        var transport = Transport(drive, cloudFolderId: "id-that-no-longer-exists");

        Assert.Empty(await transport.ListAsync(Cancellation));
    }

    [Fact]
    public async Task List_WithAValidCachedFolderIdDoesNotPayForTheRecoveryProbe()
    {
        // The correction is only worth having if the healthy path does not pay for it.
        var drive = new FakeDriveServer();
        var folderId = drive.AddFolder(CloudFolder);
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([new SaveUnitSnapshot("pcsx2/a", "hash-a", Modified, null)]));

        var transport = Transport(drive, cloudFolderId: folderId);
        await transport.ListAsync(Cancellation);

        // One listing of the saves folder to find the index, and nothing spent walking to it.
        Assert.Equal(1, drive.ListCalls);
        Assert.Equal(folderId, transport.CloudFolderId);
    }

    [Fact]
    public async Task Download_MissingPayloadRaisesTheRecoverableCondition()
    {
        var drive = new FakeDriveServer();
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([new SaveUnitSnapshot("pcsx2/a", "hash", Modified, null)]));
        var transport = Transport(drive);
        await transport.ListAsync(Cancellation);

        await Assert.ThrowsAsync<CloudPayloadMissingException>(
            () => transport.DownloadAsync("pcsx2/a", Cancellation));
    }

    [Fact]
    public async Task Flush_PrunesIndexEntriesWhosePayloadIsGone()
    {
        // Otherwise the machine that still holds the save keeps seeing "already on the remote" and
        // never re-uploads it, while every other machine fails trying to download it.
        var drive = new FakeDriveServer();
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([
                new SaveUnitSnapshot("pcsx2/a", "hash-a", Modified, null),
                new SaveUnitSnapshot("pcsx2/b", "hash-b", Modified, null),
            ]));
        drive.AddFile($"{CloudFolder}/pcsx2/b.payload", "kept");

        var transport = Transport(drive);
        await transport.ListAsync(Cancellation);
        await Assert.ThrowsAsync<CloudPayloadMissingException>(() => transport.DownloadAsync("pcsx2/a", Cancellation));
        await transport.FlushAsync(cancellationToken: Cancellation);

        var index = CloudSaveIndex.Parse(drive.Files[$"{CloudFolder}/{CloudSaveIndex.FileName}"]);
        Assert.False(index.ContainsKey("pcsx2/a"));
        Assert.True(index.ContainsKey("pcsx2/b"));
    }

    [Fact]
    public async Task FindMissingPayloads_ReportsIndexedUnitsWithNoBlob()
    {
        var drive = new FakeDriveServer();
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([
                new SaveUnitSnapshot("pcsx2/a", "hash-a", Modified, null),
                new SaveUnitSnapshot("pcsx2/b", "hash-b", Modified, null),
            ]));
        drive.AddFile($"{CloudFolder}/pcsx2/b.payload", "kept");

        var transport = Transport(drive);
        await transport.ListAsync(Cancellation);

        Assert.Equal(["pcsx2/a"], await transport.FindMissingPayloadsAsync(Cancellation));
    }

    [Fact]
    public async Task Flush_UploadsPayloadsBeforeTheIndexThatDescribesThem()
    {
        // The index is the commit. An index that lands first would advertise a payload that is not
        // there yet, and any machine syncing in that window fails to download it.
        var drive = new OrderRecordingDriveServer();
        var transport = Transport(drive);

        await transport.ListAsync(Cancellation);
        await transport.UploadAsync("pcsx2/a", Bytes("one"), "hash-a", Modified, Cancellation);
        await transport.FlushAsync(cancellationToken: Cancellation);

        var payloadWrite = drive.WrittenNames.IndexOf("a.payload");
        var indexWrite = drive.WrittenNames.IndexOf(CloudSaveIndex.FileName);
        Assert.True(payloadWrite >= 0 && indexWrite > payloadWrite);
    }

    [Fact]
    public async Task Flush_ReportsProgressThatEndsAtEveryUnit()
    {
        var drive = new FakeDriveServer();
        var transport = Transport(drive);
        await transport.ListAsync(Cancellation);
        for (var i = 0; i < 3; i++)
            await transport.UploadAsync($"pcsx2/{i}", Bytes("x"), $"hash-{i}", Modified, Cancellation);

        var reports = new SynchronousProgress<SaveTransferProgress>();
        await transport.FlushAsync(reports, Cancellation);

        Assert.NotEmpty(reports.Reports);
        Assert.Equal(3, reports.Reports[^1].CompletedUnits);
        Assert.Equal(3, reports.Reports[^1].TotalUnits);
        Assert.Equal(100, reports.Reports[^1].Percent);
        // Never overshoots: a count above the total would render as "4 of 3 saves".
        Assert.All(reports.Reports, report => Assert.InRange(report.CompletedUnits, 0, 3));
    }

    [Fact]
    public async Task Flush_WithNothingStagedMakesNoCalls()
    {
        var drive = new FakeDriveServer();
        var transport = Transport(drive);

        await transport.FlushAsync(cancellationToken: Cancellation);

        Assert.Equal(0, drive.UploadCalls);
    }

    [Fact]
    public async Task Flush_LeavesNoStagingDirectoryBehind()
    {
        var drive = new FakeDriveServer();
        var transport = Transport(drive);
        await transport.ListAsync(Cancellation);
        await transport.UploadAsync("pcsx2/a", Bytes("one"), "hash-a", Modified, Cancellation);

        await transport.FlushAsync(cancellationToken: Cancellation);

        var transfers = Path.Combine(AppPaths.SavesDirectory, "transfers");
        Assert.True(!Directory.Exists(transfers) || Directory.GetDirectories(transfers).Length == 0);
    }

    [Fact]
    public async Task Download_WalksTheFolderTreeOnlyOncePerSession()
    {
        // Drive resolves no paths of its own, so a per-unit walk is what makes a small sync slow.
        var drive = new FakeDriveServer();
        drive.AddFile(
            $"{CloudFolder}/{CloudSaveIndex.FileName}",
            CloudSaveIndex.Serialize([
                new SaveUnitSnapshot("pcsx2/a", "h", Modified, null),
                new SaveUnitSnapshot("pcsx2/b", "h", Modified, null),
                new SaveUnitSnapshot("duckstation/c", "h", Modified, null),
            ]));
        drive.AddFile($"{CloudFolder}/pcsx2/a.payload", "a");
        drive.AddFile($"{CloudFolder}/pcsx2/b.payload", "b");
        drive.AddFile($"{CloudFolder}/duckstation/c.payload", "c");

        var transport = Transport(drive);
        await transport.ListAsync(Cancellation);
        var afterList = drive.ListCalls;

        foreach (var unitId in new[] { "pcsx2/a", "pcsx2/b", "duckstation/c" })
            (await transport.DownloadAsync(unitId, Cancellation)).Dispose();

        // One walk of root + the two emulator folders, and nothing per additional unit.
        Assert.Equal(3, drive.ListCalls - afterList);
    }

    [Fact]
    public void Constructor_RejectsATraversalCloudFolder() =>
        Assert.Throws<ArgumentException>(() => Transport(new FakeDriveServer(), "../escape"));

    [Fact]
    public async Task Upload_RejectsAnUnsafeUnitId()
    {
        var transport = Transport(new FakeDriveServer());

        await Assert.ThrowsAsync<ArgumentException>(
            () => transport.UploadAsync("pcsx2/../escape", Bytes("x"), "hash", Modified, Cancellation));
    }

    [Theory]
    [InlineData("pcsx2/shared/Mcd001.ps2.payload", "pcsx2/shared", "Mcd001.ps2.payload")]
    [InlineData("index.json", "", "index.json")]
    public void PathHelpers_SplitAtTheLastSeparator(string path, string parent, string leaf)
    {
        Assert.Equal(parent, GoogleDriveCloudSyncTransport.ParentPath(path));
        Assert.Equal(leaf, GoogleDriveCloudSyncTransport.LeafName(path));
    }

    private GoogleDriveCloudSyncTransport Transport(
        HttpMessageHandler drive,
        string cloudFolder = CloudFolder,
        string? cloudFolderId = null) =>
        new(
            new GoogleDriveApiClient(new HttpClient(drive), new StubTokenSource()),
            AppPaths,
            cloudFolder,
            logger: null,
            cloudFolderId);

    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

    /// <summary>
    /// Records on the calling thread. <see cref="Progress{T}"/> dispatches through the synchronization
    /// context, so under a test runner its callbacks can arrive after the assertion.
    /// </summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = [];

        public void Report(T value) => Reports.Add(value);
    }

    private static string Text(byte[] content) => Encoding.UTF8.GetString(content);

    /// <summary>Records the order in which file contents were written, to pin the commit ordering.</summary>
    private sealed class OrderRecordingDriveServer : FakeDriveServer
    {
        public List<string> WrittenNames { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var isUpload = request.RequestUri!.AbsolutePath.Contains("/upload/", StringComparison.Ordinal);
            var body = isUpload && request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : null;

            var response = await base.SendAsync(request, cancellationToken);

            if (body is not null)
            {
                foreach (var name in new[] { "a.payload", CloudSaveIndex.FileName })
                {
                    if (body.Contains($"\"name\":\"{name}\"", StringComparison.Ordinal))
                        WrittenNames.Add(name);
                }
            }

            return response;
        }
    }
}
