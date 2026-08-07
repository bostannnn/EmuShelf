using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Integrations.Emulators.Dolphin;

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
    public async Task IncompatibleRemoteOnlyState_IsReportedWithoutDownloading()
    {
        var state = new SaveUnit("test/states/GAME.state", "GAME.state", SaveUnitKind.File);
        _remote.Seed(state.UnitId, Bytes("old state"), T0, compatibility: "old-build");

        var report = await CreateService().SyncAsync(new CompatibilityProvider(state, includeLocal: false));

        var skipped = Assert.Single(report.Skipped);
        Assert.Contains("different build", skipped.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(_local.Has(state.UnitId));
        Assert.Equal(0, _remote.Downloads);
    }

    [Fact]
    public async Task CompatibleLocalState_ReplacesIncompatibleStableRemoteUnitAfterBackup()
    {
        var state = new SaveUnit("test/states/GAME.state", "GAME.state", SaveUnitKind.File);
        _local.CompatibilityResolver = _ => "current-build";
        _local.Seed(state.UnitId, Bytes("current state"), T0.AddMinutes(1));
        _remote.Seed(state.UnitId, Bytes("old state"), T0, compatibility: "old-build");

        var report = await CreateService().SyncAsync(new CompatibilityProvider(state, includeLocal: true));

        Assert.Equal(1, report.Conflicts);
        Assert.Equal(Bytes("current state"), _remote.Content(state.UnitId));
        Assert.Single(_local.Backups, backup => !backup.FromLocal);
    }

    [Fact]
    public async Task EmulatorUpgrade_DoesNotRelabelUnchangedOldStateAsCurrent()
    {
        var state = new SaveUnit("test/states/GAME.state", "GAME.state", SaveUnitKind.File);
        _local.Seed(state.UnitId, Bytes("old state"), T0);
        _local.CompatibilityResolver = _ => "old-build";
        var service = CreateService();

        await service.SyncAsync(new CompatibilityProvider(state, includeLocal: true, currentBuild: "old-build"));
        Assert.Equal("old-build", _manifests.Current.Get(state.UnitId)?.Compatibility);

        // Merely installing a new emulator changes the provider's current identity, not the state
        // bytes. The old provenance must survive and the old state must not be certified as new.
        _local.CompatibilityResolver = _ => "current-build";
        var report = await service.SyncAsync(new CompatibilityProvider(state, includeLocal: true));

        Assert.Single(report.Skipped);
        Assert.Equal("old-build", _remote.Compatibility(state.UnitId));
        Assert.Equal(1, _remote.Uploads);
        Assert.Equal("old-build", _manifests.Current.Get(state.UnitId)?.Compatibility);
    }

    [Fact]
    public async Task CorruptDownload_DoesNotReplaceLocalOrAdvanceItsBaseline()
    {
        _local.Seed(FileCard.UnitId, Bytes("original"), T0);
        var service = CreateService();
        await service.SyncAsync(Provider(FileCard));
        var baseline = _manifests.Current.Get(FileCard.UnitId);

        _remote.Seed(FileCard.UnitId, Bytes("new remote"), T0.AddMinutes(1));
        _remote.ReplacePayloadWithoutUpdatingIndex(FileCard.UnitId, Bytes("damaged in transit"));

        await Assert.ThrowsAsync<InvalidDataException>(() => service.SyncAsync(Provider(FileCard)));

        Assert.Equal(Bytes("original"), _local.Content(FileCard.UnitId));
        Assert.Equal(baseline, _manifests.Current.Get(FileCard.UnitId));
    }

    [Fact]
    public async Task UnreadableLocalSave_IsSkipped_InsteadOfAbortingTheSync()
    {
        // A reparse point/symlink inside a folder save (InvalidDataException) or a file locked by a
        // running emulator (IOException) makes the local snapshot throw during planning. That unit must
        // sit out and be reported as Skipped — it must not propagate and abort the whole pass.
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);
        _local.SnapshotHook = _ => throw new IOException("the save file is in use by the emulator");

        var report = await CreateService().SyncAsync(Provider(FileCard));

        var skipped = Assert.Single(report.Skipped);
        Assert.Contains("could not read the local save", skipped.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _remote.Uploads);
    }

    [Fact]
    public async Task LocalSaveLockedDuringApply_SkipsOnlyThatUnit_AndStillFlushesAndSyncsTheRest()
    {
        // The emulator relaunches and locks one card's file after planning but before the transfer.
        // That IOException in the apply loop must cost only its own unit, not fault the whole pass
        // (which would drop the manifest flush and every other platform's sync too).
        var locked = new SaveUnit("pcsx2/Mcd001.ps2", "locked card", SaveUnitKind.File);
        var healthy = new SaveUnit("pcsx2/Mcd002.ps2", "healthy card", SaveUnitKind.File);
        _local.Seed(locked.UnitId, Bytes("locked"), T0);
        _local.Seed(healthy.UnitId, Bytes("healthy"), T0);
        _local.ReadHook = unitId =>
        {
            if (unitId == locked.UnitId)
                throw new IOException("the save file is in use by the emulator");
        };

        var report = await CreateService().SyncAsync(Provider(locked, healthy));

        Assert.Contains(
            report.Results,
            result => result.UnitId == locked.UnitId && result.Action == SaveSyncAction.Skipped);
        Assert.Equal(1, report.Uploaded);
        Assert.True(_remote.Has(healthy.UnitId));
        Assert.Equal(1, _remote.FlushCalls);
        Assert.NotNull(_manifests.Current.Get(healthy.UnitId));
        Assert.Null(_manifests.Current.Get(locked.UnitId));
    }

    [Fact]
    public async Task ProviderWhoseConfigurationCannotBeRead_SkipsOnlyItsPlatform_NotTheWholePass()
    {
        // One platform's unreadable emulator config must not fault every other platform's sync in the
        // same multi-provider pass. The broken platform sits out and is reported under its own id.
        var ppsspp = new SaveUnit("ppsspp/ULUS10041DATA00", "PSP save", SaveUnitKind.Folder);
        var pspLocal = new InMemoryLocalSaveEndpoint();
        pspLocal.Seed(ppsspp.UnitId, Bytes("psp-save"), T0);

        var report = await CreateService().SyncAllAsync(
            [
                new SaveSyncTarget(new UnreadableConfigProvider("gamecube", "dolphin/gc/"), _local),
                new SaveSyncTarget(new FakeSaveLocationProvider("psp", ppsspp), pspLocal),
            ]);

        Assert.Contains(
            report.Skipped,
            result => result.UnitId.StartsWith("dolphin/gc/", StringComparison.Ordinal));
        Assert.Equal(1, report.Uploaded);
        Assert.True(_remote.Has(ppsspp.UnitId));
    }

    [Fact]
    public async Task RemoteUnitResolvingOutsideItsRoot_IsSkipped_NotFatal()
    {
        // The endpoint rejects an out-of-root resolution (a corrupt/crafted cloud id) with an
        // ArgumentException. That must skip the one unit, not propagate out of the pass.
        _remote.Seed(FileCard.UnitId, Bytes("save"), T0);
        _local.SnapshotHook = _ =>
            throw new ArgumentException("The provider resolved the save unit outside its approved root.");

        var report = await CreateService().SyncAsync(Provider(FileCard));

        var skipped = Assert.Single(report.Skipped);
        Assert.Equal(FileCard.UnitId, skipped.UnitId);
        Assert.Contains("safely resolved", skipped.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(_local.Has(FileCard.UnitId));
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
    public async Task MatchingCopies_RepairAStaleBaselineLeftByAnInterruptedCommit()
    {
        _local.Seed(FileCard.UnitId, Bytes("initial"), T0);
        var service = CreateService();
        await service.SyncAsync(Provider(FileCard));

        // Reproduce a cloud commit that succeeded before the local manifest could be saved: both
        // copies contain the committed content, while the baseline still describes the old copy.
        var committedUtc = T0.AddMinutes(1);
        _local.Seed(FileCard.UnitId, Bytes("committed"), committedUtc);
        _remote.Seed(FileCard.UnitId, Bytes("committed"), committedUtc);

        var healingPass = await service.SyncAsync(Provider(FileCard));

        Assert.Equal(1, healingPass.Unchanged);
        Assert.Equal(
            InMemoryCloudSyncTransport.Hash(Bytes("committed")),
            _manifests.Current.Get(FileCard.UnitId)?.ContentHash);

        // With the repaired baseline, a later remote-only edit is a download, not a false
        // two-sided conflict decided by machine clocks.
        _remote.Seed(FileCard.UnitId, Bytes("remote edit"), committedUtc.AddMinutes(1));
        var nextPass = await service.SyncAsync(Provider(FileCard));

        Assert.Equal(1, nextPass.Downloaded);
        Assert.Equal(0, nextPass.Conflicts);
        Assert.Equal(Bytes("remote edit"), _local.Content(FileCard.UnitId));
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
    public async Task EveryUnitNeedingTheCloudPayloadIsAnnouncedBeforeTheFirstTransfer()
    {
        // The rclone transport opens one download session for the whole pass; it can only scope that
        // session to the payloads this pass needs if the service says so before transferring.
        var download = new SaveUnit("pcsx2/Mcd002.ps2", "second card", SaveUnitKind.File);
        var conflict = new SaveUnit("pcsx2/Mcd003.ps2", "third card", SaveUnitKind.File);
        _local.Seed(FileCard.UnitId, Bytes("upload-only"), T0);
        _remote.Seed(download.UnitId, Bytes("remote-only"), T0);
        _local.Seed(conflict.UnitId, Bytes("local-edit"), T0.AddMinutes(2));
        _remote.Seed(conflict.UnitId, Bytes("remote-edit"), T0.AddMinutes(1));

        await CreateService().SyncAsync(Provider(FileCard, download, conflict));

        Assert.Equal(
            [download.UnitId, conflict.UnitId],
            _remote.AnnouncedDownloads.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(FileCard.UnitId, _remote.AnnouncedDownloads);
    }

    [Fact]
    public async Task AUnitTheIndexPromisesButCannotDeliver_DoesNotCostTheOtherUnitsTheirSync()
    {
        // Real failure: the cloud index listed three PSP saves whose payloads were never uploaded,
        // and the first of them aborted every pass on the other machine.
        var missing = new SaveUnit("pcsx2/Mcd002.ps2", "second card", SaveUnitKind.File);
        var healthy = new SaveUnit("pcsx2/Mcd003.ps2", "third card", SaveUnitKind.File);
        _remote.Seed(missing.UnitId, Bytes("promised"), T0);
        _remote.Seed(healthy.UnitId, Bytes("deliverable"), T0);
        _remote.MissingPayloads.Add(missing.UnitId);

        var report = await CreateService().SyncAsync(Provider(missing, healthy));

        Assert.Equal(1, report.Downloaded);
        Assert.Equal(Bytes("deliverable"), _local.Content(healthy.UnitId));
        Assert.False(_local.Has(missing.UnitId));
        Assert.Contains(
            report.Results,
            result => result.UnitId == missing.UnitId && result.Reason.Contains("missing"));
    }

    [Fact]
    public async Task TheTransferIsReportedAsItsOwnPhase_NotAsAFinishedUnitCounter()
    {
        // Units are staged locally, so the counter reaches its total before a byte moves. Without a
        // phase of its own, a large upload looks like a finished sync that has hung.
        _local.Seed(FileCard.UnitId, Bytes("save-A"), T0);
        var reports = new List<SaveSyncProgress>();

        await CreateService().SyncAsync(
            Provider(FileCard),
            new Progress<SaveSyncProgress>(reports.Add));

        // Progress is marshalled, so allow the posted callbacks to run before asserting.
        for (var attempt = 0; attempt < 50 && !reports.Any(r => r.Phase == SaveSyncPhase.Transferring); attempt++)
            await Task.Delay(10);

        var transfer = Assert.Single(reports, report => report.Phase == SaveSyncPhase.Transferring);
        Assert.Null(transfer.TransferPercent);
        Assert.Contains(reports, report => report.Phase == SaveSyncPhase.Reconciling);
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
    public async Task SyncAll_ReconcilesSeveralProvidersWithOneRemotePass()
    {
        var ppsspp = new SaveUnit("ppsspp/ULUS10041DATA00", "PSP save", SaveUnitKind.Folder);
        var pspLocal = new InMemoryLocalSaveEndpoint();
        _local.Seed(FileCard.UnitId, Bytes("ps2-save"), T0);
        pspLocal.Seed(ppsspp.UnitId, Bytes("psp-save"), T0);

        var report = await CreateService().SyncAllAsync(
            [
                new SaveSyncTarget(Provider(FileCard), _local),
                new SaveSyncTarget(new FakeSaveLocationProvider("psp", ppsspp), pspLocal),
            ]);

        Assert.Equal(2, report.Uploaded);
        Assert.Equal(1, _remote.ListCalls);
        Assert.Equal(1, _remote.FlushCalls);
        Assert.Equal(Bytes("ps2-save"), _remote.Content(FileCard.UnitId));
        Assert.Equal(Bytes("psp-save"), _remote.Content(ppsspp.UnitId));
    }

    [Fact]
    public async Task SyncAll_ConflictOnASecondProvider_BacksTheLoserUpThroughThatProvidersEndpoint()
    {
        // Regression: the losing remote copy used to be handed to the first target's endpoint
        // regardless of which provider owned the unit. A real PCSX2 endpoint refuses to resolve a
        // `ppsspp/...` id, so a PSP conflict aborted the whole multi-provider run.
        var ppsspp = new SaveUnit("ppsspp/ULUS10041DATA00", "PSP save", SaveUnitKind.Folder);
        var pspLocal = new InMemoryLocalSaveEndpoint();
        _local.Seed(FileCard.UnitId, Bytes("ps2-save"), T0);
        // Both sides changed with no baseline; the local copy is newer, so local wins.
        pspLocal.Seed(ppsspp.UnitId, Bytes("psp-local"), T0.AddMinutes(10));
        _remote.Seed(ppsspp.UnitId, Bytes("psp-remote"), T0);

        var report = await CreateService().SyncAllAsync(
            [
                new SaveSyncTarget(Provider(FileCard), _local),
                new SaveSyncTarget(new FakeSaveLocationProvider("psp", ppsspp), pspLocal),
            ]);

        Assert.Equal(1, report.Conflicts);
        var backup = Assert.Single(pspLocal.Backups);
        Assert.Equal(ppsspp.UnitId, backup.UnitId);
        Assert.Equal(Bytes("psp-remote"), backup.Content);
        Assert.False(backup.FromLocal);
        // The PS2 endpoint must not have been asked to back up a unit it does not own.
        Assert.Empty(_local.Backups);
        // The winning local copy still reached the cloud.
        Assert.Equal(Bytes("psp-local"), _remote.Content(ppsspp.UnitId));
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

        // Per unit while reconciling, plus one report for the transfer that follows them.
        var perUnit = progress.Reports.Where(report => report.Phase == SaveSyncPhase.Reconciling).ToList();
        Assert.Equal(2, perUnit.Count);
        Assert.All(perUnit, report => Assert.Equal(2, report.Total));
        Assert.Equal([0, 1], perUnit.Select(report => report.Completed));
        Assert.All(perUnit, report => Assert.Equal(SaveSyncAction.Upload, report.Action));
        Assert.Single(progress.Reports, report => report.Phase == SaveSyncPhase.Transferring);
    }

    private static FakeSaveLocationProvider Provider(params SaveUnit[] units) =>
        new("playstation2", units);

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

    private sealed class CapturingProgress : IProgress<SaveSyncProgress>
    {
        public List<SaveSyncProgress> Reports { get; } = [];

        public void Report(SaveSyncProgress value) => Reports.Add(value);
    }

    // A provider whose emulator configuration cannot be read: enumeration fails closed with a
    // SaveProviderConfigurationException, the exact shape the service must isolate per platform.
    private sealed class UnreadableConfigProvider(string systemId, string unitIdPrefix) : ISaveLocationProvider
    {
        public string SystemId => systemId;
        public string UnitIdPrefix => unitIdPrefix;
        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            throw new DolphinConfigurationFormatException("Dolphin.ini could not be read.");
        public SaveUnitLocation? ResolveUnit(string unitId) => null;
    }

    private sealed class CompatibilityProvider(
        SaveUnit state,
        bool includeLocal,
        string currentBuild = "current-build") : ISaveLocationProvider
    {
        public string SystemId => "test";
        public string UnitIdPrefix => "test/";
        public bool OwnsUnit(string unitId) => unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal);
        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SaveUnit>>(includeLocal ? [state] : []);
        public SaveUnitLocation? ResolveUnit(string unitId) => null;
        public string? GetCompatibility(string unitId) => currentBuild;
        public string? GetRemoteIncompatibilityReason(SaveUnitSnapshot remoteSnapshot) =>
            remoteSnapshot.Compatibility == currentBuild ? null : "This state was written by a different build.";
    }
}
