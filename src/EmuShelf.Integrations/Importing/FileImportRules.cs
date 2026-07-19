using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Authoritative file-based import rules for PlayStation, PlayStation 2, PSP,
/// GameCube, and Wii. PS3 remains directory-based and is intentionally absent.
/// </summary>
public sealed class FileImportRules : IGameImportRules
{
    private const string PlayStationId = "playstation";
    private const string PlayStation2Id = "playstation2";
    private const string PspId = "psp";
    private const string GameCubeId = "gamecube";
    private const string WiiId = "wii";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> ExtensionsBySystem =
        new Dictionary<string, HashSet<string>>
        {
            [PlayStationId] = new(StringComparer.OrdinalIgnoreCase)
                { ".cue", ".chd", ".m3u", ".pbp", ".iso" },
            [PlayStation2Id] = new(StringComparer.OrdinalIgnoreCase)
                { ".cue", ".iso", ".chd", ".cso", ".m3u" },
            // PPSSPP's documented desktop load path. A candidate must also contain a valid
            // PSP_GAME/PARAM.SFO, so a generic ISO/CSO is never auto-imported as a PSP game.
            [PspId] = new(StringComparer.OrdinalIgnoreCase) { ".iso", ".cso" },
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
        var pspEvidence = ExtensionsBySystem[PspId].Contains(extension)
            ? PspGameMetadataReader.TryRead(path)
            : null;

        // PSP_GAME/PARAM.SFO is decisive evidence for the otherwise ambiguous ISO/CSO
        // extensions. Put it first so the system picker defaults to PSP, and never let an
        // explicitly confirmed PS1/PS2 import misclassify a validated PSP image.
        if (pspEvidence is not null && FindSystem(PspId) is { } pspSystem)
            suggestions.Add(pspSystem);

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

            var match = system.Id == PspId
                ? pspEvidence is null ? GameFileMatch.Incompatible : GameFileMatch.Compatible
                : MatchSystem(extension, system.Id, detectedNintendoSystem, pspEvidence is not null);

            matches[system.Id] = match;
            if ((match == GameFileMatch.Compatible && system.Id is not (GameCubeId or WiiId)) ||
                match == GameFileMatch.Unrecognized)
            {
                if (!suggestions.Any(candidate => candidate.Id == system.Id))
                    suggestions.Add(system);
            }
        }

        // Raw BIN is accepted only when the user picks it explicitly. Folder scans
        // reject it in IsFolderCandidate so missing CUEs do not create junk entries.
        if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var systemId in new[] { PlayStationId, PlayStation2Id })
            {
                if (FindSystem(systemId) is not { } system)
                    continue;

                matches[systemId] = GameFileMatch.Compatible;
                suggestions.Add(system);
            }
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

        var pspEvidence = ExtensionsBySystem[PspId].Contains(extension)
            ? PspGameMetadataReader.TryRead(path)
            : null;
        if (system.Id == PspId)
            return pspEvidence is not null;

        var detectedNintendoSystem = NintendoExtensions.Contains(extension)
            ? NintendoDiscDetector.Detect(path)
            : NintendoDiscSystem.Unknown;
        return MatchSystem(extension, system.Id, detectedNintendoSystem, pspEvidence is not null) ==
               GameFileMatch.Compatible;
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
                ".cue" => ReferencedFileParser.ParseCue(candidate),
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

    public GameImportMetadata ReadImportMetadata(string path, GameSystem system)
    {
        if (system.Id != PspId || PspGameMetadataReader.TryRead(path) is not { } evidence)
            return GameImportMetadata.Empty;

        IReadOnlyList<GameIdentifier> identifiers = evidence.DiscId is null
            ? []
            : [new GameIdentifier(
                GameIdentifierKind.Serial,
                evidence.DiscId,
                "PSP PARAM.SFO",
                IsPrimary: true)];
        return new GameImportMetadata(evidence.Title, identifiers);
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
        NintendoDiscSystem detectedNintendoSystem,
        bool pspEvidence) =>
        systemId switch
        {
            GameCubeId or WiiId when pspEvidence => GameFileMatch.Incompatible,
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
            PlayStationId or PlayStation2Id
                when pspEvidence &&
                     (extension.Equals(".iso", StringComparison.OrdinalIgnoreCase) ||
                      extension.Equals(".cso", StringComparison.OrdinalIgnoreCase)) =>
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
