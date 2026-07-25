using System.Text;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class SaveSyncServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly SaveUnit FileCard =
        new("pcsx2/Mcd001.ps2", "Memory Card 1", SaveUnitKind.File);

    private readonly InMemoryLocalSaveEndpoint _local = new();
    private readonly InMemoryCloudSyncTransport _remote = new();
    private readonly InMemorySaveSyncManifestStore _manifests = new();

    private SaveSyncService CreateService() => new(_local, _remote, _manifests);

    [Fact]
    public async Task FirstConnect_UploadsLocalSave_AndRecordsBaseline()
    {
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);

        var report = await CreateService().SyncAsync(Provider(FileCard));

        Assert.Equal(1, report.Uploaded);
        Assert.True(_remote.Has(FileCard.UnitId));
        Assert.Equal(Bytes("save-A"), _remote.Content(FileCard.UnitId));
        Assert.NotNull(_manifests.Current.Get(FileCard.UnitId));
    }

    [Fact]
    public async Task SecondMachine_DownloadsSaveItDoesNotYetHave()
    {
        _remote.Seed(FileCard.UnitId, Bytes("save-B"), T0);

        // A fresh machine's provider lists nothing locally yet; the remote-only unit is still
        // reconciled and pulled down.
        var report = await CreateService().SyncAsync(Provider());

        Assert.Equal(1, report.Downloaded);
        Assert.Equal(Bytes("save-B"), _local.Content(FileCard.UnitId));
    }

    [Fact]
    public async Task UnchangedSince_LastSync_DoesNothing()
    {
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);
        var service = CreateService();
        await service.SyncAsync(Provider(FileCard));

        var report = await service.SyncAsync(Provider(FileCard));

        Assert.Equal(1, report.Unchanged);
        Assert.Equal(1, _remote.Uploads);
        Assert.Equal(0, _remote.Downloads);
    }

    [Fact]
    public async Task BothPlayedOffline_KeepsNewerLocal_AndBacksUpTheRemoteLoser()
    {
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);
        var service = CreateService();
        await service.SyncAsync(Provider(FileCard));

        // Both sides edit the same card while disconnected; local ends up newer.
        _local.Seed(FileCard.UnitId, Bytes("local-newer"), T0.AddMinutes(10));
        _remote.Seed(FileCard.UnitId, Bytes("remote-older"), T0.AddMinutes(5));

        var report = await service.SyncAsync(Provider(FileCard));

        Assert.Equal(1, report.Conflicts);
        Assert.Equal(Bytes("local-newer"), _remote.Content(FileCard.UnitId));
        Assert.Equal(Bytes("local-newer"), _local.Content(FileCard.UnitId));

        var backup = Assert.Single(_local.Backups);
        Assert.Equal(Bytes("remote-older"), backup.Content);
        Assert.False(backup.FromLocal);
    }

    [Fact]
    public async Task OfflineRemote_LeavesLocalSaveUntouched()
    {
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);
        _remote.ThrowOnAccess = true;

        await Assert.ThrowsAsync<IOException>(() => CreateService().SyncAsync(Provider(FileCard)));

        Assert.Equal(Bytes("save-A"), _local.Content(FileCard.UnitId));
        Assert.Equal(0, _remote.Uploads);
    }

    [Fact]
    public async Task ForceUpload_OverwritesRemote_AndBacksUpTheRemoteCopy()
    {
        _local.Seed(FileCard.UnitId, Bytes("local"), T0);
        _remote.Seed(FileCard.UnitId, Bytes("remote-different"), T0.AddMinutes(30));

        var report = await CreateService().ForceAsync(Provider(FileCard), SaveSyncDirection.Upload);

        Assert.Equal(1, report.Uploaded);
        Assert.Equal(Bytes("local"), _remote.Content(FileCard.UnitId));

        var backup = Assert.Single(_local.Backups);
        Assert.Equal(Bytes("remote-different"), backup.Content);
        Assert.False(backup.FromLocal);
    }

    [Fact]
    public async Task ForceDownload_OverwritesLocal_AndBacksUpTheLocalCopy()
    {
        _local.Seed(FileCard.UnitId, Bytes("local-different"), T0.AddMinutes(30));
        _remote.Seed(FileCard.UnitId, Bytes("remote"), T0);

        var report = await CreateService().ForceAsync(Provider(FileCard), SaveSyncDirection.Download);

        Assert.Equal(1, report.Downloaded);
        Assert.Equal(Bytes("remote"), _local.Content(FileCard.UnitId));

        var backup = Assert.Single(_local.Backups);
        Assert.Equal(Bytes("local-different"), backup.Content);
        Assert.True(backup.FromLocal);
    }

    [Fact]
    public async Task FolderCard_PerGameUnitsSyncIndependently()
    {
        // The folder-memcard model: each game is its own unit, so a game changed on one machine
        // and a game changed on the other do not collide the way a shared file card would.
        var gow = new SaveUnit("pcsx2/folder/SLUS-20552", "God of War", SaveUnitKind.Folder);
        var sotc = new SaveUnit("pcsx2/folder/SLUS-21274", "Shadow of the Colossus", SaveUnitKind.Folder);
        _local.Seed(gow.UnitId, Bytes("gow-save"), T0);
        _remote.Seed(sotc.UnitId, Bytes("sotc-save"), T0);

        var report = await CreateService().SyncAsync(Provider(gow, sotc));

        Assert.Equal(1, report.Uploaded);
        Assert.Equal(1, report.Downloaded);
        Assert.Equal(Bytes("gow-save"), _remote.Content(gow.UnitId));
        Assert.Equal(Bytes("sotc-save"), _local.Content(sotc.UnitId));
    }

    [Fact]
    public async Task RemoteUnitsOwnedByAnotherProvider_AreIgnored()
    {
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);
        _remote.Seed("dolphin/GM8E01", Bytes("foreign-save"), T0);

        var report = await CreateService().SyncAsync(Provider(FileCard));

        Assert.Equal(1, report.Uploaded);
        Assert.True(_remote.Has("dolphin/GM8E01"));
        Assert.DoesNotContain(report.Results, result => result.UnitId == "dolphin/GM8E01");
    }

    [Fact]
    public async Task Cancellation_StopsBeforeTouchingSaves()
    {
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CreateService().SyncAsync(Provider(FileCard), cancellationToken: cancellation.Token));

        Assert.Equal(0, _remote.Uploads);
    }

    [Fact]
    public async Task SyncAsync_ReportsPerUnitProgress()
    {
        var first = new SaveUnit("pcsx2/Mcd001.ps2", "Memory Card 1", SaveUnitKind.File);
        var second = new SaveUnit("pcsx2/Mcd002.ps2", "Memory Card 2", SaveUnitKind.File);
        _local.Seed(first.UnitId, Bytes("a"), T0);
        _local.Seed(second.UnitId, Bytes("b"), T0);
        var progress = new CapturingProgress();

        await CreateService().SyncAsync(Provider(first, second), progress);

        Assert.Equal(2, progress.Reports.Count);
        Assert.All(progress.Reports, report => Assert.Equal(2, report.Total));
        Assert.Equal([0, 1], progress.Reports.Select(report => report.Completed));
        Assert.All(progress.Reports, report => Assert.Equal(SaveSyncAction.Upload, report.Action));
    }

    private static FakeSaveLocationProvider Provider(params SaveUnit[] units) =>
        new("playstation2", units);

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private sealed class CapturingProgress : IProgress<SaveSyncProgress>
    {
        public List<SaveSyncProgress> Reports { get; } = [];

        public void Report(SaveSyncProgress value) => Reports.Add(value);
    }
}
