namespace EmuShelf.Core.SaveSync;

/// <summary>
/// Reconciles a system's save units between the local machine and a cloud remote. The planner
/// decides direction per unit; this service performs the copies non-destructively — it backs up
/// before every overwrite, preserves the losing side of a conflict, and never deletes a save —
/// then advances the manifest baseline only for the units it successfully moved.
/// </summary>
public sealed class SaveSyncService
{
    private readonly ILocalSaveEndpoint _local;
    private readonly ICloudSyncTransport _remote;
    private readonly ISaveSyncManifestStore _manifests;

    public SaveSyncService(
        ILocalSaveEndpoint local,
        ICloudSyncTransport remote,
        ISaveSyncManifestStore manifests)
    {
        _local = local;
        _remote = remote;
        _manifests = manifests;
    }

    /// <summary>
    /// Automatically reconciles every unit that exists locally or remotely for the provider's
    /// system, using the last-synced baseline to choose a safe direction for each.
    /// </summary>
    public Task<SaveSyncReport> SyncAsync(
        ISaveLocationProvider provider,
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        SyncAllAsync([new SaveSyncTarget(provider, _local)], progress, cancellationToken);

    /// <summary>
    /// Reconciles several provider/endpoint pairs through one manifest read, remote index read,
    /// staged transport flush, and manifest write. Unit-id namespaces must not overlap.
    /// </summary>
    public async Task<SaveSyncReport> SyncAllAsync(
        IReadOnlyList<SaveSyncTarget> targets,
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(targets);

        var manifest = await _manifests.LoadAsync(cancellationToken);
        var allRemoteSnapshots = (await _remote.ListAsync(cancellationToken))
            .ToDictionary(snapshot => snapshot.UnitId, StringComparer.Ordinal);
        var work = new List<SyncWorkItem>();
        var claimedUnitIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var provider = target.Provider;
            var localUnits = await provider.GetSaveUnitsAsync(cancellationToken);
            var displayNames = localUnits.ToDictionary(unit => unit.UnitId, unit => unit.DisplayName, StringComparer.Ordinal);
            var unitIds = new SortedSet<string>(
                localUnits.Select(unit => unit.UnitId),
                StringComparer.Ordinal);
            foreach (var remote in provider.SelectRemoteUnits(allRemoteSnapshots.Values.ToArray()))
            {
                unitIds.Add(remote.UnitId);
            }

            foreach (var unitId in unitIds)
            {
                if (!claimedUnitIds.Add(unitId))
                    throw new InvalidOperationException($"More than one save provider claimed unit '{unitId}'.");
                work.Add(new SyncWorkItem(
                    unitId,
                    displayNames.GetValueOrDefault(unitId, unitId),
                    target.LocalEndpoint,
                    provider));
            }
        }

        // Two phases on purpose. Deciding everything before the first transfer lets the transport be
        // told which payloads this pass needs, so a cloud session can fetch those instead of the
        // whole remote. Decisions depend only on each unit's own local/remote/baseline state, so
        // taking them all up front is equivalent to interleaving them.
        var planned = new List<PlannedUnit>(work.Count);
        var results = new List<SaveUnitSyncResult>();
        foreach (var item in work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveUnitSnapshot? localSnapshot;
            try
            {
                localSnapshot = await item.LocalEndpoint.SnapshotAsync(item.UnitId, cancellationToken);
            }
            catch (SaveUnitNotResolvableException ex)
            {
                // The cloud holds a save this machine's configuration has no place for — a card
                // scheme the local emulator does not use, say. Two machines may legitimately differ,
                // so this unit sits out and the rest of the pass proceeds.
                results.Add(new SaveUnitSyncResult(
                    item.UnitId,
                    SaveSyncAction.Skipped,
                    DescribeUnresolvable(ex)));
                continue;
            }

            allRemoteSnapshots.TryGetValue(item.UnitId, out var remoteSnapshot);
            var baseline = manifest.Get(item.UnitId);
            if (localSnapshot is not null)
                localSnapshot = PreserveCompatibilityProvenance(localSnapshot, baseline, remoteSnapshot);
            var remoteIncompatibility = remoteSnapshot is null
                ? null
                : item.Provider.GetRemoteIncompatibilityReason(remoteSnapshot);
            var localIncompatibility = localSnapshot is null
                ? null
                : item.Provider.GetRemoteIncompatibilityReason(localSnapshot);
            if (localIncompatibility is not null)
            {
                if (remoteSnapshot is not null && remoteIncompatibility is null)
                {
                    planned.Add(new PlannedUnit(
                        item,
                        new SaveSyncDecision(
                            SaveSyncAction.ConflictRemoteWins,
                            localIncompatibility + " The compatible cloud state replaces it after the local copy is backed up."),
                        localSnapshot,
                        remoteSnapshot,
                        baseline));
                }
                else
                {
                    results.Add(new SaveUnitSyncResult(
                        item.UnitId,
                        SaveSyncAction.Skipped,
                        localIncompatibility + " The local state was not uploaded."));
                }
                continue;
            }
            if (remoteIncompatibility is not null && localSnapshot is null)
            {
                results.Add(new SaveUnitSyncResult(item.UnitId, SaveSyncAction.Skipped, remoteIncompatibility));
                continue;
            }
            planned.Add(new PlannedUnit(
                item,
                remoteIncompatibility is null
                    ? SaveSyncPlanner.Decide(localSnapshot, remoteSnapshot, baseline)
                    : new SaveSyncDecision(
                        SaveSyncAction.ConflictLocalWins,
                        remoteIncompatibility + " The compatible local state replaces it after the cloud copy is backed up."),
                localSnapshot,
                remoteSnapshot,
                baseline));
        }

        _remote.ExpectDownloads(planned
            .Where(unit => NeedsRemotePayload(unit.Decision.Action))
            .Select(unit => unit.Item.UnitId));

        var completed = 0;
        foreach (var unit in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new SaveSyncProgress(
                completed, planned.Count, unit.Item.DisplayName, unit.Decision.Action));
            try
            {
                manifest = await ApplyAsync(
                    unit.Item.LocalEndpoint,
                    unit.Item.UnitId,
                    unit.Decision.Action,
                    unit.LocalSnapshot,
                    unit.RemoteSnapshot,
                    unit.Baseline,
                    manifest,
                    cancellationToken);

                results.Add(new SaveUnitSyncResult(unit.Item.UnitId, unit.Decision.Action, unit.Decision.Reason));
            }
            catch (CloudPayloadMissingException)
            {
                // One unit the remote index promised but cannot deliver must not cost every other
                // unit its sync. The baseline is deliberately not advanced, and the transport drops
                // the stale index entry, so the machine that still holds the save re-uploads it.
                results.Add(new SaveUnitSyncResult(
                    unit.Item.UnitId,
                    SaveSyncAction.Skipped,
                    "The cloud copy is missing; the stale entry was removed and the save will be " +
                    "re-uploaded by the machine that still has it."));
            }
            catch (SaveUnitNotResolvableException ex)
            {
                results.Add(new SaveUnitSyncResult(
                    unit.Item.UnitId,
                    SaveSyncAction.Skipped,
                    DescribeUnresolvable(ex)));
            }

            completed++;
        }

        await FlushAsync(progress, planned.Count, cancellationToken);
        await _manifests.SaveAsync(manifest, cancellationToken);
        return new SaveSyncReport(results);
    }

    // The transfer is one step of the pass, but for a sync that actually moves data it is nearly
    // all of the wall clock: everything above only stages files locally. Reporting it as its own
    // phase is what keeps the caller from showing a finished counter while the upload runs.
    private async Task FlushAsync(
        IProgress<SaveSyncProgress>? progress,
        int unitCount,
        CancellationToken cancellationToken)
    {
        if (progress is null)
        {
            await _remote.FlushAsync(cancellationToken: cancellationToken);
            return;
        }

        progress.Report(new SaveSyncProgress(
            unitCount, unitCount, "Transferring to the cloud", SaveSyncAction.Upload, SaveSyncPhase.Transferring));
        // The counts here are the transfer's own — how many saves it is actually sending — not the
        // reconciliation's unit count, most of which needed no transfer at all.
        var transferProgress = new Progress<SaveTransferProgress>(transfer => progress.Report(new SaveSyncProgress(
            transfer.CompletedUnits,
            transfer.TotalUnits,
            "Transferring to the cloud",
            SaveSyncAction.Upload,
            SaveSyncPhase.Transferring,
            transfer.Percent)));
        await _remote.FlushAsync(transferProgress, cancellationToken);
    }

    /// <summary>
    /// Forces every present unit in one direction regardless of the baseline — the manual
    /// "Upload local → cloud" / "Download cloud → local" overwrite. A download still backs up the
    /// local copy first; an upload still preserves the (losing) remote copy as a backup.
    /// </summary>
    public async Task<SaveSyncReport> ForceAsync(
        ISaveLocationProvider provider,
        SaveSyncDirection direction,
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var manifest = await _manifests.LoadAsync(cancellationToken);
        var remoteSnapshots = provider.SelectRemoteUnits(await _remote.ListAsync(cancellationToken))
            .ToDictionary(snapshot => snapshot.UnitId, StringComparer.Ordinal);
        var results = new List<SaveUnitSyncResult>();

        if (direction == SaveSyncDirection.Upload)
        {
            var localUnits = await provider.GetSaveUnitsAsync(cancellationToken);
            var localSnapshots = new Dictionary<string, SaveUnitSnapshot>(StringComparer.Ordinal);
            var incompatibleLocal = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var unit in localUnits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await _local.SnapshotAsync(unit.UnitId, cancellationToken) is { } snapshot)
                {
                    remoteSnapshots.TryGetValue(unit.UnitId, out var remoteSnapshot);
                    snapshot = PreserveCompatibilityProvenance(snapshot, manifest.Get(unit.UnitId), remoteSnapshot);
                    if (provider.GetRemoteIncompatibilityReason(snapshot) is { } incompatibility)
                        incompatibleLocal[unit.UnitId] = incompatibility + " The local state was not uploaded.";
                    else
                        localSnapshots[unit.UnitId] = snapshot;
                }
            }

            // Only the units whose remote copy differs are read back, to preserve it as a backup.
            _remote.ExpectDownloads(localSnapshots
                .Where(pair => remoteSnapshots.TryGetValue(pair.Key, out var remote) &&
                    !ContentEquals(remote.ContentHash, pair.Value.ContentHash))
                .Select(pair => pair.Key));

            var completed = 0;
            foreach (var unit in localUnits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!localSnapshots.TryGetValue(unit.UnitId, out var localSnapshot))
                {
                    if (incompatibleLocal.TryGetValue(unit.UnitId, out var incompatibility))
                        results.Add(new SaveUnitSyncResult(unit.UnitId, SaveSyncAction.Skipped, incompatibility));
                    completed++;
                    continue;
                }

                progress?.Report(new SaveSyncProgress(completed, localUnits.Count, unit.DisplayName, SaveSyncAction.Upload));
                if (remoteSnapshots.TryGetValue(unit.UnitId, out var existingRemote) &&
                    !ContentEquals(existingRemote.ContentHash, localSnapshot.ContentHash))
                {
                    await BackupRemoteAsync(_local, unit.UnitId, "Overwritten by a forced upload.", cancellationToken);
                }

                await UploadAsync(_local, unit.UnitId, localSnapshot, cancellationToken);
                manifest = manifest.With(NextBaseline(localSnapshot, manifest.Get(unit.UnitId)));
                results.Add(new SaveUnitSyncResult(unit.UnitId, SaveSyncAction.Upload, "Forced upload of the local save."));
                completed++;
            }
        }
        else
        {
            _remote.ExpectDownloads(remoteSnapshots.Keys);
            var completed = 0;
            foreach (var (unitId, remoteSnapshot) in remoteSnapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new SaveSyncProgress(completed, remoteSnapshots.Count, unitId, SaveSyncAction.Download));
                if (provider.GetRemoteIncompatibilityReason(remoteSnapshot) is { } incompatibility)
                {
                    results.Add(new SaveUnitSyncResult(unitId, SaveSyncAction.Skipped, incompatibility));
                    completed++;
                    continue;
                }
                try
                {
                    var localSnapshot = await _local.SnapshotAsync(unitId, cancellationToken);
                    if (localSnapshot is not null && !ContentEquals(localSnapshot.ContentHash, remoteSnapshot.ContentHash))
                        await _local.BackupLocalAsync(unitId, "Overwritten by a forced download.", cancellationToken);

                    await DownloadAsync(_local, unitId, remoteSnapshot, cancellationToken);
                    manifest = manifest.With(NextBaseline(remoteSnapshot, manifest.Get(unitId)));
                    results.Add(new SaveUnitSyncResult(unitId, SaveSyncAction.Download, "Forced download of the cloud save."));
                }
                catch (CloudPayloadMissingException)
                {
                    results.Add(new SaveUnitSyncResult(
                        unitId,
                        SaveSyncAction.Skipped,
                        "The cloud copy is missing; the local save was left untouched."));
                }
                catch (SaveUnitNotResolvableException ex)
                {
                    // Forcing a direction cannot force a layout this machine does not use.
                    results.Add(new SaveUnitSyncResult(
                        unitId,
                        SaveSyncAction.Skipped,
                        DescribeUnresolvable(ex)));
                }

                completed++;
            }
        }

        await FlushAsync(progress, results.Count, cancellationToken);
        await _manifests.SaveAsync(manifest, cancellationToken);
        return new SaveSyncReport(results);
    }

    private static string DescribeUnresolvable(SaveUnitNotResolvableException exception) =>
        string.IsNullOrWhiteSpace(exception.UserReason)
            ? "This machine's emulator configuration has no place for this save, so it was left in the cloud untouched."
            : exception.UserReason;

    private async Task<SaveSyncManifest> ApplyAsync(
        ILocalSaveEndpoint local,
        string unitId,
        SaveSyncAction action,
        SaveUnitSnapshot? localSnapshot,
        SaveUnitSnapshot? remoteSnapshot,
        SaveUnitBaseline? baseline,
        SaveSyncManifest manifest,
        CancellationToken cancellationToken)
    {
        switch (action)
        {
            case SaveSyncAction.None:
                // Content already agrees. Record or repair the baseline so a pass interrupted after
                // its cloud commit but before its manifest write heals on the next run. Leaving the
                // older hash here would make the next one-sided edit look like a two-sided conflict.
                if (localSnapshot is not null && remoteSnapshot is not null &&
                    (baseline is null ||
                     !ContentEquals(baseline.ContentHash, remoteSnapshot.ContentHash) ||
                     !string.Equals(baseline.Compatibility, remoteSnapshot.Compatibility, StringComparison.Ordinal)))
                    return manifest.With(NextBaseline(remoteSnapshot, baseline));
                return manifest;

            case SaveSyncAction.Upload:
                await UploadAsync(local, unitId, localSnapshot!, cancellationToken);
                return manifest.With(NextBaseline(localSnapshot!, baseline));

            case SaveSyncAction.Download:
                if (localSnapshot is not null)
                    await local.BackupLocalAsync(unitId, "Overwritten by a newer cloud save.", cancellationToken);
                await DownloadAsync(local, unitId, remoteSnapshot!, cancellationToken);
                return manifest.With(NextBaseline(remoteSnapshot!, baseline));

            case SaveSyncAction.ConflictLocalWins:
                await BackupRemoteAsync(local, unitId, "Superseded by a newer local save in a conflict.", cancellationToken);
                await UploadAsync(local, unitId, localSnapshot!, cancellationToken);
                return manifest.With(NextBaseline(localSnapshot!, baseline));

            case SaveSyncAction.ConflictRemoteWins:
                await local.BackupLocalAsync(unitId, "Superseded by a newer cloud save in a conflict.", cancellationToken);
                await DownloadAsync(local, unitId, remoteSnapshot!, cancellationToken);
                return manifest.With(NextBaseline(remoteSnapshot!, baseline));

            default:
                return manifest;
        }
    }

    private async Task UploadAsync(
        ILocalSaveEndpoint local,
        string unitId,
        SaveUnitSnapshot localSnapshot,
        CancellationToken cancellationToken)
    {
        await using var content = await local.ReadAsync(unitId, cancellationToken);
        await _remote.UploadAsync(
            unitId,
            content,
            localSnapshot.ContentHash,
            localSnapshot.ModifiedUtc,
            cancellationToken,
            localSnapshot.Compatibility);
    }

    private async Task DownloadAsync(
        ILocalSaveEndpoint local,
        string unitId,
        SaveUnitSnapshot remoteSnapshot,
        CancellationToken cancellationToken)
    {
        await using var content = await _remote.DownloadAsync(unitId, cancellationToken);
        // Carry the cloud copy's modified time onto the written unit so both sides converge on
        // one timestamp rather than stamping "now" and manufacturing a future false conflict.
        await local.WriteAsync(
            unitId,
            content,
            remoteSnapshot.ContentHash,
            remoteSnapshot.ModifiedUtc,
            cancellationToken);
    }

    // The endpoint is passed in rather than read from _local: a multi-provider sync reconciles
    // units from several providers, and each unit's backup must be written by the endpoint that
    // owns it. Using _local here would hand a PPSSPP unit to the PCSX2 endpoint, whose provider
    // refuses to resolve it and fails the whole run.
    private async Task BackupRemoteAsync(
        ILocalSaveEndpoint local,
        string unitId,
        string reason,
        CancellationToken cancellationToken)
    {
        await using var content = await _remote.DownloadAsync(unitId, cancellationToken);
        await local.BackupIncomingAsync(unitId, content, reason, cancellationToken);
    }

    private static SaveUnitBaseline NextBaseline(SaveUnitSnapshot snapshot, SaveUnitBaseline? previous) =>
        new(
            snapshot.UnitId,
            snapshot.ContentHash,
            snapshot.ModifiedUtc,
            (previous?.Revision ?? 0) + 1,
            snapshot.Compatibility);

    private static SaveUnitSnapshot PreserveCompatibilityProvenance(
        SaveUnitSnapshot local,
        SaveUnitBaseline? baseline,
        SaveUnitSnapshot? remote)
    {
        if (local.Compatibility is null)
            return local;
        if (baseline is not null &&
            ContentEquals(local.ContentHash, baseline.ContentHash) &&
            !string.IsNullOrWhiteSpace(baseline.Compatibility))
        {
            return local with { Compatibility = baseline.Compatibility };
        }
        if (remote is not null &&
            ContentEquals(local.ContentHash, remote.ContentHash) &&
            !string.IsNullOrWhiteSpace(remote.Compatibility))
        {
            return local with { Compatibility = remote.Compatibility };
        }
        return local;
    }

    private static bool ContentEquals(string first, string second) =>
        string.Equals(first, second, StringComparison.Ordinal);

    // Every action that reads the remote payload: a download, and either side of a conflict — the
    // local winner still fetches the cloud copy to preserve it as a backup.
    private static bool NeedsRemotePayload(SaveSyncAction action) =>
        action is SaveSyncAction.Download or SaveSyncAction.ConflictRemoteWins or SaveSyncAction.ConflictLocalWins;

    private sealed record SyncWorkItem(
        string UnitId,
        string DisplayName,
        ILocalSaveEndpoint LocalEndpoint,
        ISaveLocationProvider Provider);

    private sealed record PlannedUnit(
        SyncWorkItem Item,
        SaveSyncDecision Decision,
        SaveUnitSnapshot? LocalSnapshot,
        SaveUnitSnapshot? RemoteSnapshot,
        SaveUnitBaseline? Baseline);
}
