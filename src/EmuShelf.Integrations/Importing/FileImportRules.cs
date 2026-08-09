using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Authoritative file-based import rules for PlayStation, PlayStation 2, PSP,
/// Mega Drive / Genesis, Nintendo DS, Game Boy Advance, Game Boy Color, NES, Super Nintendo,
/// Dreamcast, GameCube, Wii, and Arcade. PS3 remains
/// directory-based and is intentionally absent.
/// </summary>
public sealed class FileImportRules : IGameImportRules
{
    private const string PlayStationId = "playstation";
    private const string PlayStation2Id = "playstation2";
    private const string PspId = "psp";
    private const string MegaDriveId = "megadrive";
    private const string NintendoDsId = "nds";
    private const string GameBoyAdvanceId = "gba";
    private const string SuperNintendoId = "snes";
    private const string DreamcastId = "dreamcast";
    private const string GameCubeId = "gamecube";
    private const string WiiId = "wii";
    private const string ArcadeId = "arcade";
    private const string GameBoyColorId = "gbc";
    private const string NesId = "nes";
    private const string ThreeDsId = "3ds";

    private static readonly IReadOnlyDictionary<string, HashSet<string>> ExtensionsBySystem =
        new Dictionary<string, HashSet<string>>
        {
            [PlayStationId] = new(StringComparer.OrdinalIgnoreCase)
                { ".cue", ".chd", ".m3u", ".pbp", ".iso" },
            [PlayStation2Id] = new(StringComparer.OrdinalIgnoreCase)
                { ".cue", ".iso", ".chd", ".cso", ".m3u" },
            // PPSSPP's documented desktop load path (CHD since PPSSPP 1.15). A candidate must
            // also contain a valid PSP_GAME/PARAM.SFO, so a generic ISO/CSO/CHD is never
            // auto-imported as a PSP game.
            [PspId] = new(StringComparer.OrdinalIgnoreCase) { ".iso", ".cso", ".chd" },
            // The extension is only a routing hint: the reader requires the Sega header and,
            // for .smd, the canonical 512-byte copier-header/interleaved layout.
            [MegaDriveId] = new(StringComparer.OrdinalIgnoreCase) { ".md", ".gen", ".bin", ".smd" },
            // These are raw, header-validated cartridge images only. Archives and converted
            // layouts need their own read-only normalization contracts before they can join.
            [NintendoDsId] = new(StringComparer.OrdinalIgnoreCase) { ".nds" },
            [GameBoyAdvanceId] = new(StringComparer.OrdinalIgnoreCase) { ".gba" },
            // Every container Azahar can load. The extension is a routing hint only: the reader
            // validates each family's magic/structure (NCSD/NCCH/CIA/3DSX/ELF/seekable-Zstandard),
            // so a renamed arbitrary file is never imported as a 3DS game.
            [ThreeDsId] = new(Nintendo3dsRomReader.SupportedExtensions, StringComparer.OrdinalIgnoreCase),
            // The extension is a routing hint only: the reader requires the Game Boy boot logo, a
            // valid header checksum, and the CGB flag, so an original Game Boy ROM is never accepted.
            [GameBoyColorId] = new(StringComparer.OrdinalIgnoreCase) { ".gbc", ".gb" },
            // The extension is a routing hint only: the reader requires the iNES "NES\x1A" magic and a
            // length that fits the PRG/CHR banks the header declares, so a renamed file is rejected.
            [NesId] = new(StringComparer.OrdinalIgnoreCase) { ".nes" },
            // The extension is a routing hint only: the reader requires a valid internal LoROM or
            // HiROM header. Copier formats (.fig/.swc) wait for their own normalization contract.
            [SuperNintendoId] = new(StringComparer.OrdinalIgnoreCase) { ".sfc", ".smc" },
            // The extension is a routing hint only. A GDI descriptor must name every track and a
            // CHD must declare its own track layout; either way the image is accepted only when a
            // data track really starts with IP.BIN, so a .chd shared with the PlayStation systems
            // is never filename-guessed onto Dreamcast. CDI still waits for its own reader.
            [DreamcastId] = new(StringComparer.OrdinalIgnoreCase) { ".gdi", ".chd" },
            [GameCubeId] = new(StringComparer.OrdinalIgnoreCase)
                { ".iso", ".rvz", ".wbfs", ".gcm", ".ciso" },
            // Wii shares the disc containers with GameCube and adds .wad for installable titles
            // (WiiWare, Virtual Console, channels). A .wad has no disc header, so it is recognized by
            // WiiWadReader's own header/section validation rather than the shared disc-magic detector,
            // and no other system claims the extension — so it never routes to GameCube.
            [WiiId] = new(StringComparer.OrdinalIgnoreCase)
                { ".iso", ".rvz", ".wbfs", ".gcm", ".ciso", ".wad" },
            // FinalBurn Neo loads a romset from a .zip named by the set's short id. No other system
            // claims .zip, so it routes straight to Arcade; the archive itself is never opened at
            // import — the zip basename is the identity, resolved to a title later from the DAT.
            // .7z is deliberately omitted: the framework has no 7z reader and v1 does not add one.
            [ArcadeId] = new(StringComparer.OrdinalIgnoreCase) { ".zip" },
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
        var megaDriveLayout = ExtensionsBySystem[MegaDriveId].Contains(extension)
            ? MegaDriveRomReader.TryRecognize(path)
            : null;
        var nintendoDsHeader = ExtensionsBySystem[NintendoDsId].Contains(extension)
            ? NintendoDsRomReader.TryRecognize(path)
            : null;
        var gameBoyAdvanceHeader = ExtensionsBySystem[GameBoyAdvanceId].Contains(extension)
            ? GameBoyAdvanceRomReader.TryRecognize(path)
            : null;
        var nintendo3dsRecognition = ExtensionsBySystem[ThreeDsId].Contains(extension)
            ? Nintendo3dsRomReader.TryRecognize(path)
            : null;
        var gameBoyColorHeader = ExtensionsBySystem[GameBoyColorId].Contains(extension)
            ? GameBoyColorRomReader.TryRecognize(path)
            : null;
        var nesHeader = ExtensionsBySystem[NesId].Contains(extension)
            ? NesRomReader.TryRecognize(path)
            : null;
        var superNintendoHeader = ExtensionsBySystem[SuperNintendoId].Contains(extension)
            ? SuperNintendoRomReader.TryRecognize(path)
            : null;
        // A .wad is a Wii installable title (WiiWare/VC/channel), validated by its own header and
        // section layout rather than the disc-magic detector below.
        var isWad = extension.Equals(".wad", StringComparison.OrdinalIgnoreCase);
        var wiiWadRecognized = isWad && WiiWadReader.TryRecognize(path);
        // A validated PSP image is never a Dreamcast one, so the shared .chd extension only pays
        // for the IP.BIN probe when the PSP evidence came back empty.
        var dreamcastImage = ExtensionsBySystem[DreamcastId].Contains(extension) &&
                             pspEvidence is null &&
                             DreamcastDisc.TryRecognize(path);

        // PSP_GAME/PARAM.SFO is decisive evidence for the otherwise ambiguous ISO/CSO/CHD
        // extensions. Put it first so the system picker defaults to PSP, and never let an
        // explicitly confirmed PS1/PS2 import misclassify a validated PSP image.
        if (pspEvidence is not null && FindSystem(PspId) is { } pspSystem)
            suggestions.Add(pspSystem);

        // A validated IP.BIN is decisive for the otherwise ambiguous .chd extension in the same
        // way PARAM.SFO is for PSP, so the system picker defaults to Dreamcast and MatchSystem
        // vetoes the PlayStation systems that share the container.
        if (dreamcastImage && FindSystem(DreamcastId) is { } dreamcastSystem)
            suggestions.Add(dreamcastSystem);

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

        // A recognized WAD carries no disc header, so the detection above never fires for it. Add Wii
        // here so the system picker defaults to it, mirroring the disc-detection suggestion.
        if (wiiWadRecognized && FindSystem(WiiId) is { } wiiWadSystem)
            suggestions.Add(wiiWadSystem);

        foreach (var system in _systems)
        {
            if (!ExtensionsBySystem.TryGetValue(system.Id, out var extensions) ||
                !extensions.Contains(extension))
            {
                continue;
            }

            var match = system.Id switch
            {
                PspId => pspEvidence is null ? GameFileMatch.Incompatible : GameFileMatch.Compatible,
                MegaDriveId => megaDriveLayout is null
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                NintendoDsId => nintendoDsHeader is null
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                GameBoyAdvanceId => gameBoyAdvanceHeader is null
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                ThreeDsId => nintendo3dsRecognition is null
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                GameBoyColorId => gameBoyColorHeader is null
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                NesId => nesHeader is null
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                SuperNintendoId => superNintendoHeader is null
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                DreamcastId => dreamcastImage
                    ? GameFileMatch.Compatible
                    : GameFileMatch.Incompatible,
                // A .zip is an arcade set unless its basename is a known BIOS/device archive, which
                // is hidden so neogeo.zip and friends never become a game.
                ArcadeId => IsArcadeBiosArchive(path)
                    ? GameFileMatch.Incompatible
                    : GameFileMatch.Compatible,
                // A .wad only ever matches Wii, and only when WiiWadReader validates its structure.
                WiiId when isWad =>
                    wiiWadRecognized ? GameFileMatch.Compatible : GameFileMatch.Incompatible,
                _ => MatchSystem(
                    extension,
                    system.Id,
                    detectedNintendoSystem,
                    pspEvidence is not null,
                    dreamcastImage),
            };

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

                // A validated cartridge header is decisive evidence for the otherwise generic
                // .bin extension. Preserve the explicit PS raw-BIN fallback only when the file
                // did not prove itself to be a Mega Drive ROM.
                matches[systemId] = megaDriveLayout is null
                    ? GameFileMatch.Compatible
                    : GameFileMatch.Incompatible;
                if (megaDriveLayout is null)
                    suggestions.Add(system);
            }
        }

        return new GameFileAnalysis(path, suggestions, matches);
    }

    public bool IsFolderCandidate(string path, GameSystem system)
    {
        var extension = Path.GetExtension(path);
        if (system.Id == MegaDriveId)
            return MegaDriveRomReader.TryRecognize(path) is not null;
        if (system.Id == NintendoDsId)
            return NintendoDsRomReader.TryRecognize(path) is not null;
        if (system.Id == GameBoyAdvanceId)
            return GameBoyAdvanceRomReader.TryRecognize(path) is not null;
        if (system.Id == ThreeDsId)
            return Nintendo3dsRomReader.TryRecognize(path) is not null;
        if (system.Id == GameBoyColorId)
            return GameBoyColorRomReader.TryRecognize(path) is not null;
        if (system.Id == NesId)
            return NesRomReader.TryRecognize(path) is not null;
        if (system.Id == SuperNintendoId)
            return SuperNintendoRomReader.TryRecognize(path) is not null;
        if (system.Id == DreamcastId)
            return DreamcastDisc.TryRecognize(path);
        if (system.Id == ArcadeId)
            return ExtensionsBySystem[ArcadeId].Contains(extension) && !IsArcadeBiosArchive(path);
        // A .wad routes only to Wii, and only when its WAD structure validates. Wii's disc
        // extensions still fall through to the shared disc-magic path below.
        if (system.Id == WiiId && extension.Equals(".wad", StringComparison.OrdinalIgnoreCase))
            return WiiWadReader.TryRecognize(path);

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
        // A Dreamcast CHD is not a PlayStation one. The IP.BIN probe runs only for the container
        // the two share, and only once the PSP evidence has already come back empty.
        var dreamcastImage = system.Id is PlayStationId or PlayStation2Id &&
                             pspEvidence is null &&
                             ExtensionsBySystem[DreamcastId].Contains(extension) &&
                             DreamcastDisc.TryRecognize(path);
        return MatchSystem(
                   extension,
                   system.Id,
                   detectedNintendoSystem,
                   pspEvidence is not null,
                   dreamcastImage) ==
               GameFileMatch.Compatible;
    }

    public GameEntrySelection SelectGameEntries(
        IReadOnlyList<string> candidates,
        GameSystem system)
    {
        var distinctCandidates = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (system.Id is not (PlayStationId or PlayStation2Id or DreamcastId))
            return new GameEntrySelection(distinctCandidates, []);

        var referencedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in distinctCandidates)
        {
            var extension = Path.GetExtension(candidate);
            IReadOnlyList<string> references = extension.ToLowerInvariant() switch
            {
                ".m3u" => ReferencedFileParser.ParseM3u(candidate),
                ".cue" => ReferencedFileParser.ParseCue(candidate),
                ".gdi" => DreamcastDisc.GetReferencedFiles(candidate),
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
        if (system.Id == MegaDriveId && MegaDriveRomReader.TryRead(path) is { } megaDriveEvidence)
        {
            return new GameImportMetadata(
                null,
                [new GameIdentifier(
                    GameIdentifierKind.Sha1,
                    megaDriveEvidence.Sha1,
                    "Mega Drive normalized ROM",
                    IsPrimary: true)]);
        }

        if (system.Id == NintendoDsId && NintendoDsRomReader.TryRead(path) is { } nintendoDsEvidence)
            return CreateCartridgeMetadata(
                nintendoDsEvidence.GameCode,
                "Nintendo DS header",
                nintendoDsEvidence.Sha1,
                "Nintendo DS ROM");

        if (system.Id == GameBoyAdvanceId && GameBoyAdvanceRomReader.TryRead(path) is { } gameBoyAdvanceEvidence)
            return CreateCartridgeMetadata(
                gameBoyAdvanceEvidence.GameCode,
                "Game Boy Advance header",
                gameBoyAdvanceEvidence.Sha1,
                "Game Boy Advance ROM");

        // 3DS carries no cheap whole-file hash (dumps are multi-gigabyte). Uncompressed NCSD/NCCH
        // dumps supply a plaintext product code and title id; compressed, CIA, and homebrew files
        // are recognized but yield no header identity, so they fall back to the filename for covers.
        if (system.Id == ThreeDsId && Nintendo3dsRomReader.TryRead(path) is { } nintendo3dsEvidence)
            return Create3dsMetadata(nintendo3dsEvidence);

        // The Game Boy Color header has no reliable commercial game code, so only the raw SHA-1 is
        // catalogue evidence — the same shape as Super Nintendo.
        if (system.Id == GameBoyColorId && GameBoyColorRomReader.TryRead(path) is { } gameBoyColorEvidence)
            return CreateCartridgeMetadata(
                null,
                "Game Boy Color header",
                gameBoyColorEvidence.Sha1,
                "Game Boy Color ROM");

        // The iNES header has no reliable commercial game code, so only the SHA-1 is catalogue
        // evidence. Unlike SNES this keeps the 16-byte header, because the No-Intro NES set is keyed
        // by the whole headered file; RetroAchievements strips the header in its own hasher instead.
        if (system.Id == NesId && NesRomReader.TryRead(path) is { } nesEvidence)
            return CreateCartridgeMetadata(
                null,
                "NES header",
                nesEvidence.Sha1,
                "NES ROM");

        // The SNES header has no reliable commercial game code, so only the headerless SHA-1 is
        // used as catalogue evidence; the Shift-JIS header title stays out of the display fields.
        if (system.Id == SuperNintendoId && SuperNintendoRomReader.TryRead(path) is { } superNintendoEvidence)
            return CreateCartridgeMetadata(
                null,
                "Super Nintendo header",
                superNintendoEvidence.Sha1,
                "Super Nintendo ROM");

        // The arcade set id is the zip basename — no file is opened. Storing it now lets metadata
        // enrichment resolve the title from the DAT without re-deriving the identifier.
        if (system.Id == ArcadeId)
        {
            var setName = Path.GetFileNameWithoutExtension(path);
            return string.IsNullOrWhiteSpace(setName)
                ? GameImportMetadata.Empty
                : new GameImportMetadata(
                    null,
                    [new GameIdentifier(
                        GameIdentifierKind.ArcadeSetName,
                        setName,
                        "FBNeo set name",
                        IsPrimary: true)]);
        }

        // Dreamcast deliberately supplies no import-time evidence. A GDI set's catalogue key is
        // the SHA-1 of a whole data track — up to 1.1 GB per game, and a set can have more than
        // one — whereas every other system's import evidence is a header read or a cartridge-sized
        // ROM. DreamcastIdentifierExtractor computes it once during opt-in metadata enrichment,
        // which is already gated and reports progress, so adding a folder stays as cheap as
        // AnalyzeFile. A CHD is keyed on its IP.BIN serial and would be cheap to read here, but it
        // takes the same path so that both packagings are identified in exactly one place.
        if (system.Id == DreamcastId)
            return GameImportMetadata.Empty;

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

    private static GameImportMetadata CreateCartridgeMetadata(
        string? gameCode,
        string gameCodeSource,
        string sha1,
        string sha1Source)
    {
        var identifiers = new List<GameIdentifier>();
        if (gameCode is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.TitleId,
                gameCode,
                gameCodeSource));
        }
        identifiers.Add(new GameIdentifier(
            GameIdentifierKind.Sha1,
            sha1,
            sha1Source,
            IsPrimary: true));
        return new GameImportMetadata(null, identifiers);
    }

    private static GameImportMetadata Create3dsMetadata(Nintendo3dsEvidence evidence)
    {
        var identifiers = new List<GameIdentifier>();
        // The NCCH product code (for example "CTR-P-AQNE") is the exact GameTDB cover key, so it is
        // the primary evidence; the title id is retained as secondary evidence. This mirrors the
        // identifiers produced by Nintendo3dsRomIdentifierExtractor during metadata enrichment.
        if (evidence.ProductCode is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.Serial,
                evidence.ProductCode,
                "Nintendo 3DS NCCH product code",
                IsPrimary: true));
        }
        if (evidence.TitleId is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.TitleId,
                evidence.TitleId,
                "Nintendo 3DS title id"));
        }
        return identifiers.Count == 0
            ? GameImportMetadata.Empty
            : new GameImportMetadata(null, identifiers);
    }

    private GameSystem? FindSystem(string id) =>
        _systems.FirstOrDefault(system => system.Id == id);

    private static bool IsArcadeBiosArchive(string path) =>
        KnownArcadeBiosSets.Contains(Path.GetFileNameWithoutExtension(path));

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
        bool pspEvidence,
        bool dreamcastEvidence) =>
        systemId switch
        {
            GameCubeId or WiiId when pspEvidence => GameFileMatch.Incompatible,
            // Dreamcast and the PlayStation systems share .chd, and only one of them can have
            // written an IP.BIN header into the image's data track.
            PlayStationId or PlayStation2Id when dreamcastEvidence => GameFileMatch.Incompatible,
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
            // Every PSP container extension is also a PlayStation one, so validated PSP evidence
            // has to veto the PS1/PS2 match for all of them, not just the uncompressed ISO.
            PlayStationId or PlayStation2Id
                when pspEvidence && ExtensionsBySystem[PspId].Contains(extension) =>
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
