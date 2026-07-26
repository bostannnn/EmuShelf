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
}

/// <summary>
/// Portable cloud save-sync configuration. Holds no secret: the OAuth token lives only in rclone's
/// own config file, never here. Empty until the user connects a remote.
/// </summary>
public sealed record CloudSaveSyncSettings
{
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
        SaveLocations.TryGetValue(systemId, out var location) &&
        !string.IsNullOrWhiteSpace(location.DirectoryOverride)
            ? location.DirectoryOverride
            : null;

    /// <summary>The stored state for one system, or an empty record when it has none yet.</summary>
    public SaveLocationSettings GetLocation(string systemId) =>
        SaveLocations.TryGetValue(systemId, out var location) ? location : new SaveLocationSettings();

    /// <summary>Replaces one system's override, leaving its recorded sync outcome intact.</summary>
    public CloudSaveSyncSettings WithOverride(string systemId, string? directory)
    {
        var trimmed = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        return With(systemId, location => location with { DirectoryOverride = trimmed });
    }

    /// <summary>Records a successful sync for one system and clears its last error.</summary>
    public CloudSaveSyncSettings WithSyncSuccess(string systemId, DateTimeOffset completedUtc) =>
        With(systemId, location => location with { LastSuccessUtc = completedUtc, LastError = null });

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
        var normalized = this;
        if (!string.IsNullOrWhiteSpace(Pcsx2ConfigDirectory) && normalized.GetOverride(Pcsx2SystemId) is null)
            normalized = normalized.WithOverride(Pcsx2SystemId, Pcsx2ConfigDirectory);
        if (!string.IsNullOrWhiteSpace(PpssppMemoryStickDirectory) && normalized.GetOverride(PpssppSystemId) is null)
            normalized = normalized.WithOverride(PpssppSystemId, PpssppMemoryStickDirectory);
        return normalized;
    }

    // The synthesized record equality would compare SaveLocations by reference, so two settings
    // objects with identical contents (a round-trip through settings.json, for instance) would
    // report as different. Compare the dictionary structurally instead.
    public bool Equals(CloudSaveSyncSettings? other) =>
        other is not null &&
        Enabled == other.Enabled &&
        RemoteName == other.RemoteName &&
        CloudFolder == other.CloudFolder &&
        Pcsx2ConfigDirectory == other.Pcsx2ConfigDirectory &&
        PpssppMemoryStickDirectory == other.PpssppMemoryStickDirectory &&
        SaveLocations.Count == other.SaveLocations.Count &&
        SaveLocations.All(entry =>
            other.SaveLocations.TryGetValue(entry.Key, out var value) && entry.Value == value);

    public override int GetHashCode() => HashCode.Combine(
        Enabled,
        RemoteName,
        CloudFolder,
        Pcsx2ConfigDirectory,
        PpssppMemoryStickDirectory,
        SaveLocations.Count);

    private CloudSaveSyncSettings With(string systemId, Func<SaveLocationSettings, SaveLocationSettings> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        var locations = new Dictionary<string, SaveLocationSettings>(SaveLocations, StringComparer.Ordinal)
        {
            [systemId] = update(GetLocation(systemId)),
        };

        var updated = this with { SaveLocations = locations };
        // Mirror the two originally supported systems back onto their legacy fields so writing a
        // newer settings.json cannot strand a user who rolls back to an older build.
        return updated with
        {
            Pcsx2ConfigDirectory = updated.GetOverride(Pcsx2SystemId),
            PpssppMemoryStickDirectory = updated.GetOverride(PpssppSystemId),
        };
    }
}
