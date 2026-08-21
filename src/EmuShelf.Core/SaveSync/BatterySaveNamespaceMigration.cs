namespace EmuShelf.Core.SaveSync;

/// <summary>
/// One-time, copy-only re-key of existing cloud <em>battery</em> saves from the old emulator-scoped
/// namespace (<c>duckstation/</c>, <c>pcsx2/</c>, <c>retroarch/&lt;sys&gt;/</c>, …) to the new
/// system-scoped one (<c>playstation/</c>, <c>playstation2/</c>, <c>&lt;sys&gt;/</c>, …). See
/// DECISIONS 2026-08-21 and docs/android-save-sync-model.md.
///
/// <para>
/// The transport has no delete by design (<see cref="ICloudSyncTransport"/>), so this <em>copies</em>
/// each old-key entry to its new key and leaves the original frozen in the cloud — no provider claims
/// the old battery key any more, so it goes inert but is never removed. The pass is idempotent (an
/// entry whose new key already exists is skipped), so a caller may run it until it reports done and
/// persist that once.
/// </para>
///
/// <para>
/// Save-state, cheat, and patch sub-namespaces are deliberately left untouched: states stay
/// emulator-scoped (two emulators for one system can write same-named states), and cheats/patches are
/// legacy payloads no longer synced.
/// </para>
/// </summary>
public sealed class BatterySaveNamespaceMigration
{
    // The frozen v1→v2 rename table: former emulator-scoped battery prefix → new system-scoped prefix.
    // Historical data — new systems added later are born system-keyed and never need an entry. Matched
    // longest-prefix-first so "dolphin/gc/" wins over any shorter candidate. RetroArch and Dolphin
    // carried the system inside the old prefix; every other emulator mapped one prefix to one system.
    private static readonly (string Old, string New)[] PrefixMap = BuildMap();

    // Sub-namespaces excluded from the remap: their first path segment after the emulator prefix.
    // States keep their emulator-scoped key; cheats/patches are legacy and no longer synced.
    private static readonly string[] ExcludedSubNamespaces = ["states", "cheats", "patches"];

    private readonly ICloudSyncTransport _transport;

    public BatterySaveNamespaceMigration(ICloudSyncTransport transport) => _transport = transport;

    /// <summary>
    /// Copies every re-keyable battery entry to its system-scoped key when that key is not already
    /// present, then flushes. Returns the number of entries copied (0 when nothing needed migrating).
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var snapshots = await _transport.ListAsync(cancellationToken);
        var existing = snapshots.Select(snapshot => snapshot.UnitId).ToHashSet(StringComparer.Ordinal);

        var copied = 0;
        foreach (var snapshot in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (MapToSystemKey(snapshot.UnitId) is not { } newUnitId)
                continue;
            // Idempotent: skip when the destination already exists (a prior run, or a machine that
            // already syncs under the new key). Track locally too so two old keys that map to the same
            // new key never both copy in one pass.
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
    /// Re-keys the local sync baseline manifest to match the cloud re-key: for every baseline under an
    /// old emulator-scoped battery key, adds an equivalent baseline under the new system-scoped key (when
    /// one is not already present). Without this the migrated cloud entry has no baseline on the machine
    /// that migrated it, so the first post-upgrade sync of a locally-edited save degrades from a clean
    /// upload to a conflict-with-backup. The old baselines are left in place (harmless, inert). Returns
    /// the same instance when nothing changed.
    /// </summary>
    public static SaveSyncManifest RekeyManifestBaselines(SaveSyncManifest manifest)
    {
        var result = manifest;
        foreach (var baseline in manifest.Baselines)
        {
            if (MapToSystemKey(baseline.UnitId) is { } newUnitId && result.Get(newUnitId) is null)
                result = result.With(baseline with { UnitId = newUnitId });
        }

        return result;
    }

    /// <summary>
    /// The system-scoped key an old battery unit id maps to, or null when the id is not a re-keyable
    /// battery entry (already system-keyed, or a state/cheat/patch entry that keeps its key).
    /// </summary>
    public static string? MapToSystemKey(string unitId)
    {
        if (string.IsNullOrEmpty(unitId))
            return null;
        foreach (var (old, @new) in PrefixMap)
        {
            if (!unitId.StartsWith(old, StringComparison.Ordinal))
                continue;
            var rest = unitId[old.Length..];
            var firstSegment = rest.Split('/', 2)[0];
            if (ExcludedSubNamespaces.Contains(firstSegment, StringComparer.Ordinal))
                return null;
            return @new + rest;
        }

        return null;
    }

    private static (string, string)[] BuildMap()
    {
        var map = new List<(string Old, string New)>
        {
            ("duckstation/", "playstation/"),
            ("pcsx2/", "playstation2/"),
            ("rpcs3/", "playstation3/"),
            ("ppsspp/", "psp/"),
            ("azahar/", "3ds/"),
            ("dolphin/gc/", "gamecube/"),
            ("dolphin/wii/", "wii/"),
        };
        // Every RetroArch system carried "retroarch/<systemId>/" and now keys by "<systemId>/".
        foreach (var systemId in new[]
                 {
                     "megadrive", "snes", "nds", "gba", "gbc", "nes", "dreamcast", "arcade", "playstation",
                 })
        {
            map.Add(($"retroarch/{systemId}/", $"{systemId}/"));
        }

        // Longest prefix first so a more specific key (dolphin/gc/) is preferred over any shorter one.
        return map.OrderByDescending(entry => entry.Old.Length).ToArray();
    }
}
