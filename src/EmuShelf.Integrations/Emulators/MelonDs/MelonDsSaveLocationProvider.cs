using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.MelonDs;

/// <summary>Where this machine's melonDS keeps its saves, and how EmuShelf worked that out.</summary>
/// <param name="SaveDirectory">
/// The folder holding <c>&lt;game&gt;.sav</c>, or null when melonDS has none configured — its default,
/// which writes each save beside its ROM instead (see <see cref="MelonDsSaveLocationProvider"/>).
/// </param>
/// <param name="ConfigDirectory">melonDS's resolved emu directory, or null when none exists here.</param>
/// <param name="ConfigFilePath">The configuration file that was read, or null when none was found.</param>
/// <param name="IsOverridden">Whether the folder came from the user's EmuShelf override.</param>
/// <param name="SavestateDirectory">The configured save-state folder, or null when melonDS has none.</param>
public sealed record MelonDsSaveInfo(
    string? SaveDirectory,
    string? ConfigDirectory,
    string? ConfigFilePath,
    bool IsOverridden,
    string? SavestateDirectory);

/// <summary>
/// Locates standalone melonDS's Nintendo DS battery saves — one raw <c>&lt;game&gt;.sav</c> per game —
/// by reading melonDS's own <c>SaveFilePath</c> setting, and exposes each as a save unit under the
/// cross-emulator <see cref="NintendoDsBatterySaveKey"/>, so a save round-trips between standalone
/// melonDS and a RetroArch DS core (which writes the byte-identical dump as <c>.srm</c>).
/// </summary>
/// <remarks>
/// <para>
/// melonDS's default is to write a save <em>beside its ROM</em>. EmuShelf deliberately does not sync
/// that layout: it would mean writing into the user's game folders, and a save there is
/// indistinguishable from any other file that happens to sit next to a ROM. So this provider syncs
/// only a dedicated folder — melonDS's own <c>Config → Path settings → Save file path</c>, or the
/// EmuShelf save-location override — and reports the unconfigured case rather than guessing at it.
/// </para>
/// <para>
/// A folder melonDS's own configuration named is claimed by <em>name</em>, not wholesale: melonDS
/// puts a Slot-2 GBA cartridge's <c>.sav</c> there too, and the folder may be shared with another
/// emulator, so only files named after a Nintendo DS game in the library are units. A folder the user
/// pointed EmuShelf at is instead treated as this platform's own and claimed whole — the same rule
/// the RetroArch provider applies to an explicit override, and the escape hatch for a library whose
/// file names differ from the save names.
/// </para>
/// </remarks>
public sealed class MelonDsSaveLocationProvider : ISaveLocationProvider
{
    // melonDS's own extension; a save restored here for the first time lands on it so melonDS finds
    // it. A game that already has a file here keeps that file — see
    // NintendoDsBatterySaveKey.ResolveFileName.
    private const string MelonDsExtension = ".sav";

    // melonDS names the DSi/firmware save after "firmware" when no ROM is loaded. It is console
    // identity, not a game save, and never syncs — it is excluded explicitly for the case where the
    // library list is unavailable and every other file would be claimed.
    private const string FirmwareBaseName = "firmware";

    private readonly string _emulatorId;
    private readonly string _installationDirectory;
    private readonly string? _saveDirectoryOverride;
    private readonly string _homeDirectory;
    private readonly string? _appDataDirectory;
    private readonly string? _localAppDataDirectory;
    private readonly string? _xdgConfigHome;
    private readonly bool _isFlatpak;
    private readonly Func<IReadOnlyCollection<string>>? _gameFileNames;
    private HashSet<string>? _knownGameFileNames;
    private MelonDsSaveInfo? _resolved;

    public MelonDsSaveLocationProvider(
        string emulatorId,
        string installationDirectory,
        string? saveDirectoryOverride = null,
        string? homeDirectory = null,
        string? appDataDirectory = null,
        string? localAppDataDirectory = null,
        string? xdgConfigHome = null,
        bool isFlatpak = false,
        Func<IReadOnlyCollection<string>>? gameFileNames = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        _emulatorId = emulatorId;
        _installationDirectory = Path.GetFullPath(installationDirectory);
        _saveDirectoryOverride = FullPathOrNull(saveDirectoryOverride);
        var resolvedHome = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : homeDirectory;
        _homeDirectory = string.IsNullOrWhiteSpace(resolvedHome) ? string.Empty : Path.GetFullPath(resolvedHome);
        _appDataDirectory = FullPathOrNull(appDataDirectory)
            ?? FullPathOrNull(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        _localAppDataDirectory = FullPathOrNull(localAppDataDirectory)
            ?? FullPathOrNull(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var configuredXdgHome = string.IsNullOrWhiteSpace(xdgConfigHome)
            ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
            : xdgConfigHome;
        _xdgConfigHome = string.IsNullOrWhiteSpace(configuredXdgHome) ||
                         !Path.IsPathFullyQualified(configuredXdgHome)
            ? null
            : Path.GetFullPath(configuredXdgHome);
        _isFlatpak = isFlatpak;
        _gameFileNames = gameFileNames;
    }

    /// <summary>The melonDS channel this provider serves (<c>melonds</c> or <c>melonds-nightly</c>).</summary>
    public string EmulatorId => _emulatorId;

    public string SystemId => NintendoDsBatterySaveKey.SystemId;

    // Battery saves key by the system, and DS battery saves additionally key by game name alone, so
    // one game is one cloud entry whichever DS emulator wrote it.
    public string UnitIdPrefix => SystemId + "/";

    // Save states are the opposite: melonDS's .ml0 format is bound to its build, and the release and
    // nightly channels are different builds, so each keeps its own namespace.
    public string StateNamespacePrefix => $"{_emulatorId}/{SystemId}/";

    /// <summary>Only the canonical DS battery keys are this provider's; see the class remarks.</summary>
    public bool OwnsUnit(string unitId) =>
        unitId is not null &&
        unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal) &&
        NintendoDsBatterySaveKey.BaseNameFrom(unitId[UnitIdPrefix.Length..]) is not null;

    /// <summary>Everything Settings needs to describe this machine's melonDS save location.</summary>
    public Task<MelonDsSaveInfo> GetSaveInfoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Resolve(cancellationToken), cancellationToken);

    /// <summary>
    /// The resolved save folder, or melonDS's emu directory when it has none configured — the folder
    /// Settings shows, never a path this provider would write into on its own.
    /// </summary>
    public async Task<string> GetSaveDataDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var info = await GetSaveInfoAsync(cancellationToken);
        return info.SaveDirectory ?? info.ConfigDirectory ?? _installationDirectory;
    }

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;
        if (NintendoDsBatterySaveKey.BaseNameFrom(unitId[UnitIdPrefix.Length..]) is not { } baseName)
            return null;

        var directory = Resolve(CancellationToken.None).SaveDirectory;
        if (directory is null || !BelongsToThisLibrary(baseName))
            return null;

        // An existing save keeps its own file name — a folder shared with a RetroArch core may already
        // hold this game as a .srm — and only a first-time restore creates melonDS's own .sav.
        var fileName = NintendoDsBatterySaveKey.ResolveFileName(directory, baseName, MelonDsExtension);
        return new SaveUnitLocation(Path.Combine(directory, fileName), directory, SaveUnitKind.File);
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        var directory = Resolve(cancellationToken).SaveDirectory;
        if (directory is null || !Directory.Exists(directory))
            return [];

        var units = new List<SaveUnit>();
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (NintendoDsBatterySaveKey.LocalIdFor(fileName) is not { } localId)
                continue;

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (!BelongsToThisLibrary(baseName))
                continue;

            // One game is one unit even when the folder holds both extensions (a folder shared with
            // RetroArch): the sync would otherwise build two entries resolving to the same file.
            if (!emitted.Add(localId))
                continue;

            units.Add(new SaveUnit(UnitIdPrefix + localId, fileName, SaveUnitKind.File));
        }

        return units;
    }

    // melonDS's own save folder is not exclusively this system's — a Slot-2 GBA cartridge's save lands
    // there too, and the folder may be shared with another emulator — so a file is claimed only when
    // it is named after a Nintendo DS game in the library. Resolved once per provider: the lookup
    // reads the library database and a folder of saves would otherwise mean one query per save.
    //
    // A folder the user chose here is exempt: they said this folder is this platform's saves, which is
    // more specific than anything the emulator's configuration could tell us, and it is the only way
    // out when the library's file names do not match the save names. Same rule as the RetroArch
    // provider's exact-folder override. The firmware/DSi console save is never a game save either way.
    private bool BelongsToThisLibrary(string baseName)
    {
        if (string.Equals(baseName, FirmwareBaseName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_gameFileNames is null || _saveDirectoryOverride is not null)
            return true;
        _knownGameFileNames ??= _gameFileNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _knownGameFileNames.Contains(baseName);
    }

    private MelonDsSaveInfo Resolve(CancellationToken cancellationToken)
    {
        if (_resolved is { } cached)
            return cached;

        var configDirectory = ResolveConfigDirectory();
        var configuration = configDirectory is null
            ? null
            : MelonDsConfigFile.TryRead(configDirectory, cancellationToken);

        var configured = ResolveConfiguredDirectory(configuration?.SaveFilePath, configDirectory);
        var saveDirectory = _saveDirectoryOverride ?? configured;
        var savestateDirectory = ResolveConfiguredDirectory(configuration?.SavestatePath, configDirectory);

        return _resolved = new MelonDsSaveInfo(
            saveDirectory,
            configDirectory,
            configuration?.Path,
            IsOverridden: _saveDirectoryOverride is not null,
            savestateDirectory);
    }

    // melonDS stores its path settings as it received them, so a relative one is relative to whatever
    // directory it was started from. Its own emu directory is the only stable interpretation EmuShelf
    // can give that; an absolute path (what the GUI's folder picker writes) is used as-is.
    private static string? ResolveConfiguredDirectory(string? configured, string? configDirectory)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;
        var trimmed = configured.Trim();
        if (Path.IsPathFullyQualified(trimmed))
            return Path.GetFullPath(trimmed);
        return configDirectory is null ? null : Path.GetFullPath(Path.Combine(configDirectory, trimmed));
    }

    /// <summary>
    /// melonDS's emu directory, the folder holding <c>melonDS.toml</c>. Mirrors melonDS's own
    /// <c>pathInit</c>: a <c>portable</c> folder beside the executable wins outright (melonDS adopts it
    /// whether or not it holds a config yet), then a Windows portable build's own folder, then Qt's
    /// per-platform config location. Every candidate must exist to be chosen, so an install that has
    /// never been run resolves to null rather than to a plausible-but-absent path.
    /// </summary>
    internal string? ResolveConfigDirectory()
    {
        return Candidates()
            .Select(ExistingDirectory)
            .FirstOrDefault(directory => directory is not null);

        IEnumerable<string?> Candidates()
        {
            if (_isFlatpak)
            {
                yield return Combine(_homeDirectory, ".var", "app", "net.kuribo64.melonDS", "config", "melonDS");
                yield break;
            }

            yield return Path.Combine(_installationDirectory, "portable");
            if (HasConfigFile(_installationDirectory))
                yield return _installationDirectory;

            yield return Combine(_xdgConfigHome, "melonDS");                            // Linux (explicit XDG)
            yield return Combine(_homeDirectory, ".config", "melonDS");                 // Linux (default XDG)
            yield return Combine(_homeDirectory, "Library", "Preferences", "melonDS");  // macOS
            yield return Combine(_localAppDataDirectory, "melonDS");                    // Windows
            yield return Combine(_appDataDirectory, "melonDS");
            yield return Combine(_homeDirectory, "Library", "Application Support", "melonDS");
        }
    }

    private static bool HasConfigFile(string directory)
    {
        try
        {
            return File.Exists(Path.Combine(directory, MelonDsConfigFile.FileName)) ||
                   File.Exists(Path.Combine(directory, MelonDsConfigFile.LegacyFileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Combine(string? directory, params string[] segments) =>
        string.IsNullOrWhiteSpace(directory) ? null : Path.Combine([directory, .. segments]);

    private static string? ExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            return Directory.Exists(path) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? FullPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
}
