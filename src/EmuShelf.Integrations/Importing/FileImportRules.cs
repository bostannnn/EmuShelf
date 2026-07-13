using EmuShelf.Core.Importing;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Authoritative file-based import rules for PlayStation, PlayStation 2,
/// GameCube, and Wii. PS3 remains directory-based and is intentionally absent.
/// </summary>
public sealed class FileImportRules : IGameImportRules
{
    private const string PlayStationId = "playstation";
    private const string PlayStation2Id = "playstation2";
    private const string GameCubeId = "gamecube";
    private const string WiiId = "wii";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> ExtensionsBySystem =
        new Dictionary<string, HashSet<string>>
        {
            [PlayStationId] = new(StringComparer.OrdinalIgnoreCase)
                { ".cue", ".chd", ".m3u", ".pbp", ".iso" },
            [PlayStation2Id] = new(StringComparer.OrdinalIgnoreCase)
                { ".iso", ".chd", ".cso", ".m3u" },
            [GameCubeId] = new(StringComparer.OrdinalIgnoreCase)
                { ".iso", ".rvz", ".wbfs", ".gcm", ".ciso" },
            [WiiId] = new(StringComparer.OrdinalIgnoreCase)
                { ".iso", ".rvz", ".wbfs", ".gcm", ".ciso" },
        };

    private static readonly HashSet<string> NintendoExtensions =
        new(ExtensionsBySystem[GameCubeId], StringComparer.OrdinalIgnoreCase);

    private readonly IReadOnlyList<GameSystem> _systems;

    public FileImportRules() : this(KnownSystems.All)
    {
    }

    public FileImportRules(IReadOnlyList<GameSystem> systems)
    {
        _systems = systems;
    }

    public GameFileAnalysis AnalyzeFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Length == 0)
            return EmptyAnalysis(path);

        var suggestions = new List<GameSystem>();
        var matches = new Dictionary<string, GameFileMatch>();
        var detectedNintendoSystem = NintendoDiscSystem.Unknown;

        // A valid Nintendo header is definitive, so put that match ahead of the
        // extension-only suggestions for shared formats such as .iso.
        if (NintendoExtensions.Contains(extension))
        {
            detectedNintendoSystem = NintendoDiscDetector.Detect(path);
            var detectedId = detectedNintendoSystem switch
            {
                NintendoDiscSystem.GameCube => GameCubeId,
                NintendoDiscSystem.Wii => WiiId,
                _ => null,
            };

            if (detectedId is not null && FindSystem(detectedId) is { } detectedSystem)
                suggestions.Add(detectedSystem);
        }

        foreach (var system in _systems)
        {
            if (!ExtensionsBySystem.TryGetValue(system.Id, out var extensions) ||
                !extensions.Contains(extension))
            {
                continue;
            }

            var match = MatchSystem(extension, system.Id, detectedNintendoSystem);

            matches[system.Id] = match;
            if ((match == GameFileMatch.Compatible && system.Id is not (GameCubeId or WiiId)) ||
                match == GameFileMatch.Unrecognized)
            {
                suggestions.Add(system);
            }
        }

        // Raw BIN is accepted only when the user picks it explicitly. Folder scans
        // reject it in IsFolderCandidate so missing CUEs do not create junk entries.
        if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) &&
            FindSystem(PlayStationId) is { } playStation)
        {
            matches[PlayStationId] = GameFileMatch.Compatible;
            suggestions.Add(playStation);
        }

        return new GameFileAnalysis(path, suggestions, matches);
    }

    public bool IsFolderCandidate(string path, GameSystem system)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase) ||
            !ExtensionsBySystem.TryGetValue(system.Id, out var extensions) ||
            !extensions.Contains(extension))
        {
            return false;
        }

        var detectedNintendoSystem = NintendoExtensions.Contains(extension)
            ? NintendoDiscDetector.Detect(path)
            : NintendoDiscSystem.Unknown;
        return MatchSystem(extension, system.Id, detectedNintendoSystem) == GameFileMatch.Compatible;
    }

    public GameEntrySelection SelectGameEntries(
        IReadOnlyList<string> candidates,
        GameSystem system)
    {
        var distinctCandidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (system.Id is not (PlayStationId or PlayStation2Id))
            return new GameEntrySelection(distinctCandidates, []);

        var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in distinctCandidates)
        {
            var extension = Path.GetExtension(candidate);
            IReadOnlyList<string> references = extension.ToLowerInvariant() switch
            {
                ".m3u" => ReferencedFileParser.ParseM3u(candidate),
                ".cue" when system.Id == PlayStationId => ReferencedFileParser.ParseCue(candidate),
                _ => [],
            };

            foreach (var reference in references)
                referencedPaths.Add(reference);
        }

        var entryPaths = distinctCandidates
            .Where(candidate =>
                Path.GetExtension(candidate).Equals(".m3u", StringComparison.OrdinalIgnoreCase) ||
                !referencedPaths.Contains(NormalizeForComparison(candidate)))
            .ToList();

        // References may name components imported in an earlier batch, so report
        // every resolved path rather than only references present in candidates.
        // Never suppress a retained entry (for example a self-referencing playlist).
        var retainedPaths = entryPaths
            .Select(NormalizeForComparison)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var suppressedPaths = referencedPaths
            .Where(reference => !retainedPaths.Contains(reference))
            .ToList();

        return new GameEntrySelection(entryPaths, suppressedPaths);
    }

    private GameSystem? FindSystem(string id) =>
        _systems.FirstOrDefault(system => system.Id == id);

    private static GameFileMatch MatchNintendoSystem(
        NintendoDiscSystem detected,
        NintendoDiscSystem expected) =>
        detected switch
        {
            NintendoDiscSystem.Unknown => GameFileMatch.Unrecognized,
            _ when detected == expected => GameFileMatch.Compatible,
            _ => GameFileMatch.Incompatible,
        };

    private static GameFileMatch MatchSystem(
        string extension,
        string systemId,
        NintendoDiscSystem detectedNintendoSystem) =>
        systemId switch
        {
            GameCubeId => MatchNintendoSystem(
                detectedNintendoSystem,
                NintendoDiscSystem.GameCube),
            WiiId => MatchNintendoSystem(
                detectedNintendoSystem,
                NintendoDiscSystem.Wii),
            PlayStationId or PlayStation2Id
                when extension.Equals(".iso", StringComparison.OrdinalIgnoreCase) &&
                     detectedNintendoSystem != NintendoDiscSystem.Unknown =>
                GameFileMatch.Incompatible,
            _ => GameFileMatch.Compatible,
        };

    private static GameFileAnalysis EmptyAnalysis(string path) =>
        new(path, [], new Dictionary<string, GameFileMatch>());

    private static string NormalizeForComparison(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
