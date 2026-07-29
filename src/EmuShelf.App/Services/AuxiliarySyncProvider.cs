using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.App.Services;

internal enum AuxiliaryContentKind
{
    Cheats,
    Patches,
    SaveStates,
}

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
    AuxiliaryContentKind Kind,
    string Namespace,
    Func<CancellationToken, string?> ResolveRoot,
    Func<string, bool> Include,
    bool Recursive = true,
    Func<string, string>? StateGroup = null);

internal sealed record AuxiliaryContentLocation(
    AuxiliaryContentKind Kind,
    string? Directory,
    int EligibleFileCount,
    int TotalFileCount,
    long EligibleBytes,
    string? Compatibility = null,
    string? Warning = null);

/// <summary>
/// Adds allow-listed, per-file optional content to an established save provider. Optional
/// namespaces do not change existing save ids; state compatibility is an optional field in the
/// existing cloud index entries.
/// </summary>
internal sealed class AuxiliarySyncProvider : ISaveLocationProvider
{
    private readonly ISaveLocationProvider _saves;
    private readonly IReadOnlyList<AuxiliaryFileSource> _sources;
    private readonly StateCompatibility? _compatibility;
    private readonly int _stateRetention;
    private readonly Dictionary<AuxiliaryFileSource, string?> _resolvedRoots = [];
    private readonly object _rootGate = new();

    public AuxiliarySyncProvider(
        ISaveLocationProvider saves,
        IReadOnlyList<AuxiliaryFileSource> sources,
        StateCompatibility? compatibility,
        int stateRetention)
    {
        _saves = saves;
        _sources = sources;
        _compatibility = compatibility;
        _stateRetention = Math.Clamp(stateRetention, 1, 10);
    }

    public string SystemId => _saves.SystemId;

    public string UnitIdPrefix => _saves.UnitIdPrefix;

    public bool HasStateSource => _sources.Any(source => source.Kind == AuxiliaryContentKind.SaveStates);

    public bool HasStateCompatibility => _compatibility is not null;

    /// <summary>
    /// Resolves each optional root independently for Settings. An optional source is advisory: a
    /// missing or unreadable cheats folder must never hide an otherwise valid save-card location.
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
        IsStateUnit(unitId) ? _compatibility?.Key : _saves.GetCompatibility(unitId);

    public string? GetRemoteIncompatibilityReason(SaveUnitSnapshot remoteSnapshot)
    {
        if (!IsStateUnit(remoteSnapshot.UnitId))
            return _saves.GetRemoteIncompatibilityReason(remoteSnapshot);
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
        return _saves.OwnsUnit(unitId);
    }

    public IReadOnlyList<SaveUnitSnapshot> SelectRemoteUnits(IReadOnlyList<SaveUnitSnapshot> snapshots)
    {
        var selected = _saves.SelectRemoteUnits(snapshots
            .Where(snapshot => !TryGetOptionalNamespace(snapshot.UnitId, out _))
            .ToArray()).ToList();

        foreach (var source in _sources)
        {
            var owned = snapshots.Where(snapshot => IsInSourceNamespace(snapshot.UnitId, source)).ToArray();
            if (source.Kind != AuxiliaryContentKind.SaveStates)
            {
                selected.AddRange(owned);
                continue;
            }

            var localByGroup = GetLocalStateCandidates(source);
            foreach (var group in owned.GroupBy(
                         snapshot => StateGroupFromUnitId(snapshot.UnitId, source),
                         StringComparer.OrdinalIgnoreCase))
            {
                var candidates = group
                    .Select(snapshot => new RetentionCandidate(snapshot.UnitId, snapshot, snapshot.ModifiedUtc))
                    .ToDictionary(candidate => candidate.UnitId, StringComparer.Ordinal);
                if (localByGroup.TryGetValue(group.Key, out var localCandidates))
                {
                    foreach (var local in localCandidates)
                    {
                        if (candidates.TryGetValue(local.UnitId, out var remoteCandidate))
                        {
                            candidates[local.UnitId] = remoteCandidate with
                            {
                                ModifiedUtc = remoteCandidate.ModifiedUtc >= local.ModifiedUtc
                                    ? remoteCandidate.ModifiedUtc
                                    : local.ModifiedUtc,
                            };
                        }
                        else
                        {
                            candidates[local.UnitId] = local;
                        }
                    }
                }
                selected.AddRange(candidates
                    .Values
                    .OrderByDescending(candidate => candidate.ModifiedUtc)
                    .Take(_stateRetention)
                    .Where(candidate => candidate.Remote is not null)
                    .Select(candidate => candidate.Remote!));
            }
        }

        return selected.DistinctBy(snapshot => snapshot.UnitId, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default)
    {
        var units = (await _saves.GetSaveUnitsAsync(cancellationToken)).ToList();
        foreach (var source in _sources)
            units.AddRange(await Task.Run(() => Enumerate(source, cancellationToken), cancellationToken));
        return units;
    }

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (!TryGetOptionalNamespace(unitId, out _))
            return _saves.ResolveUnit(unitId);

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
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return [];
        if (source.Kind == AuxiliaryContentKind.SaveStates && _compatibility is null)
            return [];

        var fullRoot = Path.GetFullPath(root);
        var files = EnumerateCandidates(source, fullRoot, cancellationToken);

        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .Select(file => new SaveUnit(
                Prefix(source) +
                EncodeRelativePath(file.RelativePath),
                $"{Path.GetFileName(file.Path)} — {KindName(source.Kind)}",
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
                    source.Kind,
                    null,
                    0,
                    0,
                    0,
                    Warning: "The emulator configuration does not expose a safe folder for this content.");
            }

            var root = Path.GetFullPath(resolved);
            if (!Directory.Exists(root))
            {
                return new AuxiliaryContentLocation(
                    source.Kind,
                    root,
                    0,
                    0,
                    0,
                    Warning: "The folder does not exist yet.");
            }

            var allFiles = EnumerateCandidates(source, root, cancellationToken, applyStateRetention: false);
            var files = ApplyStateRetention(source, allFiles);
            var bytes = files.Aggregate(0L, (total, file) => checked(total + new FileInfo(file.Path).Length));
            var compatibility = source.Kind == AuxiliaryContentKind.SaveStates ? _compatibility?.Description : null;
            var warning = source.Kind == AuxiliaryContentKind.SaveStates && _compatibility is null
                ? "The emulator/core version could not be detected, so states will not be synced."
                : null;
            return new AuxiliaryContentLocation(
                source.Kind,
                root,
                files.Length,
                allFiles.Length,
                bytes,
                compatibility,
                warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return new AuxiliaryContentLocation(source.Kind, null, 0, 0, 0, Warning: ex.Message);
        }
    }

    private FileCandidate[] EnumerateCandidates(
        AuxiliaryFileSource source,
        string fullRoot,
        CancellationToken cancellationToken,
        bool applyStateRetention = true)
    {
        var search = source.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(fullRoot, "*", search)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return path;
            })
            .Where(source.Include)
            .Select(path => new FileCandidate(path, Path.GetRelativePath(fullRoot, path), File.GetLastWriteTimeUtc(path)))
            .ToArray();
        return applyStateRetention ? ApplyStateRetention(source, files) : files;
    }

    private FileCandidate[] ApplyStateRetention(AuxiliaryFileSource source, FileCandidate[] files)
    {
        if (source.Kind != AuxiliaryContentKind.SaveStates)
            return files;
        return files
            .GroupBy(file => (source.StateGroup ?? DefaultStateGroup)(file.RelativePath), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.OrderByDescending(file => file.ModifiedUtc).Take(_stateRetention))
            .ToArray();
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
        return value is "cheats" or "patches" or "states";
    }

    private string StateGroupFromUnitId(string unitId, AuxiliaryFileSource source)
    {
        var remainder = unitId[Prefix(source).Length..];
        if (!TryDecodeRelativePath(remainder, out var relative))
            return unitId;
        return (source.StateGroup ?? DefaultStateGroup)(relative);
    }

    private IReadOnlyDictionary<string, IReadOnlyList<RetentionCandidate>> GetLocalStateCandidates(
        AuxiliaryFileSource source)
    {
        if (_compatibility is null)
            return new Dictionary<string, IReadOnlyList<RetentionCandidate>>(StringComparer.OrdinalIgnoreCase);
        var root = ResolveRoot(source, CancellationToken.None);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return new Dictionary<string, IReadOnlyList<RetentionCandidate>>(StringComparer.OrdinalIgnoreCase);
        var search = source.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(root, "*", search)
            .Where(source.Include)
            .GroupBy(
                path => (source.StateGroup ?? DefaultStateGroup)(Path.GetRelativePath(root, path)),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<RetentionCandidate>)group
                    .Select(path => new RetentionCandidate(
                        Prefix(source) + EncodeRelativePath(Path.GetRelativePath(root, path)),
                        null,
                        File.GetLastWriteTimeUtc(path)))
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    internal static string DefaultStateGroup(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        var state = name.IndexOf(".state", StringComparison.OrdinalIgnoreCase);
        if (state > 0)
            return name[..state];
        var withoutExtension = Path.GetFileNameWithoutExtension(name);
        var slotSeparator = withoutExtension.LastIndexOfAny(['_', '-', '.']);
        if (slotSeparator > 0 && withoutExtension[(slotSeparator + 1)..].All(char.IsAsciiDigit))
            withoutExtension = withoutExtension[..slotSeparator];
        return withoutExtension;
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

    private bool IsStateUnit(string unitId) => _sources.Any(source =>
        source.Kind == AuxiliaryContentKind.SaveStates && IsInSourceNamespace(unitId, source));

    private static string KindName(AuxiliaryContentKind kind) => kind switch
    {
        AuxiliaryContentKind.Cheats => "cheat",
        AuxiliaryContentKind.Patches => "patch",
        _ => "save state",
    };

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

    private sealed record FileCandidate(string Path, string RelativePath, DateTimeOffset ModifiedUtc);

    private sealed record RetentionCandidate(
        string UnitId,
        SaveUnitSnapshot? Remote,
        DateTimeOffset ModifiedUtc);
}
