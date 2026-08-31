namespace EmuShelf.Core.Settings;

/// <summary>
/// Per-system save-location state. The override is the user's explicit choice; the last-success and
/// last-error fields let Settings report each platform's own outcome without a second store.
/// </summary>
public sealed record SaveLocationSettings
{
    /// <summary>An explicit save location chosen by the user, or null to derive it from the emulator.</summary>
    public string? DirectoryOverride { get; init; }

    /// <summary>
    /// An explicit save-state folder chosen by the user, or null to derive it from the emulator.
    /// Mirrors <see cref="DirectoryOverride"/> for save states so a mis-detected state folder can be
    /// corrected the same way a save folder can.
    /// </summary>
    public string? StateDirectoryOverride { get; init; }

    /// <summary>When this system last synchronized successfully, or null if it never has.</summary>
    public DateTimeOffset? LastSuccessUtc { get; init; }

    /// <summary>The most recent failure message for this system, or null after a success.</summary>
    public string? LastError { get; init; }

    /// <summary>
    /// What the last successful pass left undone and why — saves this machine's configuration has
    /// no place for, or cloud copies that were not there. A sync can succeed and still not have
    /// moved a save the user expected, and that is the case worth explaining in the row.
    /// </summary>
    public string? LastNotice { get; init; }

    /// <summary>Whether automatic and manual syncs include guarded emulator save states.</summary>
    public bool SyncSaveStates { get; init; }
}

/// <summary>How EmuShelf reaches the cloud.</summary>
public enum CloudTransportKind
{
    /// <summary>
    /// The retired external rclone binary. No transport is backed by this value any more; it is kept
    /// only so a settings.json written by an older build still deserializes. A stored connection with
    /// this kind is treated as not configured (see the coordinator's <c>IsConfigured</c>), so the user
    /// simply reconnects through the built-in client. A future backend (e.g. Yandex Disk) adds its own
    /// enum value rather than reusing this one.
    /// </summary>
    Rclone,

    /// <summary>
    /// EmuShelf's own Google Drive client, talking to the API directly. Needs no external binary,
    /// which is what makes it the only transport — and the only option on Android.
    /// </summary>
    GoogleDrive,
}

/// <summary>
/// Portable cloud save-sync configuration. Holds no secret: the built-in Google Drive transport keeps
/// its refresh token in a protected blob beside this file, never here. Empty until the user connects.
/// </summary>
public sealed record CloudSaveSyncSettings
{
    private static readonly IReadOnlyDictionary<string, SaveLocationSettings> EmptySaveLocations =
        new Dictionary<string, SaveLocationSettings>(StringComparer.Ordinal);

    /// <summary>The system ids whose save location used to have a dedicated settings field.</summary>
    public const string Pcsx2SystemId = "playstation2";

    /// <summary>The system id whose Memory Stick override used to have a dedicated settings field.</summary>
    public const string PpssppSystemId = "psp";

    /// <summary>Whether save sync is turned on.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Which transport carries the saves. Defaults to <see cref="CloudTransportKind.Rclone"/> purely
    /// for back-compat: a settings.json written before this field existed deserializes to that value,
    /// and such a connection is then treated as not configured so the user reconnects through the
    /// built-in client. A fresh connect always writes <see cref="CloudTransportKind.GoogleDrive"/>.
    /// </summary>
    public CloudTransportKind TransportKind { get; init; } = CloudTransportKind.Rclone;

    /// <summary>
    /// Legacy rclone remote name (e.g. <c>emushelf-gdrive</c>). Not a secret, and no longer used by any
    /// transport — retained only so an older settings.json still deserializes. Null until connected.
    /// </summary>
    public string? RemoteName { get; init; }

    /// <summary>The folder within the remote that holds EmuShelf saves (e.g. <c>EmuShelf/Saves</c>).</summary>
    public string? CloudFolder { get; init; }

    /// <summary>
    /// The provider's own id for <see cref="CloudFolder"/>, cached after the first lookup. Google
    /// Drive has no real paths: reaching <c>EmuShelf/Saves/index.json</c> from the account root
    /// costs one listing request per folder segment, every call, and those requests are what a
    /// launch waits on. Not a secret — it identifies a folder, and grants no access to it.
    /// Null until resolved; any lookup failure simply falls back to the path.
    /// </summary>
    public string? CloudFolderId { get; init; }

    /// <summary>
    /// Legacy PCSX2 configuration directory. <see cref="SaveLocations"/> is authoritative; this is
    /// retained so an existing settings.json still loads and an older EmuShelf build still reads
    /// the value after this one has written it.
    /// </summary>
    public string? Pcsx2ConfigDirectory { get; init; }

    /// <summary>Legacy PPSSPP Memory Stick override. See <see cref="Pcsx2ConfigDirectory"/>.</summary>
    public string? PpssppMemoryStickDirectory { get; init; }

    /// <summary>Per-system save locations, keyed by stable system id.</summary>
    public IReadOnlyDictionary<string, SaveLocationSettings> SaveLocations { get; init; } =
        new Dictionary<string, SaveLocationSettings>(StringComparer.Ordinal);

    /// <summary>
    /// Whether this machine has already run the one-time re-key of cloud battery saves from the old
    /// emulator-scoped namespace to the new system-scoped one (see DECISIONS 2026-08-21). Set once, after
    /// a successful copy-only pass, so the migration never runs again. Defaults to false so an existing
    /// settings.json triggers it on the first sync after upgrade; a fresh install has nothing to migrate
    /// and flips it on its first pass at effectively no cost.
    /// </summary>
    public bool BatteryNamespaceMigrated { get; init; }

    /// <summary>
    /// Whether this machine has already run the one-time re-key of cloud Nintendo DS battery saves
    /// from the file-name key (<c>nds/Game.srm</c>) to the cross-emulator one (<c>nds/battery/Game</c>)
    /// that standalone melonDS and RetroArch now share (see DECISIONS 2026-09-01). Independent of
    /// <see cref="BatteryNamespaceMigrated"/> — that one moved emulator-scoped keys to system-scoped
    /// ones and is already true on machines that need this second pass.
    /// </summary>
    public bool NintendoDsBatteryKeyMigrated { get; init; }

    /// <summary>The explicit save-location override for one system, or null when none is set.</summary>
    public string? GetOverride(string systemId) => OverrideOf(GetLocationByKey(systemId));

    /// <summary>The explicit save-location override for one system's emulator, or null when none is set.</summary>
    public string? GetOverride(string systemId, string emulatorId) => OverrideOf(GetLocationByKey(Key(systemId, emulatorId)));

    /// <summary>The explicit save-state folder override for one system, or null when none is set.</summary>
    public string? GetStateOverride(string systemId) => StateOverrideOf(GetLocationByKey(systemId));

    /// <summary>The explicit save-state folder override for one system's emulator, or null when none is set.</summary>
    public string? GetStateOverride(string systemId, string emulatorId) => StateOverrideOf(GetLocationByKey(Key(systemId, emulatorId)));

    /// <summary>The stored state for one system, or an empty record when it has none yet.</summary>
    public SaveLocationSettings GetLocation(string systemId) => GetLocationByKey(systemId);

    /// <summary>The stored state for one system's emulator, or an empty record when it has none yet.</summary>
    public SaveLocationSettings GetLocation(string systemId, string emulatorId) => GetLocationByKey(Key(systemId, emulatorId));

    private SaveLocationSettings GetLocationByKey(string key) =>
        SafeSaveLocations.TryGetValue(key, out var location) && location is not null
            ? location
            : new SaveLocationSettings();

    private static string? OverrideOf(SaveLocationSettings location) =>
        string.IsNullOrWhiteSpace(location.DirectoryOverride) ? null : location.DirectoryOverride;

    private static string? StateOverrideOf(SaveLocationSettings location) =>
        string.IsNullOrWhiteSpace(location.StateDirectoryOverride) ? null : location.StateDirectoryOverride;

    /// <summary>Replaces one system's override, leaving its recorded sync outcome intact.</summary>
    public CloudSaveSyncSettings WithOverride(string systemId, string? directory)
    {
        var trimmed = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        return With(systemId, location => location with { DirectoryOverride = trimmed });
    }

    /// <summary>Replaces one system's save-state folder override, leaving its other state intact.</summary>
    public CloudSaveSyncSettings WithStateOverride(string systemId, string? directory)
    {
        var trimmed = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        return With(systemId, location => location with { StateDirectoryOverride = trimmed });
    }

    /// <summary>Updates optional content without changing the platform's save location or result.</summary>
    public CloudSaveSyncSettings WithOptionalContent(string systemId, bool syncSaveStates) =>
        With(systemId, location => location with { SyncSaveStates = syncSaveStates });

    /// <summary>Records a successful sync for one system and clears its last error.</summary>
    /// <param name="notice">What the pass left undone and why, or null when it left nothing.</param>
    public CloudSaveSyncSettings WithSyncSuccess(string systemId, DateTimeOffset completedUtc, string? notice = null) =>
        With(systemId, location => location with
        {
            LastSuccessUtc = completedUtc,
            LastError = null,
            LastNotice = string.IsNullOrWhiteSpace(notice) ? null : notice,
        });

    /// <summary>Records a failure for one system without discarding its last known success.</summary>
    public CloudSaveSyncSettings WithSyncFailure(string systemId, string message) =>
        With(systemId, location => location with { LastError = message });

    /// <summary>Replaces one system emulator's override, leaving its recorded sync outcome intact.</summary>
    public CloudSaveSyncSettings WithOverride(string systemId, string emulatorId, string? directory)
    {
        var trimmed = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        return WithKey(Key(systemId, emulatorId), location => location with { DirectoryOverride = trimmed });
    }

    /// <summary>Replaces one system emulator's save-state folder override, leaving its other state intact.</summary>
    public CloudSaveSyncSettings WithStateOverride(string systemId, string emulatorId, string? directory)
    {
        var trimmed = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        return WithKey(Key(systemId, emulatorId), location => location with { StateDirectoryOverride = trimmed });
    }

    /// <summary>Updates optional content for one system emulator without changing its location or result.</summary>
    public CloudSaveSyncSettings WithOptionalContent(string systemId, string emulatorId, bool syncSaveStates) =>
        WithKey(Key(systemId, emulatorId), location => location with { SyncSaveStates = syncSaveStates });

    /// <summary>Records a successful sync for one system emulator and clears its last error.</summary>
    public CloudSaveSyncSettings WithSyncSuccess(
        string systemId, string emulatorId, DateTimeOffset completedUtc, string? notice = null) =>
        WithKey(Key(systemId, emulatorId), location => location with
        {
            LastSuccessUtc = completedUtc,
            LastError = null,
            LastNotice = string.IsNullOrWhiteSpace(notice) ? null : notice,
        });

    /// <summary>Records a failure for one system emulator without discarding its last known success.</summary>
    public CloudSaveSyncSettings WithSyncFailure(string systemId, string emulatorId, string message) =>
        WithKey(Key(systemId, emulatorId), location => location with { LastError = message });

    /// <summary>
    /// Re-keys legacy per-system overrides to per-(system, emulator) using each system's active
    /// emulator (the caller supplies the mapping). Existing composite entries always win
    /// ("presence wins"), and the legacy per-system entries are retained so an older build still
    /// reads them. Idempotent — safe to run on every load.
    /// </summary>
    /// <param name="activeEmulatorBySystem">The emulator to attribute each system's legacy override to.</param>
    public CloudSaveSyncSettings MigrateOverridesToPerEmulator(
        IReadOnlyDictionary<string, string> activeEmulatorBySystem)
    {
        ArgumentNullException.ThrowIfNull(activeEmulatorBySystem);
        var locations = new Dictionary<string, SaveLocationSettings>(StringComparer.Ordinal);
        var systemsWithComposite = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, location) in SafeSaveLocations)
        {
            if (string.IsNullOrWhiteSpace(key) || location is null)
                continue;
            locations[key] = location;
            var slash = key.IndexOf('/');
            if (slash > 0)
                systemsWithComposite.Add(key[..slash]);
        }

        foreach (var (systemId, location) in SafeSaveLocations)
        {
            // Only bare system-id entries migrate; a composite key already contains the delimiter.
            if (string.IsNullOrWhiteSpace(systemId) || location is null || systemId.Contains('/'))
                continue;
            // Once any emulator has a per-emulator entry for this system the feature is already active
            // for it, so a bare entry here is a rollback mirror, not a legacy override — never re-key
            // it. Doing so would let switching the active emulator silently inherit another's folder.
            if (systemsWithComposite.Contains(systemId))
                continue;
            if (!activeEmulatorBySystem.TryGetValue(systemId, out var emulatorId) ||
                string.IsNullOrWhiteSpace(emulatorId))
            {
                continue;
            }
            locations[Key(systemId, emulatorId)] = location;
        }

        return this with { SaveLocations = locations };
    }

    /// <summary>
    /// Folds the legacy single-emulator fields into <see cref="SaveLocations"/>. Runs on load; an
    /// entry already present in the dictionary always wins, so a migrated value is never re-applied
    /// over a newer explicit choice.
    /// </summary>
    public CloudSaveSyncSettings NormalizeSaveLocations()
    {
        // Nullable annotations do not stop System.Text.Json from assigning an explicit JSON null
        // to the dictionary or one of its values. Sanitize the hand-editable settings shape before
        // any migration or lookup so an invalid optional entry cannot crash application startup.
        var locations = new Dictionary<string, SaveLocationSettings>(StringComparer.Ordinal);
        foreach (var (systemId, location) in SafeSaveLocations)
        {
            if (!string.IsNullOrWhiteSpace(systemId) && location is not null)
                locations[systemId] = location;
        }

        var normalized = this with { SaveLocations = locations };
        // Presence, rather than a non-empty override, is authoritative. A newer settings file may
        // intentionally contain an empty override plus result metadata; a stale legacy field must
        // not resurrect the path the user cleared.
        if (!string.IsNullOrWhiteSpace(Pcsx2ConfigDirectory) && !locations.ContainsKey(Pcsx2SystemId))
            normalized = normalized.WithOverride(Pcsx2SystemId, Pcsx2ConfigDirectory);
        if (!string.IsNullOrWhiteSpace(PpssppMemoryStickDirectory) && !locations.ContainsKey(PpssppSystemId))
            normalized = normalized.WithOverride(PpssppSystemId, PpssppMemoryStickDirectory);
        return normalized;
    }

    // The synthesized record equality would compare SaveLocations by reference, so two settings
    // objects with identical contents (a round-trip through settings.json, for instance) would
    // report as different. Compare the dictionary structurally instead.
    public bool Equals(CloudSaveSyncSettings? other)
    {
        if (other is null ||
            Enabled != other.Enabled ||
            TransportKind != other.TransportKind ||
            RemoteName != other.RemoteName ||
            CloudFolder != other.CloudFolder ||
            BatteryNamespaceMigrated != other.BatteryNamespaceMigrated ||
            NintendoDsBatteryKeyMigrated != other.NintendoDsBatteryKeyMigrated ||
            Pcsx2ConfigDirectory != other.Pcsx2ConfigDirectory ||
            PpssppMemoryStickDirectory != other.PpssppMemoryStickDirectory)
        {
            return false;
        }

        var locations = SafeSaveLocations.Where(entry => entry.Value is not null).ToArray();
        var otherLocations = other.SafeSaveLocations.Where(entry => entry.Value is not null).ToArray();
        return locations.Length == otherLocations.Length && locations.All(entry =>
            other.SafeSaveLocations.TryGetValue(entry.Key, out var value) && value is not null && entry.Value == value);
    }

    public override int GetHashCode() => HashCode.Combine(
        Enabled,
        TransportKind,
        RemoteName,
        CloudFolder,
        HashCode.Combine(BatteryNamespaceMigrated, NintendoDsBatteryKeyMigrated),
        Pcsx2ConfigDirectory,
        PpssppMemoryStickDirectory,
        SafeSaveLocations.Count(entry => entry.Value is not null));

    private CloudSaveSyncSettings With(string systemId, Func<SaveLocationSettings, SaveLocationSettings> update)
    {
        var updated = WithKey(systemId, update);
        // Mirror the two originally supported systems back onto their legacy fields so writing a
        // newer settings.json cannot strand a user who rolls back to an older build.
        return updated with
        {
            Pcsx2ConfigDirectory = updated.GetOverride(Pcsx2SystemId),
            PpssppMemoryStickDirectory = updated.GetOverride(PpssppSystemId),
        };
    }

    private CloudSaveSyncSettings WithKey(string key, Func<SaveLocationSettings, SaveLocationSettings> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var locations = new Dictionary<string, SaveLocationSettings>(StringComparer.Ordinal);
        foreach (var (existingKey, location) in SafeSaveLocations)
        {
            if (!string.IsNullOrWhiteSpace(existingKey) && location is not null)
                locations[existingKey] = location;
        }
        locations[key] = update(GetLocationByKey(key));
        return this with { SaveLocations = locations };
    }

    // The composite key a per-(system, emulator) save location is stored under. System and emulator
    // ids are simple lowercase tokens with no delimiter, so a single "/" separates them unambiguously
    // and never collides with a bare system-id key.
    private static string Key(string systemId, string emulatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        return $"{systemId}/{emulatorId}";
    }

    private IReadOnlyDictionary<string, SaveLocationSettings> SafeSaveLocations =>
        SaveLocations ?? EmptySaveLocations;
}
