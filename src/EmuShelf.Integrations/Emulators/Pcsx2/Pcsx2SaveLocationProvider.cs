using System.Text;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.Pcsx2;

public sealed record Pcsx2ContentDirectories(string Cheats, string Patches, string SaveStates);

/// <summary>
/// Reads PCSX2's version-1 INI format without modifying it and exposes each memory-card save as
/// an independently addressable sync unit. Unknown configuration formats fail closed so a future
/// PCSX2 format change cannot make EmuShelf guess at a save location.
/// </summary>
public sealed class Pcsx2SaveLocationProvider : ISaveLocationProvider
{
    private const string IniFileName = "PCSX2.ini";
    private const string IniSubdirectory = "inis";
    private const string FolderIndexFileName = "_pcsx2_index";

    private readonly string _configurationDirectory;
    private readonly string _fallbackMemoryCardsDirectory;

    public Pcsx2SaveLocationProvider(string configurationDirectory, string? homeDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        _configurationDirectory = Path.GetFullPath(configurationDirectory);
        _fallbackMemoryCardsDirectory = GetDefaultMemoryCardsDirectory(homeDirectory, OperatingSystem.IsWindows());
    }

    public string SystemId => "playstation2";

    public string UnitIdPrefix => "pcsx2/";

    /// <summary>Configured roots for portable content stored outside the memory cards folder.</summary>
    public Task<Pcsx2ContentDirectories> GetContentDirectoriesAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadContentDirectories(cancellationToken), cancellationToken);

    /// <summary>Gets the configured memory-card location, or the platform default when the INI is unreadable.</summary>
    public async Task<string> GetMemoryCardsDirectoryAsync(CancellationToken cancellationToken = default) =>
        await Task.Run(() => ReadConfiguration(cancellationToken).MemoryCardsDirectory, cancellationToken);

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;

        var segments = unitId[UnitIdPrefix.Length..].Split('/', StringSplitOptions.None);
        if (segments.Length is < 1 or > 2 || segments.Any(segment => !IsSafeCardName(segment)))
            return null;

        var configuration = ReadConfiguration(CancellationToken.None);
        var cardName = segments[0];
        if (!configuration.EnumerateAllCards && !configuration.EnabledCardNames.Contains(cardName))
            return null;

        var cardPath = Path.GetFullPath(Path.Combine(configuration.MemoryCardsDirectory, cardName));
        if (segments.Length == 1)
        {
            if (!cardName.EndsWith(".ps2", StringComparison.OrdinalIgnoreCase) || Directory.Exists(cardPath))
                return null;

            // With a readable INI an enabled but not-yet-created file card is a safe destination.
            // In heuristic fallback mode, require the card to exist rather than allowing an
            // arbitrary remote id to manufacture a new card.
            if (configuration.EnumerateAllCards && !File.Exists(cardPath))
                return null;
            return new SaveUnitLocation(cardPath, configuration.MemoryCardsDirectory, SaveUnitKind.File);
        }

        var saveName = segments[1];
        if (!IsSaveDirectory(saveName) || !Directory.Exists(cardPath) ||
            !IsFolderCard(cardPath, configuration))
        {
            return null;
        }

        return new SaveUnitLocation(
            Path.Combine(cardPath, saveName),
            configuration.MemoryCardsDirectory,
            SaveUnitKind.Folder);
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(cancellationToken);
        if (!Directory.Exists(configuration.MemoryCardsDirectory))
            return [];

        // Classify each card from disk rather than the global auto-manage flag: a directory is a
        // folder card (one unit per save subfolder); a *.ps2 file is a file card. A folder card
        // need not carry a root _pcsx2_index — newer PCSX2 stores per-save subdirectories directly.
        var units = new List<SaveUnit>();
        foreach (var entry in Directory
            .EnumerateFileSystemEntries(configuration.MemoryCardsDirectory)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cardName = Path.GetFileName(entry);
            if (!IsSafeCardName(cardName) ||
                (!configuration.EnumerateAllCards && !configuration.EnabledCardNames.Contains(cardName)))
            {
                continue;
            }

            if (IsFolderCard(entry, configuration))
            {
                foreach (var saveDirectory in Directory.EnumerateDirectories(entry).OrderBy(path => path, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var saveName = Path.GetFileName(saveDirectory);
                    if (IsSaveDirectory(saveName))
                    {
                        units.Add(new SaveUnit(
                            $"pcsx2/{cardName}/{saveName}",
                            $"{cardName} — {saveName}",
                            SaveUnitKind.Folder));
                    }
                }
            }
            else if (File.Exists(entry) && Path.GetExtension(cardName).Equals(".ps2", StringComparison.OrdinalIgnoreCase))
            {
                units.Add(new SaveUnit($"pcsx2/{cardName}", cardName, SaveUnitKind.File));
            }
        }

        return units;
    }

    private Pcsx2Configuration ReadConfiguration(CancellationToken cancellationToken)
    {
        var iniPath = ResolveIniPath();
        if (iniPath is null)
            return ConfigurationWithoutIni();

        try
        {
            using var stream = new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Pcsx2IniV1Adapter.Parse(reader, _configurationDirectory, cancellationToken);
        }
        catch (IOException)
        {
            return ConfigurationWithoutIni();
        }
        catch (UnauthorizedAccessException)
        {
            return ConfigurationWithoutIni();
        }
    }

    private Pcsx2ContentDirectories ReadContentDirectories(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var iniPath = ResolveIniPath();
        if (iniPath is null && IsLikelyMemoryCardsDirectory(_configurationDirectory))
        {
            throw new InvalidOperationException(
                "The selected PCSX2 location is the memory-card folder. Select PCSX2's data folder " +
                "to resolve its cheats, patches, and save-state folders safely.");
        }

        var contentRoot = iniPath is null
            ? Path.GetDirectoryName(_fallbackMemoryCardsDirectory)!
            : _configurationDirectory;
        if (iniPath is not null)
        {
            using var stream = new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var inFolders = false;
            while (reader.ReadLine() is { } line)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trimmed = line.Trim();
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inFolders = trimmed.Equals("[Folders]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inFolders)
                    continue;
                var equals = trimmed.IndexOf('=');
                if (equals > 0)
                    values[trimmed[..equals].Trim()] = trimmed[(equals + 1)..].Trim();
            }
        }

        return new Pcsx2ContentDirectories(
            ResolveFolder(values.GetValueOrDefault("Cheats"), "cheats"),
            ResolveFolder(values.GetValueOrDefault("Patches"), "patches"),
            ResolveFolder(values.GetValueOrDefault("SaveStates"), "sstates"));

        string ResolveFolder(string? configured, string fallback)
        {
            var value = string.IsNullOrWhiteSpace(configured) ? fallback : configured;
            var normalized = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.IsPathFullyQualified(normalized)
                ? normalized
                : Path.Combine(contentRoot, normalized));
        }
    }

    // No readable PCSX2.ini under the selected folder. The user may have pointed us straight at
    // the memory-card folder instead of the PCSX2 install folder — accept that directly; otherwise
    // fall back to the platform default.
    private Pcsx2Configuration ConfigurationWithoutIni()
    {
        if (IsLikelyMemoryCardsDirectory(_configurationDirectory))
        {
            return new Pcsx2Configuration(
                _configurationDirectory,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                EnumerateAllCards: true);
        }

        return Pcsx2Configuration.Fallback(_fallbackMemoryCardsDirectory);
    }

    private static bool IsLikelyMemoryCardsDirectory(string directory) =>
        Directory.Exists(directory) &&
        Directory.EnumerateFileSystemEntries(directory).Any(entry =>
        {
            var name = Path.GetFileName(entry);
            return name.EndsWith(".ps2", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("_pcsx2", StringComparison.Ordinal);
        });

    // PCSX2 Qt keeps PCSX2.ini in an "inis" subfolder of its data directory; older/portable
    // layouts may place it at the root. Try the subfolder first, then the root.
    private string? ResolveIniPath()
    {
        var candidates = new[]
        {
            Path.Combine(_configurationDirectory, IniSubdirectory, IniFileName),
            Path.Combine(_configurationDirectory, IniFileName),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // A folder memory card is a directory. When the INI names the enabled cards, an enabled
    // directory is a folder card; the marker/subdirectory heuristic only guards the fallback
    // (unreadable INI) case where the card names are unknown.
    private static bool IsFolderCard(string entry, Pcsx2Configuration configuration)
    {
        if (!Directory.Exists(entry))
            return false;
        if (!configuration.EnumerateAllCards)
            return true;
        return File.Exists(Path.Combine(entry, FolderIndexFileName)) ||
            Directory.EnumerateDirectories(entry).Any(sub => IsSaveDirectory(Path.GetFileName(sub)));
    }

    internal static string GetDefaultMemoryCardsDirectory(string? homeDirectory, bool isWindows)
    {
        var home = homeDirectory;
        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (isWindows)
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documents, "PCSX2", "memcards");
        }

        return Path.Combine(home!, ".var", "app", "net.pcsx2.PCSX2", "config", "PCSX2", "memcards");
    }

    private static bool IsSafeCardName(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        !value.Contains('/') &&
        !value.Contains('\\');

    private static bool IsGameSerial(string value) =>
        value.Length is >= 4 and <= 32 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    // A per-save subdirectory of a folder card, keyed by the PS2 memory-card directory name
    // (e.g. "BASCUS-97399GodOfWar"). PCSX2's own "_"-prefixed housekeeping entries
    // (_pcsx2_index, _pcsx2_deleted_*) are not saves.
    private static bool IsSaveDirectory(string name) =>
        !name.StartsWith('_') && IsGameSerial(name);

    private sealed record Pcsx2Configuration(
        string MemoryCardsDirectory,
        IReadOnlySet<string> EnabledCardNames,
        bool EnumerateAllCards = false)
    {
        public static Pcsx2Configuration Fallback(string memoryCardsDirectory) =>
            new(memoryCardsDirectory, new HashSet<string>(StringComparer.OrdinalIgnoreCase), EnumerateAllCards: true);
    }

    /// <summary>Strict reader for the PCSX2 INI layout observed in PCSX2 SettingsVersion 1.</summary>
    private static class Pcsx2IniV1Adapter
    {
        public static Pcsx2Configuration Parse(
            TextReader reader,
            string configurationDirectory,
            CancellationToken cancellationToken)
        {
            var values = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string? section = null;
            var lineNumber = 0;
            while (reader.ReadLine() is { } rawLine)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber++;
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    continue;
                if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
                {
                    section = line[1..^1];
                    values.TryAdd(section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                    continue;
                }

                var equals = line.IndexOf('=');
                if (section is null || equals <= 0)
                    throw new Pcsx2ConfigurationFormatException($"PCSX2.ini has an unsupported line at {lineNumber}.");
                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (key.Length == 0 || !values[section].TryAdd(key, value))
                    throw new Pcsx2ConfigurationFormatException($"PCSX2.ini has an unsupported duplicate or empty key at {lineNumber}.");
            }

            if (!TryGet(values, "UI", "SettingsVersion", out var version) || version != "1" ||
                !TryGet(values, "Folders", "MemoryCards", out var memoryCards) ||
                string.IsNullOrWhiteSpace(memoryCards) ||
                !values.TryGetValue("MemoryCards", out var cardSettings))
            {
                throw new Pcsx2ConfigurationFormatException("PCSX2.ini is not the supported SettingsVersion 1 memory-card configuration.");
            }

            // PCSX2 writes the path with the authoring OS's separator; normalize so a Windows
            // ini (backslashes) still resolves when read on Linux/macOS after a portable move.
            var normalizedMemoryCards = memoryCards
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var resolved = Path.IsPathFullyQualified(normalizedMemoryCards)
                ? normalizedMemoryCards
                : Path.Combine(configurationDirectory, normalizedMemoryCards);
            var enabledCardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, enabled) in cardSettings)
            {
                if (!key.EndsWith("_Enable", StringComparison.OrdinalIgnoreCase) || !bool.TryParse(enabled, out var isEnabled) || !isEnabled)
                    continue;

                var filenameKey = key[..^"_Enable".Length] + "_Filename";
                // A file card's filename ends in .ps2; a folder card's may not. Accept either, and
                // record both the raw name and its stem so the on-disk card (file or directory) is
                // matched. Only a missing or unsafe filename is an unsupported entry.
                if (!cardSettings.TryGetValue(filenameKey, out var filename) || string.IsNullOrWhiteSpace(filename))
                    throw new Pcsx2ConfigurationFormatException("PCSX2.ini has an enabled memory-card slot without a filename.");

                var normalizedFilename = Path.GetFileName(filename.Trim());
                if (!IsSafeCardName(normalizedFilename))
                    throw new Pcsx2ConfigurationFormatException("PCSX2.ini has an unsupported enabled memory-card filename.");

                enabledCardNames.Add(normalizedFilename);
                enabledCardNames.Add(Path.GetFileNameWithoutExtension(normalizedFilename));
            }

            return new Pcsx2Configuration(Path.GetFullPath(resolved), enabledCardNames);
        }

        private static bool TryGet(
            IReadOnlyDictionary<string, Dictionary<string, string>> values,
            string section,
            string key,
            out string value)
        {
            value = string.Empty;
            if (!values.TryGetValue(section, out var sectionValues) ||
                !sectionValues.TryGetValue(key, out var foundValue))
            {
                return false;
            }

            value = foundValue;
            return true;
        }
    }
}

/// <summary>Raised when a readable PCSX2 INI does not match the explicitly supported adapter format.</summary>
public sealed class Pcsx2ConfigurationFormatException : SaveProviderConfigurationException
{
    public Pcsx2ConfigurationFormatException(string message) : base(message)
    {
    }
}
