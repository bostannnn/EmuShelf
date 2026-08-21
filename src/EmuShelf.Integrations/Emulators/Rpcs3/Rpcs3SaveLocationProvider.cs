using System.Text;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.Rpcs3;

public sealed record Rpcs3ContentDirectories(string Patches, string SaveStates);

/// <summary>One RPCS3 local user account under <c>dev_hdd0/home</c>.</summary>
/// <param name="Id">The eight-digit account directory name, e.g. <c>00000001</c>.</param>
/// <param name="Name">The account's display name from <c>localusername</c>, when readable.</param>
/// <param name="HasSaveData">Whether the account already holds at least one complete save.</param>
public sealed record Rpcs3Profile(string Id, string? Name, bool HasSaveData);

/// <summary>The resolved save-data directory plus the local account it belongs to.</summary>
/// <param name="SaveDataDirectory">The bound account's <c>savedata</c> directory.</param>
/// <param name="Profile">The account EmuShelf is bound to.</param>
/// <param name="AvailableProfiles">Every account found beside it, empty when one was chosen explicitly.</param>
/// <param name="TrophyDirectory">The bound account's <c>trophy</c> directory, when it is known.</param>
/// <param name="VirtualMemoryCardDirectory">
/// The console-wide <c>dev_hdd0/savedata/vmc</c> directory holding PS1/PS2 Classics virtual memory
/// cards, when the hard-disk root is known.
/// </param>
public sealed record Rpcs3SaveDataInfo(
    string SaveDataDirectory,
    Rpcs3Profile Profile,
    IReadOnlyList<Rpcs3Profile> AvailableProfiles,
    string? TrophyDirectory = null,
    string? VirtualMemoryCardDirectory = null);

/// <summary>
/// Resolves RPCS3's <c>/dev_hdd0</c> through its own read-only <c>vfs.yml</c> and exposes each
/// complete <c>home/&lt;user&gt;/savedata/&lt;save&gt;/</c> directory as one folder unit, including its
/// <c>PARAM.SFO</c>/<c>PARAM.PFD</c>. Trophies, licenses, installed games, caches, configuration and
/// save states live elsewhere under <c>dev_hdd0</c> and are never enumerated.
/// </summary>
/// <remarks>
/// Local account ids are machine-local: the same person is <c>00000001</c> here and may be
/// <c>00000002</c> elsewhere. Unit ids therefore address the save alone, and the account is bound
/// locally — one machine's bound account syncs against another machine's bound account.
/// </remarks>
public sealed class Rpcs3SaveLocationProvider : ISaveLocationProvider
{
    private const string VfsFileName = "vfs.yml";
    private const string ConfigSubdirectory = "config";
    private const string PortableDirectoryName = "portable";
    private const string SaveDataDirectoryName = "savedata";
    private const string HomeDirectoryName = "home";
    private const string HddDirectoryName = "dev_hdd0";
    private const string UserNameFileName = "localusername";
    private const string SaveParametersFileName = "PARAM.SFO";
    private const string TrophyDirectoryName = "trophy";
    private const string TrophyProgressFileName = "TROPUSR.DAT";
    private const string VirtualMemoryCardDirectoryName = "vmc";
    private const string EmulatorDirectoryMacro = "$(EmulatorDir)";
    private const string SaveDataNamespace = "savedata";
    private const string TrophyNamespace = "trophy";
    private const string VirtualMemoryCardNamespace = "vmc";

    private readonly string _installationDirectory;
    private readonly string? _directoryOverride;
    private readonly string _homeDirectory;
    private readonly string? _xdgConfigHome;
    private readonly string? _configDirectoryEnvironmentOverride;
    private readonly bool _isWindows;
    private readonly bool _isMacOS;
    private readonly bool _isFlatpak;

    public Rpcs3SaveLocationProvider(
        string installationDirectory,
        string? directoryOverride = null,
        string? homeDirectory = null,
        string? xdgConfigHome = null,
        string? configDirectoryEnvironmentOverride = null,
        bool? isWindows = null,
        bool? isMacOS = null,
        bool isFlatpak = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        _installationDirectory = Path.GetFullPath(installationDirectory);
        _directoryOverride = FullPathOrNull(directoryOverride);
        var resolvedHome = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : homeDirectory;
        _homeDirectory = string.IsNullOrWhiteSpace(resolvedHome) ? string.Empty : Path.GetFullPath(resolvedHome);
        _xdgConfigHome = AbsoluteOrNull(
            string.IsNullOrWhiteSpace(xdgConfigHome)
                ? Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                : xdgConfigHome);
        _configDirectoryEnvironmentOverride = AbsoluteOrNull(
            string.IsNullOrWhiteSpace(configDirectoryEnvironmentOverride)
                ? Environment.GetEnvironmentVariable("RPCS3_CONFIG_DIR")
                : configDirectoryEnvironmentOverride);
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
        _isMacOS = isMacOS ?? OperatingSystem.IsMacOS();
        _isFlatpak = isFlatpak;
    }

    public string SystemId => "playstation3";

    // Battery saves key by the system ("playstation3/"); save states keep the emulator-scoped namespace.
    public string UnitIdPrefix => SystemId + "/";

    public string StateNamespacePrefix => "rpcs3/";

    /// <summary>RPCS3-owned roots for its patch database and manually created save states.</summary>
    public Task<Rpcs3ContentDirectories> GetContentDirectoriesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = ResolveConfigDirectory();
            return new Rpcs3ContentDirectories(Path.Combine(root, "patches"), Path.Combine(root, "savestates"));
        }, cancellationToken);

    /// <summary>Returns the bound account's save-data directory and the accounts available beside it.</summary>
    public Task<Rpcs3SaveDataInfo> GetSaveDataInfoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ResolveSaveData(cancellationToken), cancellationToken);

    /// <summary>Returns the bound account's save-data directory.</summary>
    public async Task<string> GetSaveDataDirectoryAsync(CancellationToken cancellationToken = default) =>
        (await GetSaveDataInfoAsync(cancellationToken)).SaveDataDirectory;

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;

        var localId = unitId[UnitIdPrefix.Length..];
        var separator = localId.IndexOf('/');
        if (separator <= 0)
            return null;

        var name = localId[(separator + 1)..];
        if (!IsSafeEntryName(name))
            return null;

        var info = ResolveSaveData(CancellationToken.None);
        return localId[..separator] switch
        {
            SaveDataNamespace => Folder(info.SaveDataDirectory, name),
            TrophyNamespace => Folder(info.TrophyDirectory, name),
            VirtualMemoryCardNamespace when IsVirtualMemoryCardName(name) =>
                info.VirtualMemoryCardDirectory is null
                    ? null
                    : new SaveUnitLocation(
                        Path.Combine(info.VirtualMemoryCardDirectory, name),
                        info.VirtualMemoryCardDirectory,
                        SaveUnitKind.File),
            _ => null,
        };

        static SaveUnitLocation? Folder(string? root, string name) =>
            root is null ? null : new SaveUnitLocation(Path.Combine(root, name), root, SaveUnitKind.Folder);
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        var info = ResolveSaveData(cancellationToken);
        var units = new List<SaveUnit>();

        // PARAM.SFO is what makes the directory a save the console (and RPCS3's own save manager)
        // recognizes. Anything without it is a partial copy or scratch directory, not a unit.
        foreach (var directory in EnumerateDirectories(info.SaveDataDirectory, cancellationToken))
        {
            var name = Path.GetFileName(directory);
            if (IsSafeEntryName(name) && File.Exists(Path.Combine(directory, SaveParametersFileName)))
                units.Add(FolderUnit(SaveDataNamespace, name, name));
        }

        // A trophy set is keyed by its own NPWR communication id, which is the same on every
        // machine, and TROPUSR.DAT inside it is the unlock progress worth carrying across.
        foreach (var directory in EnumerateDirectories(info.TrophyDirectory, cancellationToken))
        {
            var name = Path.GetFileName(directory);
            if (IsSafeEntryName(name) && File.Exists(Path.Combine(directory, TrophyProgressFileName)))
                units.Add(FolderUnit(TrophyNamespace, name, $"{name} — trophies"));
        }

        // PS1/PS2 Classics write to console-wide virtual memory cards rather than savedata. Each
        // card is one monolithic file shared by every game that uses it, like a shared PS1 card.
        foreach (var file in EnumerateFiles(info.VirtualMemoryCardDirectory, cancellationToken))
        {
            var name = Path.GetFileName(file);
            if (IsSafeEntryName(name) && IsVirtualMemoryCardName(name))
            {
                units.Add(new SaveUnit(
                    $"{UnitIdPrefix}{VirtualMemoryCardNamespace}/{name}",
                    $"{name} (virtual memory card, shared by every PS1/PS2 Classics game on it)",
                    SaveUnitKind.File));
            }
        }

        return units;

        SaveUnit FolderUnit(string unitNamespace, string name, string displayName) =>
            new($"{UnitIdPrefix}{unitNamespace}/{name}", displayName, SaveUnitKind.Folder);
    }

    private static IEnumerable<string> EnumerateDirectories(string? root, CancellationToken cancellationToken) =>
        Enumerate(root, Directory.EnumerateDirectories, cancellationToken);

    private static IEnumerable<string> EnumerateFiles(string? root, CancellationToken cancellationToken) =>
        Enumerate(root, Directory.EnumerateFiles, cancellationToken);

    private static IEnumerable<string> Enumerate(
        string? root,
        Func<string, IEnumerable<string>> enumerate,
        CancellationToken cancellationToken)
    {
        if (root is null || !Directory.Exists(root))
            yield break;

        foreach (var entry in enumerate(root).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    private Rpcs3SaveDataInfo ResolveSaveData(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = ResolveOverride();
        if (selection?.SaveDataDirectory is { } overriddenSaveData)
        {
            var profileDirectory = Path.GetDirectoryName(overriddenSaveData);
            var profileId = profileDirectory is null ? SaveDataDirectoryName : Path.GetFileName(profileDirectory);
            return new Rpcs3SaveDataInfo(
                overriddenSaveData,
                new Rpcs3Profile(profileId, ReadUserName(profileDirectory), Directory.Exists(overriddenSaveData)),
                [],
                TrophyDirectoryFor(profileDirectory),
                // The virtual-card directory is console-wide, so it is only in scope when the
                // chosen folder still sits in RPCS3's own dev_hdd0/home/<account> shape.
                VirtualMemoryCardDirectoryFor(HddDirectoryAbove(profileDirectory)));
        }

        var hddDirectory = selection?.HddDirectory ?? ResolveHddDirectory(selection?.ConfigDirectory, cancellationToken);
        var homeDirectory = Path.Combine(hddDirectory, HomeDirectoryName);
        var profiles = ReadProfiles(homeDirectory);
        var profile = SelectProfile(profiles, homeDirectory);
        var accountDirectory = Path.Combine(homeDirectory, profile.Id);
        return new Rpcs3SaveDataInfo(
            Path.Combine(accountDirectory, SaveDataDirectoryName),
            profile,
            profiles,
            TrophyDirectoryFor(accountDirectory),
            VirtualMemoryCardDirectoryFor(hddDirectory));
    }

    private static string? TrophyDirectoryFor(string? accountDirectory) =>
        accountDirectory is null ? null : Path.Combine(accountDirectory, TrophyDirectoryName);

    private static string? VirtualMemoryCardDirectoryFor(string? hddDirectory) =>
        hddDirectory is null
            ? null
            : Path.Combine(hddDirectory, SaveDataDirectoryName, VirtualMemoryCardDirectoryName);

    // <hdd0>/home/<account> is the only shape a console-wide directory may be derived from; a
    // folder chosen anywhere else stays account-scoped rather than reaching up into a parent
    // EmuShelf was never pointed at.
    private static string? HddDirectoryAbove(string? accountDirectory)
    {
        var homeDirectory = accountDirectory is null ? null : Path.GetDirectoryName(accountDirectory);
        if (homeDirectory is null ||
            !Path.GetFileName(homeDirectory).Equals(HomeDirectoryName, StringComparison.OrdinalIgnoreCase) ||
            !IsProfileId(Path.GetFileName(accountDirectory!)))
        {
            return null;
        }

        return Path.GetDirectoryName(homeDirectory);
    }

    // Accept every folder a user might reasonably pick in the override box: RPCS3's own directory,
    // its dev_hdd0, one account folder, or that account's savedata. Anything else is rejected rather
    // than silently treated as an emulator directory that would resolve to an empty save list.
    private OverrideSelection? ResolveOverride()
    {
        if (_directoryOverride is null)
            return null;

        // Order matters, widest container first. dev_hdd0 has a savedata directory of its own (the
        // PS1/PS2 Classics virtual memory cards), so testing for a savedata child before testing
        // for home would resolve a dev_hdd0 override to the wrong folder entirely.
        if (Directory.Exists(Path.Combine(_directoryOverride, HddDirectoryName)) ||
            HasVfsFile(_directoryOverride))
        {
            return new OverrideSelection(ConfigDirectory: _directoryOverride);
        }

        if (Directory.Exists(Path.Combine(_directoryOverride, HomeDirectoryName)))
            return new OverrideSelection(HddDirectory: _directoryOverride);

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_directoryOverride));
        if (File.Exists(Path.Combine(_directoryOverride, UserNameFileName)) ||
            IsProfileId(name))
        {
            return new OverrideSelection(
                SaveDataDirectory: Path.Combine(_directoryOverride, SaveDataDirectoryName));
        }

        if (name.Equals(SaveDataDirectoryName, StringComparison.OrdinalIgnoreCase))
            return new OverrideSelection(SaveDataDirectory: _directoryOverride);

        throw new Rpcs3ConfigurationFormatException(
            "The selected RPCS3 folder contains no dev_hdd0, vfs.yml, or savedata directory.");
    }

    private string ResolveHddDirectory(string? overriddenConfigDirectory, CancellationToken cancellationToken)
    {
        var configDirectory = overriddenConfigDirectory ?? ResolveConfigDirectory();
        var vfsPath = FindVfsFile(configDirectory);
        if (vfsPath is null)
            return Path.Combine(configDirectory, HddDirectoryName);

        Rpcs3VfsConfiguration configuration;
        try
        {
            using var stream = new FileStream(vfsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            configuration = Rpcs3VfsAdapter.Parse(reader, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new Rpcs3ConfigurationFormatException("RPCS3's vfs.yml could not be read.", ex);
        }

        // RPCS3 expands $(EmulatorDir) to its own emulator directory, which is the configuration
        // directory itself unless vfs.yml names another one. An empty device entry means the
        // documented default, $(EmulatorDir)dev_hdd0/.
        var emulatorDirectory = string.IsNullOrWhiteSpace(configuration.EmulatorDirectory)
            ? configDirectory
            : ResolvePath(configDirectory, configDirectory, configuration.EmulatorDirectory, "emulator directory");
        var hdd0 = string.IsNullOrWhiteSpace(configuration.Hdd0Directory)
            ? Path.Combine(emulatorDirectory, HddDirectoryName)
            : ResolvePath(configDirectory, emulatorDirectory, configuration.Hdd0Directory, "/dev_hdd0/ path");
        return hdd0;
    }

    // RPCS3's own precedence: a portable directory beside the executable wins on every platform,
    // then RPCS3_CONFIG_DIR and the executable directory on Windows, then the platform default.
    private string ResolveConfigDirectory()
    {
        var portable = Path.Combine(_installationDirectory, PortableDirectoryName);
        if (Directory.Exists(portable))
            return portable;

        if (_isFlatpak)
        {
            return Path.Combine(
                RequireHome(), ".var", "app", "net.rpcs3.RPCS3", ConfigSubdirectory, "rpcs3");
        }

        if (_isWindows)
            return _configDirectoryEnvironmentOverride ?? _installationDirectory;
        if (_isMacOS)
            return Path.Combine(RequireHome(), "Library", "Application Support", "rpcs3");
        return _xdgConfigHome is null
            ? Path.Combine(RequireHome(), ".config", "rpcs3")
            : Path.Combine(_xdgConfigHome, "rpcs3");
    }

    // RPCS3 keeps vfs.yml in a config/ subdirectory on Windows and directly in the configuration
    // directory elsewhere. Accept either, so a portable install authored on one platform still
    // resolves when the same drive is read on the other.
    private string? FindVfsFile(string configDirectory)
    {
        var inConfigSubdirectory = Path.Combine(configDirectory, ConfigSubdirectory, VfsFileName);
        var inRoot = Path.Combine(configDirectory, VfsFileName);
        return (_isWindows ? new[] { inConfigSubdirectory, inRoot } : [inRoot, inConfigSubdirectory])
            .FirstOrDefault(File.Exists);
    }

    private bool HasVfsFile(string configDirectory) => FindVfsFile(configDirectory) is not null;

    private static IReadOnlyList<Rpcs3Profile> ReadProfiles(string homeDirectory)
    {
        if (!Directory.Exists(homeDirectory))
            return [];

        var profiles = new List<Rpcs3Profile>();
        foreach (var directory in Directory.EnumerateDirectories(homeDirectory)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var id = Path.GetFileName(directory);
            if (!IsProfileId(id))
                continue;

            var saveData = Path.Combine(directory, SaveDataDirectoryName);
            var hasSaveData = Directory.Exists(saveData) &&
                Directory.EnumerateDirectories(saveData).Any(save =>
                    File.Exists(Path.Combine(save, SaveParametersFileName)));
            profiles.Add(new Rpcs3Profile(id, ReadUserName(directory), hasSaveData));
        }

        return profiles;
    }

    // Binding is unambiguous while exactly one account can be meant: the only account, or the only
    // one holding saves. When several accounts hold saves, guessing would sync the wrong person's
    // saves, so this fails closed and asks for the account folder instead.
    private static Rpcs3Profile SelectProfile(IReadOnlyList<Rpcs3Profile> profiles, string homeDirectory)
    {
        if (profiles.Count == 0)
            return new Rpcs3Profile("00000001", null, HasSaveData: false);
        if (profiles.Count == 1)
            return profiles[0];

        var populated = profiles.Where(profile => profile.HasSaveData).ToArray();
        if (populated.Length == 1)
            return populated[0];
        if (populated.Length == 0)
        {
            return profiles.FirstOrDefault(profile => profile.Id == "00000001") ?? profiles[0];
        }

        throw new Rpcs3ConfigurationFormatException(
            $"RPCS3 has {populated.Length} user accounts with saves under {homeDirectory}. " +
            "Choose the account folder to sync in this platform's save location.");
    }

    private static string? ReadUserName(string? profileDirectory)
    {
        if (profileDirectory is null)
            return null;

        try
        {
            var path = Path.Combine(profileDirectory, UserNameFileName);
            if (!File.Exists(path))
                return null;

            var name = File.ReadAllText(path).Trim();
            return name.Length is 0 or > 64 ? null : name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private string RequireHome() =>
        string.IsNullOrWhiteSpace(_homeDirectory)
            ? throw new Rpcs3ConfigurationFormatException("The home directory could not be resolved on this system.")
            : _homeDirectory;

    private static string ResolvePath(
        string configDirectory,
        string baseDirectory,
        string configuredPath,
        string description)
    {
        if (configuredPath.Contains('\0'))
            throw new Rpcs3ConfigurationFormatException($"RPCS3's {description} is not a supported path.");

        var expanded = configuredPath.Replace(EmulatorDirectoryMacro, baseDirectory + Path.DirectorySeparatorChar);
        if (expanded.Contains("$(", StringComparison.Ordinal))
        {
            throw new Rpcs3ConfigurationFormatException(
                $"RPCS3's {description} uses a placeholder EmuShelf does not support.");
        }

        try
        {
            var normalized = expanded
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var resolved = Path.IsPathFullyQualified(normalized)
                ? normalized
                : Path.Combine(configDirectory, normalized);
            return Path.GetFullPath(resolved);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new Rpcs3ConfigurationFormatException($"RPCS3's {description} is not a supported path.", ex);
        }
    }

    private static string? FullPathOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static string? AbsoluteOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ? null : Path.GetFullPath(path);

    private static bool IsProfileId(string value) =>
        value.Length == 8 && value.All(char.IsAsciiDigit);

    // PS1/PS2 Classics cards are .VM1/.VM2 files written by the console's virtual memory-card
    // service. Nothing else in that directory is a card.
    private static bool IsVirtualMemoryCardName(string value) =>
        value.EndsWith(".VM1", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".VM2", StringComparison.OrdinalIgnoreCase);

    // RPCS3 save directories are the console's own <TITLEID><suffix> names, trophy sets are NPWR
    // communication ids, and cards are <serial>_mcN.VMx. Keep the accepted shape narrow enough that
    // a remote id can only ever address a direct child of the directory it belongs to.
    private static bool IsSafeEntryName(string value) =>
        value.Length is >= 1 and <= 128 &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private sealed record OverrideSelection(
        string? SaveDataDirectory = null,
        string? HddDirectory = null,
        string? ConfigDirectory = null);

    private sealed record Rpcs3VfsConfiguration(string? EmulatorDirectory, string? Hdd0Directory);

    /// <summary>
    /// Reads only the two top-level scalar keys EmuShelf needs from RPCS3's vfs.yml. Nested blocks
    /// (the <c>/dev_usb***/</c> map) and every other device are ignored rather than interpreted, so
    /// a future device entry cannot change how the PS3 hard disk is located.
    /// </summary>
    private static class Rpcs3VfsAdapter
    {
        public static Rpcs3VfsConfiguration Parse(TextReader reader, CancellationToken cancellationToken)
        {
            string? emulatorDirectory = null;
            string? hdd0 = null;
            while (reader.ReadLine() is { } rawLine)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only column-0 entries are top-level keys; indented lines belong to a nested map.
                if (rawLine.Length == 0 || char.IsWhiteSpace(rawLine[0]) || rawLine.TrimStart().StartsWith('#'))
                    continue;

                var separator = rawLine.IndexOf(':');
                if (separator <= 0)
                    continue;

                var key = rawLine[..separator].Trim();
                var value = Unquote(rawLine[(separator + 1)..].Trim());
                if (key.Equals(EmulatorDirectoryMacro, StringComparison.Ordinal))
                    emulatorDirectory = value;
                else if (key.Equals("/dev_hdd0/", StringComparison.Ordinal))
                    hdd0 = value;
            }

            return new Rpcs3VfsConfiguration(emulatorDirectory, hdd0);
        }

        private static string Unquote(string value) =>
            value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]
                ? value[1..^1]
                : value;
    }
}

/// <summary>Raised when RPCS3's readable configuration does not identify a supported save layout.</summary>
public sealed class Rpcs3ConfigurationFormatException : SaveProviderConfigurationException
{
    public Rpcs3ConfigurationFormatException(string message) : base(message)
    {
    }

    public Rpcs3ConfigurationFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
