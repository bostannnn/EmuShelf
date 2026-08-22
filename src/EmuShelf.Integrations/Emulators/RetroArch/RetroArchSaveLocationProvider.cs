using System.Text;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.RetroArch;

/// <summary>
/// The configured libretro core, identified by its file name and by the short core name RetroArch
/// uses as the folder name when "sort saves into folders by core name" is on.
/// </summary>
/// <param name="FileName">The core file name as configured, for messages.</param>
/// <param name="Name">
/// The core's short name — libretro's <c>corename</c>, e.g. <c>melonDS DS</c> — or null when it is
/// not known. This is the folder name RetroArch sorts into, and it is deliberately not the info
/// file's <c>display_name</c> ("Nintendo - DS (melonDS DS)"), which names the system, not the folder.
/// </param>
public sealed record RetroArchCore(string FileName, string? Name)
{
    /// <summary>
    /// Core names for the cores EmuShelf ships knowledge of. This is only a fallback: the
    /// authoritative name is <c>corename</c> in RetroArch's own <c>info/</c> entry for the installed
    /// core, which covers every core rather than a fixed list.
    /// </summary>
    private static readonly Dictionary<string, string> KnownCoreNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["genesis_plus_gx"] = "Genesis Plus GX",
        ["genesis_plus_gx_wide"] = "Genesis Plus GX Wide",
        ["picodrive"] = "PicoDrive",
        ["snes9x"] = "Snes9x",
        ["snes9x2010"] = "Snes9x 2010",
        // bsnes and its variants are popular SNES cores that were missing from this fallback table,
        // so on installs where the info file below cannot be read (a Flatpak RetroArch, a bare core)
        // their save-state/save folder could not be named. The info entry's corename remains
        // authoritative and is read first; these only stand in when it is unavailable.
        ["bsnes"] = "bsnes",
        ["bsnes_mercury_accuracy"] = "bsnes-mercury Accuracy",
        ["bsnes_mercury_balanced"] = "bsnes-mercury Balanced",
        ["bsnes_mercury_performance"] = "bsnes-mercury Performance",
        ["bsnes_hd_beta"] = "bsnes-hd beta",
        ["mgba"] = "mGBA",
        ["vbam"] = "VBA-M",
        ["melondsds"] = "melonDS DS",
        ["melonds"] = "melonDS",
        ["desmume"] = "DeSmuME",
        ["flycast"] = "Flycast",
        ["fbneo"] = "FinalBurn Neo",
        ["gambatte"] = "Gambatte",
        ["sameboy"] = "SameBoy",
        ["mesen"] = "Mesen",
        ["fceumm"] = "FCEUmm",
        ["nestopia"] = "Nestopia UE",
        ["quicknes"] = "QuickNES",
        // PlayStation cores, so RetroArch save-folder-by-core resolves without the core's info file.
        ["mednafen_psx"] = "Beetle PSX",
        ["mednafen_psx_hw"] = "Beetle PSX HW",
        ["swanstation"] = "SwanStation",
        ["pcsx_rearmed"] = "PCSX-ReARMed",
    };

    /// <summary>Identifies the configured core, reading RetroArch's info entry when it is present.</summary>
    /// <param name="infoDirectories">
    /// The <c>info</c> folders to search for the core's info entry, in precedence order. RetroArch
    /// keeps one per install location (portable, Flatpak, Linux, macOS), so the caller supplies every
    /// candidate rather than a single directory — a Flatpak install's entry lives nowhere near the
    /// portable folder, and missing it leaves cores outside <see cref="KnownCoreNames"/> unnamed.
    /// </param>
    public static RetroArchCore? ForCorePath(string? corePath, IReadOnlyList<string> infoDirectories)
    {
        if (string.IsNullOrWhiteSpace(corePath))
            return null;

        var fileName = Path.GetFileName(corePath.Trim());
        var coreId = Path.GetFileNameWithoutExtension(fileName);
        // RetroArch's Android cores are "<core>_libretro_android.so"; the "_android" build tag is not
        // part of the core identity (desktop is "<core>_libretro.so/.dll"), so drop it before the
        // shared "_libretro" strip. Without this the id stays "mgba_libretro_android", matches neither
        // an info entry ("mgba_libretro.info") nor KnownCoreNames, leaving the core unnamed — and a
        // sorted-by-core save folder that cannot be named syncs nothing. See docs/android-save-sync-model.md.
        const string androidTag = "_android";
        if (coreId.EndsWith(androidTag, StringComparison.OrdinalIgnoreCase))
            coreId = coreId[..^androidTag.Length];
        const string suffix = "_libretro";
        if (coreId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            coreId = coreId[..^suffix.Length];

        return new RetroArchCore(
            fileName,
            ReadCoreName(coreId, infoDirectories) ?? KnownCoreNames.GetValueOrDefault(coreId));
    }

    // RetroArch ships one info file per core, keyed by the same file name, whose corename is
    // exactly the folder name it sorts saves into. The first candidate directory that holds the
    // core's entry wins, so a portable info folder takes precedence over the user-profile one.
    private static string? ReadCoreName(string coreId, IReadOnlyList<string> infoDirectories)
    {
        foreach (var directory in infoDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            if (ReadCoreNameFrom(Path.Combine(directory, coreId + "_libretro.info")) is { } name)
                return name;
        }

        return null;
    }

    private static string? ReadCoreNameFrom(string infoPath)
    {
        try
        {
            if (!File.Exists(infoPath))
                return null;

            foreach (var rawLine in File.ReadLines(infoPath))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith("corename", StringComparison.Ordinal))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                var value = line[(separator + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>The resolved save directory for one system, and how RetroArch arrived at it.</summary>
/// <param name="SaveDirectory">The effective battery-save directory for this system's core.</param>
/// <param name="Core">The verified core the directory was resolved for.</param>
/// <param name="SortedByCore">Whether RetroArch keeps this core's saves in its own subfolder.</param>
/// <param name="IsExclusive">
/// Whether every save in the directory belongs to this system. When false the folder is shared with
/// RetroArch's other cores, and only saves named after this system's library entries are claimed.
/// </param>
/// <param name="HasUnreadPerGameOverride">
/// Whether RetroArch holds a content- or game-specific override that moves save files somewhere
/// else. Those apply to one game at a time, which EmuShelf cannot enumerate, so it reports them
/// instead of quietly resolving the wrong folder for that game.
/// </param>
public sealed record RetroArchSaveInfo(
    string SaveDirectory,
    RetroArchCore Core,
    bool SortedByCore,
    bool IsExclusive,
    bool HasUnreadPerGameOverride = false);

/// <summary>Core-scoped RetroArch roots that are safe to sync without touching ROM folders.</summary>
public sealed record RetroArchContentDirectories(string? Cheats, string? SaveStates);

/// <summary>
/// Resolves the effective battery-save directory for one system by reading RetroArch's own
/// <c>retroarch.cfg</c> and the configured core's override, then exposes each of that core's save
/// files as a unit. Save states, system files, configuration, and every other core's saves are out
/// of scope, and a layout EmuShelf cannot resolve exactly fails closed rather than guessing.
/// </summary>
public sealed class RetroArchSaveLocationProvider : ISaveLocationProvider
{
    private const string ConfigFileName = "retroarch.cfg";

    // PlayStation battery saves cross emulator: DuckStation and a RetroArch PS1 core (Beetle PSX) both
    // write the same raw 128 KB memory card, so for PlayStation this provider mirrors DuckStation's
    // "file-title" per-game card cloud key — playstation/per-game/file-title/<name>_1.mcd — instead of
    // the bare "<system>/<file>.srm" every other system uses. Both emulators then key one game to one
    // cloud entry and a card round-trips desktop DuckStation ↔ Android Beetle. Requires DuckStation
    // "Separate Card Per Game (File Title)" and matching ROM file names on each machine (a setup-
    // checklist item, not a converter — the payload is identical). See docs/android-save-sync-model.md.
    private const string PlayStationSystemId = "playstation";
    private const string PlayStationCardKeyPrefix = "per-game/file-title/";
    private const string PlayStationCardKeySuffix = "_1.mcd";
    // The extension a RetroArch PS1 core writes its card with; a fresh restore lands here so the core
    // picks the card up. An existing card is read under whatever name it already has (see the probe).
    private const string PlayStationCardExtension = ".srm";
    private static readonly string[] PlayStationCardExtensions = [".srm", ".mcr", ".mcd", ".bin", ".ps"];

    // RetroArch's own artifacts in a save folder: save states (Game.state, .state1, .state.auto),
    // input replays, screenshots, and configuration. Everything else named after a game is that
    // game's data, whichever core wrote it.
    private static readonly string[] ExcludedExtensions =
        [".auto", ".bsv", ".png", ".jpg", ".jpeg", ".cfg", ".opt", ".rmp", ".cht", ".txt", ".log", ".info", ".tmp"];

    private readonly string _systemId;
    private readonly RetroArchCore? _core;
    private readonly Func<IReadOnlyCollection<string>>? _gameFileNames;
    private readonly string _installationDirectory;
    private readonly string? _directoryOverride;
    private readonly string _homeDirectory;
    private readonly string? _xdgConfigHome;
    private readonly bool _isWindows;
    private readonly bool _isMacOS;
    private readonly bool _isFlatpak;
    private readonly bool _coreSharedAcrossSystems;
    private HashSet<string>? _knownGameFileNames;

    /// <param name="coreSharedAcrossSystems">
    /// True when the same libretro core is configured for more than one of EmuShelf's systems (e.g.
    /// mGBA for both Game Boy Advance and Game Boy Color). Such systems resolve to the same
    /// save/state folder, so this system must claim only its own library's files rather than the
    /// whole folder — otherwise every sharing system tracks and uploads every file more than once.
    /// </param>
    public RetroArchSaveLocationProvider(
        string systemId,
        string? corePath,
        string installationDirectory,
        string? directoryOverride = null,
        string? homeDirectory = null,
        string? xdgConfigHome = null,
        bool? isWindows = null,
        bool? isMacOS = null,
        bool isFlatpak = false,
        Func<IReadOnlyCollection<string>>? gameFileNames = null,
        bool coreSharedAcrossSystems = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        _systemId = systemId;
        _gameFileNames = gameFileNames;
        _installationDirectory = Path.GetFullPath(installationDirectory);
        _directoryOverride = string.IsNullOrWhiteSpace(directoryOverride)
            ? null
            : Path.GetFullPath(directoryOverride);
        var resolvedHome = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : homeDirectory;
        _homeDirectory = string.IsNullOrWhiteSpace(resolvedHome) ? string.Empty : Path.GetFullPath(resolvedHome);
        var configuredXdgHome = string.IsNullOrWhiteSpace(xdgConfigHome)
            ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            : xdgConfigHome;
        _xdgConfigHome = string.IsNullOrWhiteSpace(configuredXdgHome) ||
                         !Path.IsPathFullyQualified(configuredXdgHome)
            ? null
            : Path.GetFullPath(configuredXdgHome);
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
        _isMacOS = isMacOS ?? OperatingSystem.IsMacOS();
        _isFlatpak = isFlatpak;
        _coreSharedAcrossSystems = coreSharedAcrossSystems;

        // Resolved last: naming the core reads its info entry, whose folders depend on the platform
        // and Flatpak flags set just above.
        _core = RetroArchCore.ForCorePath(corePath, ResolveInfoDirectoryCandidates());
    }

    public string SystemId => _systemId;

    // Battery saves key by the system ("<systemId>/"), so a RetroArch battery save interoperates with
    // any other emulator for the same system. Save states keep the former emulator+system-scoped
    // namespace so cores for different systems never collide.
    public string UnitIdPrefix => SystemId + "/";

    public string StateNamespacePrefix => $"retroarch/{_systemId}/";

    /// <summary>Returns the effective save directory and the core it was resolved for.</summary>
    public Task<RetroArchSaveInfo> GetSaveInfoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Resolve(cancellationToken, probePerGameOverride: true), cancellationToken);

    /// <summary>Returns the effective save directory.</summary>
    public async Task<string> GetSaveDirectoryAsync(CancellationToken cancellationToken = default) =>
        (await GetSaveInfoAsync(cancellationToken)).SaveDirectory;

    public Task<RetroArchContentDirectories> GetContentDirectoriesAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ResolveContentDirectories(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    /// <summary>
    /// Games whose save folder holds more than one save file — typically the same save under two
    /// extensions, because another machine's core version writes <c>.sav</c> where this one writes
    /// <c>.srm</c>. Both are synced (neither is EmuShelf's to discard), but the emulator loads only
    /// one, so this is worth telling the user about.
    /// </summary>
    public Task<IReadOnlyList<string>> GetAmbiguousSaveNamesAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<string>>(
            () => GetSaveUnits(cancellationToken)
                .GroupBy(unit => Path.GetFileNameWithoutExtension(unit.DisplayName), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList(),
            cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;

        var localId = unitId[UnitIdPrefix.Length..];
        var info = Resolve(CancellationToken.None);
        string fileName;
        if (IsPlayStation)
        {
            // Land the shared PS1 card key on this core's own card file: read the existing card
            // whatever its extension, and for a fresh restore create the core's default <base>.srm.
            if (PlayStationCardBaseName(localId) is not { } baseName)
                return null;
            fileName = FindExistingPlayStationCard(info.SaveDirectory, baseName)
                ?? baseName + PlayStationCardExtension;
        }
        else
        {
            fileName = localId;
        }

        if (!IsSafeSaveFileName(fileName) || !BelongsToThisSystem(fileName, info))
            return null;

        return new SaveUnitLocation(
            Path.Combine(info.SaveDirectory, fileName),
            info.SaveDirectory,
            SaveUnitKind.File);
    }

    private bool IsPlayStation => string.Equals(_systemId, PlayStationSystemId, StringComparison.Ordinal);

    // The shared cross-emulator card key portion for a local PS1 save file (<base>.<ext>): mirrors the
    // key DuckStation emits for a file-title per-game card, so the two emulators meet at one cloud entry.
    private static string PlayStationCardLocalId(string fileName) =>
        PlayStationCardKeyPrefix + Path.GetFileNameWithoutExtension(fileName) + PlayStationCardKeySuffix;

    // The base game name inside such a key, or null when the id is not a PS1 card key.
    private static string? PlayStationCardBaseName(string localId)
    {
        if (!localId.StartsWith(PlayStationCardKeyPrefix, StringComparison.Ordinal) ||
            !localId.EndsWith(PlayStationCardKeySuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var baseName = localId[PlayStationCardKeyPrefix.Length..^PlayStationCardKeySuffix.Length];
        return baseName.Length == 0 || baseName.Contains('/') || baseName.Contains('\\') || baseName.Contains('\0')
            ? null
            : baseName;
    }

    // A card already on disk for this game keeps its own name (the core may write .srm or .mcr); only a
    // fresh restore falls back to the default extension. Returns the file name, or null when none exists.
    private static string? FindExistingPlayStationCard(string saveDirectory, string baseName)
    {
        foreach (var extension in PlayStationCardExtensions)
        {
            var candidate = baseName + extension;
            if (File.Exists(Path.Combine(saveDirectory, candidate)))
                return candidate;
        }

        return null;
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        var info = Resolve(cancellationToken);
        if (!Directory.Exists(info.SaveDirectory))
            return [];

        var units = new List<SaveUnit>();
        foreach (var path in Directory.EnumerateFiles(info.SaveDirectory)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (IsSafeSaveFileName(fileName) && BelongsToThisSystem(fileName, info))
            {
                var localId = IsPlayStation ? PlayStationCardLocalId(fileName) : fileName;
                units.Add(new SaveUnit(UnitIdPrefix + localId, fileName, SaveUnitKind.File));
            }
        }

        return units;
    }

    // Unless RetroArch gives this core a folder of its own — or the user pointed this platform at
    // one — every core writes into the same directory, so the file name is the only thing that says
    // which system a save belongs to. Matching it against this system's library entries keeps one
    // RetroArch row from claiming another row's saves; an unrecognized file is left alone.
    //
    // Deliberately matched on the game name rather than on a per-core extension allow-list: cores
    // name saves after the content but choose their own extension (.srm, .sav, .dsv, .rtc, .brm),
    // so an allow-list would silently stop syncing the day the user changed core.
    private bool BelongsToThisSystem(string fileName, RetroArchSaveInfo info)
    {
        if (info.IsExclusive)
            return true;

        // Resolved once per provider, not once per file: the caller's lookup reads the library
        // database, and a folder of saves would otherwise mean one query per save.
        _knownGameFileNames ??= _gameFileNames?.Invoke().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        return _knownGameFileNames.Contains(Path.GetFileNameWithoutExtension(fileName));
    }

    /// <summary>
    /// Whether a save-state file in this core's state folder belongs to this system. An unshared
    /// core owns its whole state folder as before; a core shared across systems (mGBA for both Game
    /// Boy Advance and Game Boy Color) owns only states named after one of this system's library
    /// games, so the sharing systems do not each enumerate and sync every state in the folder.
    /// </summary>
    public bool StateBelongsToThisSystem(string stateFilePath)
    {
        if (!_coreSharedAcrossSystems)
            return true;
        _knownGameFileNames ??= _gameFileNames?.Invoke().ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        return _knownGameFileNames.Contains(GameNameFromStateFile(Path.GetFileName(stateFilePath)));
    }

    // RetroArch names a state after the content: "<game>.state", ".state1".."stateN", ".state.auto",
    // and ".state.png" thumbnails. The game name is everything before the first ".state".
    private static string GameNameFromStateFile(string fileName)
    {
        var marker = fileName.IndexOf(".state", StringComparison.OrdinalIgnoreCase);
        return marker < 0 ? Path.GetFileNameWithoutExtension(fileName) : fileName[..marker];
    }

    // probePerGameOverride is only meaningful to detection, which surfaces HasUnreadPerGameOverride to
    // the user. The sync path (ResolveUnit/GetSaveUnits) never reads that field, so it skips the scan
    // that parses every per-game override cfg — work that would otherwise repeat on every unit.
    private RetroArchSaveInfo Resolve(CancellationToken cancellationToken, bool probePerGameOverride = false)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // An override that is not RetroArch's configuration folder is taken as the save folder itself:
        // the user pointed at the exact directory, which is more specific than anything retroarch.cfg
        // could tell us — and it needs no core, because there is no core-named folder to resolve.
        // This is how a coreless standalone emulator that writes RetroArch-shaped saves (WatermelonDS —
        // <game>.srm in a flat folder) syncs: point this provider at that folder. Resolving here before
        // the core check keeps such a system from failing for lack of a libretro core it never uses.
        // See docs/android-save-sync-model.md.
        if (_directoryOverride is not null && !File.Exists(Path.Combine(_directoryOverride, ConfigFileName)))
            return new RetroArchSaveInfo(
                _directoryOverride,
                _core ?? new RetroArchCore(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(_directoryOverride)), Name: null),
                SortedByCore: false,
                IsExclusive: true);

        // From here the configuration decides the folder, and naming a sorted-by-core folder needs the
        // core — so a core is required only on this path, not for an exact-folder override above.
        var core = _core ?? throw new RetroArchConfigurationFormatException(
            "No libretro core is configured for this system, so EmuShelf cannot tell which of " +
            "RetroArch's save folders belongs to it.");

        var configPath = ResolveConfigPath();
        var configDirectory = Path.GetDirectoryName(configPath)!;
        var settings = ReadConfig(configPath, cancellationToken);

        // A core override may relocate saves for this core alone; RetroArch applies it on top of
        // the main configuration, so read it the same way and let its keys win.
        var overrideRoot = ResolveOverrideRoot(settings, configDirectory, core);
        foreach (var (key, value) in ReadCoreOverride(overrideRoot, core, cancellationToken))
            settings[key] = value;

        if (IsTrue(settings, "cloud_sync_enable") && IsTrue(settings, "cloud_sync_sync_saves", defaultValue: true))
        {
            throw new RetroArchConfigurationFormatException(
                "RetroArch's own cloud sync is enabled for save files. EmuShelf will not manage the same " +
                "saves as a second sync system — turn one of them off.");
        }

        if (IsTrue(settings, "savefiles_in_content_dir"))
        {
            throw new RetroArchConfigurationFormatException(
                "RetroArch is configured to keep save files next to the game files, which EmuShelf does not sync. " +
                "Point RetroArch at a save folder, or set this platform's save location manually.");
        }

        if (IsTrue(settings, "sort_savefiles_by_content_enable"))
        {
            throw new RetroArchConfigurationFormatException(
                "RetroArch is configured to sort save files by content directory, so each game's saves live in a " +
                "different folder. Turn that off, or set this platform's save location manually.");
        }

        var configured = settings.GetValueOrDefault("savefile_directory");
        if (string.IsNullOrWhiteSpace(configured) ||
            configured.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            throw new RetroArchConfigurationFormatException(
                "RetroArch has no save directory configured, so it writes saves next to the game files. " +
                "Point RetroArch at a save folder, or set this platform's save location manually.");
        }

        var directory = ExpandPath(configured, configDirectory);
        var sortedByCore = IsTrue(settings, "sort_savefiles_enable");
        if (sortedByCore)
        {
            // The folder name is the core's display name, and only RetroArch knows it. Without the
            // core's info entry EmuShelf would be guessing at a directory name, which is exactly
            // the kind of guess that silently syncs nothing.
            var folderName = core.Name ?? throw new RetroArchConfigurationFormatException(
                $"RetroArch sorts saves into a folder named after the core, but EmuShelf could not read " +
                $"the core name of '{core.FileName}' from RetroArch's info folder. " +
                "Set this platform's save location to that core's save folder.");
            directory = Path.Combine(directory, folderName);
        }

        return new RetroArchSaveInfo(
            directory,
            core,
            sortedByCore,
            // A per-core folder is "all mine" only when this core serves one EmuShelf system. When it
            // is shared (mGBA for both GBA and GBC), fall back to claiming only this system's library
            // saves so the two systems do not each claim — and double-upload — the whole folder.
            IsExclusive: sortedByCore && !_coreSharedAcrossSystems,
            HasUnreadPerGameOverride: probePerGameOverride &&
                HasPerGameSaveOverride(overrideRoot, core, cancellationToken));
    }

    private RetroArchContentDirectories ResolveContentDirectories(CancellationToken cancellationToken)
    {
        var core = _core ?? throw new RetroArchConfigurationFormatException(
            "No libretro core is configured for this system, so optional content cannot be scoped safely.");
        var configPath = ResolveConfigPath();
        var configDirectory = Path.GetDirectoryName(configPath)!;
        var settings = ReadConfig(configPath, cancellationToken);

        var cheatRoot = ResolveConfiguredDirectory(
            settings.GetValueOrDefault("cheat_database_path"),
            Path.Combine(configDirectory, "cheats"));
        var coreCheats = core.Name is null ? null : Path.Combine(cheatRoot, core.Name);

        string? states = null;
        if (!IsTrue(settings, "savestates_in_content_dir") &&
            !IsTrue(settings, "sort_savestates_by_content_enable"))
        {
            states = ResolveConfiguredDirectory(
                settings.GetValueOrDefault("savestate_directory"),
                Path.Combine(configDirectory, "states"));
            if (IsTrue(settings, "sort_savestates_enable"))
            {
                if (core.Name is null)
                    states = null;
                else
                    states = Path.Combine(states, core.Name);
            }
        }

        return new RetroArchContentDirectories(coreCheats, states);

        string ResolveConfiguredDirectory(string? configured, string fallback) =>
            string.IsNullOrWhiteSpace(configured) || configured.Equals("default", StringComparison.OrdinalIgnoreCase)
                ? Path.GetFullPath(fallback)
                : ExpandPath(configured, configDirectory);
    }

    // RetroArch keeps core info files in an "info" folder beside its configuration, so this mirrors
    // ResolveConfigPath's per-platform locations. Unlike that method it never throws for a missing
    // home directory: naming the core is best-effort with a KnownCoreNames fallback, and must not
    // fail construction. The portable folder comes first so a self-contained install wins.
    private IReadOnlyList<string> ResolveInfoDirectoryCandidates()
    {
        var candidates = new List<string> { Path.Combine(_installationDirectory, "info") };
        if (_isFlatpak)
        {
            if (!string.IsNullOrWhiteSpace(_homeDirectory))
                candidates.Add(Path.Combine(
                    _homeDirectory, ".var", "app", "org.libretro.RetroArch", "config", "retroarch", "info"));
        }
        else if (_isMacOS)
        {
            if (!string.IsNullOrWhiteSpace(_homeDirectory))
                candidates.Add(Path.Combine(
                    _homeDirectory, "Library", "Application Support", "RetroArch", "info"));
        }
        else if (!_isWindows)
        {
            if (_xdgConfigHome is not null)
                candidates.Add(Path.Combine(_xdgConfigHome, "retroarch", "info"));
            else if (!string.IsNullOrWhiteSpace(_homeDirectory))
                candidates.Add(Path.Combine(_homeDirectory, ".config", "retroarch", "info"));
        }

        return candidates;
    }

    // RetroArch's own precedence: a configuration beside the executable is a portable install,
    // otherwise the platform's configuration directory.
    private string ResolveConfigPath()
    {
        var candidates = new List<string> { Path.Combine(_installationDirectory, ConfigFileName) };
        if (_isFlatpak)
        {
            candidates.Add(Path.Combine(
                RequireHome(), ".var", "app", "org.libretro.RetroArch", "config", "retroarch", ConfigFileName));
        }
        else if (_isWindows)
        {
            candidates.Add(Path.Combine(_installationDirectory, "config", ConfigFileName));
        }
        else if (_isMacOS)
        {
            candidates.Add(Path.Combine(
                RequireHome(), "Library", "Application Support", "RetroArch", "config", ConfigFileName));
        }
        else
        {
            candidates.Add(_xdgConfigHome is null
                ? Path.Combine(RequireHome(), ".config", "retroarch", ConfigFileName)
                : Path.Combine(_xdgConfigHome, "retroarch", ConfigFileName));
        }

        return candidates.FirstOrDefault(File.Exists) ??
            throw new RetroArchConfigurationFormatException(
                "EmuShelf could not find RetroArch's retroarch.cfg. Set this platform's save location manually.");
    }

    private static Dictionary<string, string> ReadConfig(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return RetroArchConfigAdapter.Parse(reader, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RetroArchConfigurationFormatException($"RetroArch's {Path.GetFileName(path)} could not be read.", ex);
        }
    }

    // RetroArch keeps overrides under <config directory>/<core name>/, where the config directory is
    // rgui_config_directory when set.
    private string? ResolveOverrideRoot(
        IReadOnlyDictionary<string, string> settings,
        string configDirectory,
        RetroArchCore core)
    {
        if (core.Name is null)
            return null;

        var configured = settings.GetValueOrDefault("rgui_config_directory");
        return Path.Combine(
            string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(configDirectory, "config")
                : ExpandPath(configured, configDirectory),
            core.Name);
    }

    private static IReadOnlyDictionary<string, string> ReadCoreOverride(
        string? overrideRoot,
        RetroArchCore core,
        CancellationToken cancellationToken)
    {
        var overridePath = overrideRoot is null ? null : Path.Combine(overrideRoot, core.Name + ".cfg");
        return overridePath is not null && File.Exists(overridePath)
            ? ReadConfig(overridePath, cancellationToken)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // Beside the core override sit content-directory and per-game overrides. Those apply to one game
    // at a time, so EmuShelf cannot resolve a single folder from them — but a silent miss for that
    // game is worse than saying so, and the check is a handful of small files.
    private static bool HasPerGameSaveOverride(
        string? overrideRoot,
        RetroArchCore core,
        CancellationToken cancellationToken)
    {
        if (overrideRoot is null || !Directory.Exists(overrideRoot))
            return false;

        var coreOverrideName = core.Name + ".cfg";
        try
        {
            foreach (var path in Directory.EnumerateFiles(overrideRoot, "*.cfg"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFileName(path).Equals(coreOverrideName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ReadConfig(path, cancellationToken).ContainsKey("savefile_directory"))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    // RetroArch expands ':' to its own application directory and '~' to the home directory before
    // using a configured path; a relative path resolves against the configuration it came from.
    private string ExpandPath(string configuredPath, string configDirectory)
    {
        var value = configuredPath.Trim();
        if (value.Contains('\0'))
            throw new RetroArchConfigurationFormatException("RetroArch's save directory is not a supported path.");

        // ':' is RetroArch's application directory. That is the installation directory only for a
        // portable install, which is exactly the case where the configuration sits beside the
        // executable; anywhere else — a Flatpak, or a Linux config under ~/.config — the
        // installation directory may be a folder RetroArch has nothing to do with, so anchor on the
        // configuration that used the prefix instead of resolving into an unrelated tree.
        if (value.StartsWith(':'))
        {
            var applicationDirectory = File.Exists(Path.Combine(_installationDirectory, ConfigFileName))
                ? _installationDirectory
                : configDirectory;
            value = applicationDirectory + value[1..];
        }
        else if (value.StartsWith('~'))
        {
            value = RequireHome() + value[1..];
        }

        try
        {
            var normalized = value
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(
                Path.IsPathFullyQualified(normalized) ? normalized : Path.Combine(configDirectory, normalized));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RetroArchConfigurationFormatException("RetroArch's save directory is not a supported path.", ex);
        }
    }

    private string RequireHome() =>
        string.IsNullOrWhiteSpace(_homeDirectory)
            ? throw new RetroArchConfigurationFormatException("The home directory could not be resolved on this system.")
            : _homeDirectory;

    private static bool IsTrue(IReadOnlyDictionary<string, string> settings, string key, bool defaultValue = false) =>
        settings.TryGetValue(key, out var value)
            ? value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1"
            : defaultValue;

    // A unit is a direct child of the save directory that is not one of RetroArch's own artifacts.
    // Save states are excluded by name shape (Game.state, Game.state3, Game.state.auto) because
    // they are build- and core-fragile, and are out of scope by design.
    private static bool IsSafeSaveFileName(string value)
    {
        if (value.Length is < 1 or > 255 ||
            value.StartsWith('.') ||
            !string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        // A save always carries an extension. Cores drop hint files into their own save folder
        // ("Place NDS saves here"), and those are not data to carry between machines.
        var extension = Path.GetExtension(value);
        return extension.Length > 1 &&
            !extension.StartsWith(".state", StringComparison.OrdinalIgnoreCase) &&
            !ExcludedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads RetroArch's <c>key = "value"</c> configuration lines.</summary>
    private static class RetroArchConfigAdapter
    {
        public static Dictionary<string, string> Parse(TextReader reader, CancellationToken cancellationToken)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            while (reader.ReadLine() is { } rawLine)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                var key = line[..separator].Trim();
                var value = line[(separator + 1)..].Trim();
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];
                if (key.Length > 0)
                    values[key] = value;
            }

            return values;
        }
    }
}

/// <summary>Raised when RetroArch's configuration does not identify a save layout EmuShelf can sync.</summary>
public sealed class RetroArchConfigurationFormatException : SaveProviderConfigurationException
{
    public RetroArchConfigurationFormatException(string message) : base(message)
    {
    }

    public RetroArchConfigurationFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
