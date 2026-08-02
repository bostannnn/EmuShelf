using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.App.Services;

internal sealed record StateCompatibility(string Key, string Description)
{
    public static StateCompatibility? Create(
        string emulatorId,
        string? version,
        string? coreVersion = null,
        string? architecture = null)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(architecture))
            return null;

        architecture = architecture.Trim().ToLowerInvariant();
        var rawIdentity = string.Join('|', new[] { emulatorId, architecture, version, coreVersion }
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var readable = Slug(rawIdentity);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity)))[..12].ToLowerInvariant();
        return new StateCompatibility($"{readable}-{hash}", $"{version} · {architecture}");
    }

    private static string Slug(string value)
    {
        var result = new string(value.ToLowerInvariant()
            .Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (result.Contains("--", StringComparison.Ordinal))
            result = result.Replace("--", "-", StringComparison.Ordinal);
        var trimmed = result.Trim('-');
        return trimmed[..Math.Min(trimmed.Length, 48)];
    }
}

internal sealed record AuxiliaryFileSource(
    string Namespace,
    Func<CancellationToken, string?> ResolveRoot,
    Func<string, bool> Include);

internal sealed record AuxiliaryContentLocation(
    string? Directory,
    int EligibleFileCount,
    int TotalFileCount,
    long EligibleBytes,
    string? Compatibility = null,
    string? Warning = null);

/// <summary>
/// Adds allow-listed, per-file save states to an established save provider. The state namespace
/// does not change existing save ids; state compatibility is an optional field in the existing
/// cloud index entries.
/// </summary>
internal sealed class AuxiliarySyncProvider : ISaveLocationProvider
{
    private readonly ISaveLocationProvider _saves;
    private readonly IReadOnlyList<AuxiliaryFileSource> _sources;
    private readonly StateCompatibility? _compatibility;
    private readonly bool _includeBaseSaves;
    private readonly IReadOnlyList<string>? _stateGameKeys;
    private readonly Dictionary<AuxiliaryFileSource, string?> _resolvedRoots = [];
    private readonly object _rootGate = new();

    /// <param name="stateGameKeys">
    /// When set, state files are scoped to one game: only states whose name contains one of these
    /// keys (a launched game's file-stem, serials, and disc ids, normalized) participate. This is the
    /// launch/exit pass — it stops launching one game from hashing and syncing every game's states.
    /// A manual "Sync all" passes none, so it still covers every state.
    /// </param>
    public AuxiliarySyncProvider(
        ISaveLocationProvider saves,
        IReadOnlyList<AuxiliaryFileSource> sources,
        StateCompatibility? compatibility,
        bool includeBaseSaves = true,
        IReadOnlyCollection<string>? stateGameKeys = null)
    {
        _saves = saves;
        _sources = sources;
        _compatibility = compatibility;
        _includeBaseSaves = includeBaseSaves;
        var normalizedKeys = stateGameKeys
            ?.Select(NormalizeStateKey)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        _stateGameKeys = normalizedKeys is { Length: > 0 } ? normalizedKeys : null;
    }

    // Alphanumeric-only, upper-cased, so a save-state file name matches a game key regardless of the
    // separators an emulator uses (spaces, dashes, underscores, dots, region parentheses). This is
    // deliberately fuzzy contains-matching: a false positive only syncs an extra state, while the
    // manual Sync all remains the exact escape hatch.
    private static string NormalizeStateKey(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private bool MatchesStateGame(string fileName)
    {
        if (_stateGameKeys is null)
            return true;
        var normalized = NormalizeStateKey(fileName);
        foreach (var key in _stateGameKeys)
            if (normalized.Contains(key, StringComparison.Ordinal))
                return true;
        return false;
    }

    private bool MatchesRemoteStateGame(string unitId, AuxiliaryFileSource source)
    {
        if (_stateGameKeys is null)
            return true;
        var prefix = Prefix(source);
        if (!unitId.StartsWith(prefix, StringComparison.Ordinal))
            return true;
        // If the id cannot be decoded we cannot tell which game it belongs to, so do not exclude it.
        return !TryDecodeRelativePath(unitId[prefix.Length..], out var relativePath)
            || MatchesStateGame(Path.GetFileName(relativePath));
    }

    public string SystemId => _saves.SystemId;

    public string UnitIdPrefix => _saves.UnitIdPrefix;

    public bool HasStateCompatibility => _compatibility is not null;

    /// <summary>
    /// Resolves each state root independently for Settings. An optional source is advisory: a
    /// missing or unreadable state folder must never hide an otherwise valid save-card location.
    /// </summary>
    public async Task<IReadOnlyList<AuxiliaryContentLocation>> GetContentLocationsAsync(
        CancellationToken cancellationToken = default)
    {
        var locations = new List<AuxiliaryContentLocation>(_sources.Count);
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            locations.Add(await Task.Run(() => Inspect(source, cancellationToken), cancellationToken));
        }
        return locations;
    }

    public string? GetCompatibility(string unitId) =>
        IsStateUnit(unitId)
            ? _compatibility?.Key
            : _includeBaseSaves ? _saves.GetCompatibility(unitId) : null;

    public string? GetRemoteIncompatibilityReason(SaveUnitSnapshot remoteSnapshot)
    {
        if (!IsStateUnit(remoteSnapshot.UnitId))
            return _includeBaseSaves ? _saves.GetRemoteIncompatibilityReason(remoteSnapshot) : null;
        return _compatibility is not null &&
               string.Equals(remoteSnapshot.Compatibility, _compatibility.Key, StringComparison.Ordinal)
            ? null
            : "This save state was written by a different emulator version or CPU architecture. " +
              "It remains available in the cloud and was not restored.";
    }

    public bool OwnsUnit(string unitId)
    {
        if (TryGetOptionalNamespace(unitId, out _))
            return _sources.Any(source => IsInSourceNamespace(unitId, source));
        return _includeBaseSaves && _saves.OwnsUnit(unitId);
    }

    public IReadOnlyList<SaveUnitSnapshot> SelectRemoteUnits(IReadOnlyList<SaveUnitSnapshot> snapshots)
    {
        var selected = _includeBaseSaves
            ? _saves.SelectRemoteUnits(snapshots
                .Where(snapshot => !TryGetOptionalNamespace(snapshot.UnitId, out _))
                .ToArray()).ToList()
            : [];

        foreach (var source in _sources)
            selected.AddRange(snapshots.Where(snapshot =>
                IsInSourceNamespace(snapshot.UnitId, source) &&
                MatchesRemoteStateGame(snapshot.UnitId, source)));

        return selected.DistinctBy(snapshot => snapshot.UnitId, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default)
    {
        var units = _includeBaseSaves
            ? (await _saves.GetSaveUnitsAsync(cancellationToken)).ToList()
            : [];
        foreach (var source in _sources)
            units.AddRange(await Task.Run(() => Enumerate(source, cancellationToken), cancellationToken));
        return units;
    }

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (!TryGetOptionalNamespace(unitId, out _))
            return _includeBaseSaves ? _saves.ResolveUnit(unitId) : null;

        var source = _sources.FirstOrDefault(candidate => IsInSourceNamespace(unitId, candidate));
        if (source is null)
            return null;

        var prefix = Prefix(source);
        var remainder = unitId[prefix.Length..];
        if (!TryDecodeRelativePath(remainder, out var relativePath))
            return null;
        var root = ResolveRoot(source, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(root))
            return null;
        var fullRoot = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!IsUnder(path, fullRoot) || !source.Include(path))
            return null;
        return new SaveUnitLocation(path, fullRoot, SaveUnitKind.File);
    }

    private IReadOnlyList<SaveUnit> Enumerate(AuxiliaryFileSource source, CancellationToken cancellationToken)
    {
        var root = ResolveRoot(source, cancellationToken);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || _compatibility is null)
            return [];

        var fullRoot = Path.GetFullPath(root);
        var files = EnumerateCandidates(source, fullRoot, cancellationToken)
            .Where(file => MatchesStateGame(Path.GetFileName(file.Path)));

        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => new SaveUnit(
                Prefix(source) +
                EncodeRelativePath(file.RelativePath),
                $"{Path.GetFileName(file.Path)} — save state",
                SaveUnitKind.File))
            .ToArray();
    }

    private AuxiliaryContentLocation Inspect(AuxiliaryFileSource source, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = ResolveRoot(source, cancellationToken);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return new AuxiliaryContentLocation(
                    null,
                    0,
                    0,
                    0,
                    Warning: "The emulator configuration does not expose a safe folder for save states.");
            }

            var root = Path.GetFullPath(resolved);
            if (!Directory.Exists(root))
                return new AuxiliaryContentLocation(root, 0, 0, 0, Warning: "The folder does not exist yet.");

            var files = EnumerateCandidates(source, root, cancellationToken);
            var bytes = files.Aggregate(0L, (total, file) => checked(total + new FileInfo(file.Path).Length));
            return new AuxiliaryContentLocation(
                root,
                files.Length,
                files.Length,
                bytes,
                _compatibility?.Description,
                _compatibility is null
                    ? "The emulator/core version could not be detected, so states will not be synced."
                    : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new AuxiliaryContentLocation(null, 0, 0, 0, Warning: ex.Message);
        }
    }

    private FileCandidate[] EnumerateCandidates(
        AuxiliaryFileSource source,
        string fullRoot,
        CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return path;
            })
            .Where(source.Include)
            .Select(path => new FileCandidate(path, Path.GetRelativePath(fullRoot, path)))
            .ToArray();
        return files;
    }

    private string Prefix(AuxiliaryFileSource source) =>
        UnitIdPrefix + source.Namespace.Trim('/') + "/";

    private bool IsInSourceNamespace(string unitId, AuxiliaryFileSource source) =>
        unitId.StartsWith(Prefix(source), StringComparison.Ordinal);

    private bool TryGetOptionalNamespace(string unitId, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return false;
        var remainder = unitId[UnitIdPrefix.Length..];
        var separator = remainder.IndexOf('/');
        value = separator < 0 ? remainder : remainder[..separator];
        return value is "states";
    }

    internal static bool IsManualState(string path)
    {
        var name = Path.GetFileName(path);
        return !name.Contains(".auto", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains(".undo", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) &&
            !name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("laststate", StringComparison.OrdinalIgnoreCase) &&
            !name.Contains("resume", StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveRoot(AuxiliaryFileSource source, CancellationToken cancellationToken)
    {
        lock (_rootGate)
        {
            if (_resolvedRoots.TryGetValue(source, out var cached))
                return cached;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var resolved = source.ResolveRoot(cancellationToken);
        if (!string.IsNullOrWhiteSpace(resolved))
            resolved = Path.GetFullPath(resolved);
        lock (_rootGate)
            _resolvedRoots[source] = resolved;
        return resolved;
    }

    private bool IsStateUnit(string unitId) =>
        _sources.Any(source => IsInSourceNamespace(unitId, source));

    private static string EncodeRelativePath(string path) => string.Join('/', path
        .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
        .Select(Uri.EscapeDataString));

    private static bool TryDecodeRelativePath(string value, out string path)
    {
        path = string.Empty;
        var segments = value.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(segment => segment.Length == 0))
            return false;
        try
        {
            var decoded = segments.Select(Uri.UnescapeDataString).ToArray();
            if (decoded.Any(segment => segment is "." or ".." ||
                    !string.Equals(Path.GetFileName(segment), segment, StringComparison.Ordinal)))
                return false;
            path = Path.Combine(decoded);
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);
    }

    private sealed record FileCandidate(string Path, string RelativePath);
}
