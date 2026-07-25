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
            foreach (var remote in allRemoteSnapshots.Values)
            {
                if (remote.UnitId.StartsWith(provider.UnitIdPrefix, StringComparison.Ordinal))
                    unitIds.Add(remote.UnitId);
            }

            foreach (var unitId in unitIds)
            {
                if (!claimedUnitIds.Add(unitId))
                    throw new InvalidOperationException($"More than one save provider claimed unit '{unitId}'.");
                work.Add(new SyncWorkItem(
                    unitId,
                    displayNames.GetValueOrDefault(unitId, unitId),
                    target.LocalEndpoint));
            }
        }

        var results = new List<SaveUnitSyncResult>();
        var completed = 0;
        foreach (var item in work)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var localSnapshot = await item.LocalEndpoint.SnapshotAsync(item.UnitId, cancellationToken);
            allRemoteSnapshots.TryGetValue(item.UnitId, out var remoteSnapshot);
            var baseline = manifest.Get(item.UnitId);

            var decision = SaveSyncPlanner.Decide(localSnapshot, remoteSnapshot, baseline);
            progress?.Report(new SaveSyncProgress(
                completed, work.Count, item.DisplayName, decision.Action));
            manifest = await ApplyAsync(
                item.LocalEndpoint,
                item.UnitId,
                decision.Action,
                localSnapshot,
                remoteSnapshot,
                baseline,
                manifest,
                cancellationToken);

            results.Add(new SaveUnitSyncResult(item.UnitId, decision.Action, decision.Reason));
            completed++;
        }

        await _remote.FlushAsync(cancellationToken);
        await _manifests.SaveAsync(manifest, cancellationToken);
        return new SaveSyncReport(results);
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
        var remoteSnapshots = (await _remote.ListAsync(cancellationToken))
            .Where(snapshot => snapshot.UnitId.StartsWith(provider.UnitIdPrefix, StringComparison.Ordinal))
            .ToDictionary(snapshot => snapshot.UnitId, StringComparer.Ordinal);
        var results = new List<SaveUnitSyncResult>();

        if (direction == SaveSyncDirection.Upload)
        {
            var localUnits = await provider.GetSaveUnitsAsync(cancellationToken);
            var completed = 0;
            foreach (var unit in localUnits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localSnapshot = await _local.SnapshotAsync(unit.UnitId, cancellationToken);
                if (localSnapshot is null)
                {
                    completed++;
                    continue;
                }

                progress?.Report(new SaveSyncProgress(completed, localUnits.Count, unit.DisplayName, SaveSyncAction.Upload));
                if (remoteSnapshots.TryGetValue(unit.UnitId, out var existingRemote) &&
                    !ContentEquals(existingRemote.ContentHash, localSnapshot.ContentHash))
                {
                    await BackupRemoteAsync(unit.UnitId, "Overwritten by a forced upload.", cancellationToken);
                }

                await UploadAsync(_local, unit.UnitId, localSnapshot, cancellationToken);
                manifest = manifest.With(NextBaseline(localSnapshot, manifest.Get(unit.UnitId)));
                results.Add(new SaveUnitSyncResult(unit.UnitId, SaveSyncAction.Upload, "Forced upload of the local save."));
                completed++;
            }
        }
        else
        {
            var completed = 0;
            foreach (var (unitId, remoteSnapshot) in remoteSnapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new SaveSyncProgress(completed, remoteSnapshots.Count, unitId, SaveSyncAction.Download));
                var localSnapshot = await _local.SnapshotAsync(unitId, cancellationToken);
                if (localSnapshot is not null && !ContentEquals(localSnapshot.ContentHash, remoteSnapshot.ContentHash))
                    await _local.BackupLocalAsync(unitId, "Overwritten by a forced download.", cancellationToken);

                await DownloadAsync(_local, unitId, remoteSnapshot, cancellationToken);
                manifest = manifest.With(NextBaseline(remoteSnapshot, manifest.Get(unitId)));
                results.Add(new SaveUnitSyncResult(unitId, SaveSyncAction.Download, "Forced download of the cloud save."));
                completed++;
            }
        }

        await _remote.FlushAsync(cancellationToken);
        await _manifests.SaveAsync(manifest, cancellationToken);
        return new SaveSyncReport(results);
    }

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
                // Content already agrees. Record a baseline the first time it is observed so a
                // later edit on one side is attributed correctly instead of read as a conflict.
                if (baseline is null && localSnapshot is not null && remoteSnapshot is not null)
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
                await BackupRemoteAsync(unitId, "Superseded by a newer local save in a conflict.", cancellationToken);
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
        await _remote.UploadAsync(unitId, content, localSnapshot.ContentHash, localSnapshot.ModifiedUtc, cancellationToken);
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
        await local.WriteAsync(unitId, content, remoteSnapshot.ModifiedUtc, cancellationToken);
    }

    private async Task BackupRemoteAsync(string unitId, string reason, CancellationToken cancellationToken)
    {
        await using var content = await _remote.DownloadAsync(unitId, cancellationToken);
        await _local.BackupIncomingAsync(unitId, content, reason, cancellationToken);
    }

    private static SaveUnitBaseline NextBaseline(SaveUnitSnapshot snapshot, SaveUnitBaseline? previous) =>
        new(snapshot.UnitId, snapshot.ContentHash, snapshot.ModifiedUtc, (previous?.Revision ?? 0) + 1);

    private static bool ContentEquals(string first, string second) =>
        string.Equals(first, second, StringComparison.Ordinal);

    private sealed record SyncWorkItem(string UnitId, string DisplayName, ILocalSaveEndpoint LocalEndpoint);
}
