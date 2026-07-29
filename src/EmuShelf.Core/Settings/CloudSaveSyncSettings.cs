namespace EmuShelf.Core.Settings;

/// <summary>
/// Per-system save-location state. The override is the user's explicit choice; the last-success and
/// last-error fields let Settings report each platform's own outcome without a second store.
/// </summary>
public sealed record SaveLocationSettings
{
    /// <summary>An explicit save location chosen by the user, or null to derive it from the emulator.</summary>
    public string? DirectoryOverride { get; init; }

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

    /// <summary>Whether portable cheat and patch files participate for this platform.</summary>
    public bool SyncCheatsAndPatches { get; init; }

    /// <summary>Whether manually initiated syncs include guarded emulator save states.</summary>
    public bool SyncSaveStates { get; init; }

    /// <summary>Maximum manual state slots exposed per game. Local files are never deleted.</summary>
    public int SaveStateRetention { get; init; } = 3;
}

/// <summary>
/// Portable cloud save-sync configuration. Holds no secret: the OAuth token lives only in rclone's
/// own config file, never here. Empty until the user connects a remote.
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

    /// <summary>The rclone remote name (e.g. <c>emushelf-gdrive</c>). Not a secret. Null until connected.</summary>
    public string? RemoteName { get; init; }

    /// <summary>The folder within the remote that holds EmuShelf saves (e.g. <c>EmuShelf/Saves</c>).</summary>
    public string? CloudFolder { get; init; }

    /// <summary>
    /// The Google OAuth client id used for this remote, or null to use rclone's shared client. Not
    /// a secret — it is public by design and identifies the application, not the user. The matching
    /// client secret is deliberately absent: it goes straight to rclone and lives only in rclone's
    /// own config, beside the OAuth token EmuShelf never sees.
    /// </summary>
    public string? GoogleClientId { get; init; }

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

    /// <summary>The explicit save-location override for one system, or null when none is set.</summary>
    public string? GetOverride(string systemId) =>
        SafeSaveLocations.TryGetValue(systemId, out var location) &&
        location is not null &&
        !string.IsNullOrWhiteSpace(location.DirectoryOverride)
            ? location.DirectoryOverride
            : null;

    /// <summary>The stored state for one system, or an empty record when it has none yet.</summary>
    public SaveLocationSettings GetLocation(string systemId) =>
        SafeSaveLocations.TryGetValue(systemId, out var location) && location is not null
            ? location
            : new SaveLocationSettings();

    /// <summary>Replaces one system's override, leaving its recorded sync outcome intact.</summary>
    public CloudSaveSyncSettings WithOverride(string systemId, string? directory)
    {
        var trimmed = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        return With(systemId, location => location with { DirectoryOverride = trimmed });
    }

    /// <summary>Updates optional content without changing the platform's save location or result.</summary>
    public CloudSaveSyncSettings WithOptionalContent(
        string systemId,
        bool syncCheatsAndPatches,
        bool syncSaveStates,
        int saveStateRetention) =>
        With(systemId, location => location with
        {
            SyncCheatsAndPatches = syncCheatsAndPatches,
            SyncSaveStates = syncSaveStates,
            SaveStateRetention = Math.Clamp(saveStateRetention, 1, 10),
        });

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
            RemoteName != other.RemoteName ||
            CloudFolder != other.CloudFolder ||
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
        RemoteName,
        CloudFolder,
        Pcsx2ConfigDirectory,
        PpssppMemoryStickDirectory,
        SafeSaveLocations.Count(entry => entry.Value is not null));

    private CloudSaveSyncSettings With(string systemId, Func<SaveLocationSettings, SaveLocationSettings> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        var locations = new Dictionary<string, SaveLocationSettings>(StringComparer.Ordinal);
        foreach (var (existingSystemId, location) in SafeSaveLocations)
        {
            if (!string.IsNullOrWhiteSpace(existingSystemId) && location is not null)
                locations[existingSystemId] = location;
        }
        locations[systemId] = update(GetLocation(systemId));

        var updated = this with { SaveLocations = locations };
        // Mirror the two originally supported systems back onto their legacy fields so writing a
        // newer settings.json cannot strand a user who rolls back to an older build.
        return updated with
        {
            Pcsx2ConfigDirectory = updated.GetOverride(Pcsx2SystemId),
            PpssppMemoryStickDirectory = updated.GetOverride(PpssppSystemId),
        };
    }

    private IReadOnlyDictionary<string, SaveLocationSettings> SafeSaveLocations =>
        SaveLocations ?? EmptySaveLocations;
}
