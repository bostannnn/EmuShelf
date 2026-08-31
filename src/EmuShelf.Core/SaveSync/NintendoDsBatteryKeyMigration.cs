namespace EmuShelf.Core.SaveSync;

/// <summary>
/// One-time, copy-only re-key of existing cloud Nintendo DS battery saves from the file-name key
/// (<c>nds/Game.srm</c>) to the cross-emulator one (<c>nds/battery/Game</c>) introduced with
/// standalone melonDS support. See <see cref="NintendoDsBatterySaveKey"/> and DECISIONS 2026-09-01.
///
/// <para>
/// Same contract as <see cref="BatterySaveNamespaceMigration"/>: the transport has no delete, so each
/// old entry is <em>copied</em> to its canonical key and the original is left frozen in the cloud. No
/// provider claims the old key any more, so it goes inert but is never removed. The pass is
/// idempotent — an entry whose canonical key already exists is skipped — so a caller may run it until
/// it reports done and persist that once.
/// </para>
///
/// <para>
/// Only raw battery dumps (<c>.srm</c>/<c>.sav</c>) are re-keyed. A DeSmuME <c>.dsv</c> is not
/// interchangeable with them and keeps its file-name key, as do save states (which stay
/// emulator-scoped) and every other system.
/// </para>
/// </summary>
public sealed class NintendoDsBatteryKeyMigration
{
    private readonly ICloudSyncTransport _transport;

    public NintendoDsBatteryKeyMigration(ICloudSyncTransport transport) => _transport = transport;

    /// <summary>
    /// Copies every re-keyable DS battery entry to its canonical key when that key is not already
    /// present, then flushes. Returns the number of entries copied (0 when nothing needed migrating).
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = await _transport.ListAsync(cancellationToken);
        var existing = snapshots.Select(snapshot => snapshot.UnitId).ToHashSet(StringComparer.Ordinal);

        var copied = 0;
        // Newest first, so when one game holds both a .srm and a .sav copy (the same save written by
        // two cores) the one the user played last becomes the canonical entry, and the older one is
        // skipped by the idempotence guard rather than overwriting it.
        foreach (var snapshot in snapshots.OrderByDescending(snapshot => snapshot.ModifiedUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NintendoDsBatterySaveKey.MapLegacyUnitId(snapshot.UnitId) is not { } newUnitId)
                continue;
            if (!existing.Add(newUnitId))
                continue;

            await using var content = await _transport.DownloadAsync(snapshot.UnitId, cancellationToken);
            await _transport.UploadAsync(
                newUnitId,
                content,
                snapshot.ContentHash,
                snapshot.ModifiedUtc,
                cancellationToken,
                snapshot.Compatibility);
            copied++;
        }

        if (copied > 0)
            await _transport.FlushAsync(cancellationToken: cancellationToken);
        return copied;
    }

    /// <summary>
    /// Re-keys the local sync baseline manifest to match the cloud re-key, so this machine's first
    /// post-migration sync of a locally-edited DS save is a clean upload rather than a
    /// conflict-with-backup. The old baselines are left in place (harmless, inert). Returns the same
    /// instance when nothing changed.
    /// </summary>
    public static SaveSyncManifest RekeyManifestBaselines(SaveSyncManifest manifest)
    {
        var result = manifest;
        foreach (var baseline in manifest.Baselines)
        {
            if (NintendoDsBatterySaveKey.MapLegacyUnitId(baseline.UnitId) is { } newUnitId &&
                result.Get(newUnitId) is null)
            {
                result = result.With(baseline with { UnitId = newUnitId });
            }
        }

        return result;
    }
}
