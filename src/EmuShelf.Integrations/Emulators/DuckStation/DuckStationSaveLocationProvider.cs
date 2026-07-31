using System.Text;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.DuckStation;

/// <summary>Detected DuckStation memory-card location and any portability-sensitive layout traits.</summary>
public sealed record DuckStationMemoryCardInfo(string Directory, bool UsesFileTitleCards);

/// <summary>
/// Locates DuckStation's user directory and reads its memory-card settings without modifying them.
/// Shared cards are exposed by slot; per-game cards are exposed as individual files. Save states
/// live outside the configured memory-card directory and are never enumerated.
/// </summary>
public sealed class DuckStationSaveLocationProvider : ISaveLocationProvider
{
    private const string SettingsFileName = "settings.ini";
    private const string PortableMarkerFileName = "portable.txt";
    private const string MemoryCardsSection = "MemoryCards";
    private const string DefaultCardsDirectoryName = "memcards";

    private readonly string _installationDirectory;
    private readonly string? _userDirectoryOverride;
    private readonly string _homeDirectory;
    private readonly string _localApplicationDataDirectory;
    private readonly string _documentsDirectory;
    private readonly string? _xdgDataHome;
    private readonly bool _isWindows;
    private readonly bool _isMacOS;
    private readonly bool _isFlatpak;

    public DuckStationSaveLocationProvider(
        string installationDirectory,
        string? userDirectoryOverride = null,
        string? homeDirectory = null,
        string? localApplicationDataDirectory = null,
        string? documentsDirectory = null,
        string? xdgDataHome = null,
        bool? isWindows = null,
        bool? isMacOS = null,
        bool isFlatpak = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        _installationDirectory = Path.GetFullPath(installationDirectory);
        _userDirectoryOverride = string.IsNullOrWhiteSpace(userDirectoryOverride)
            ? null
            : Path.GetFullPath(userDirectoryOverride);
        _homeDirectory = FullPathOrEnvironment(homeDirectory, Environment.SpecialFolder.UserProfile);
        _localApplicationDataDirectory = FullPathOrEnvironment(
            localApplicationDataDirectory,
            Environment.SpecialFolder.LocalApplicationData);
        _documentsDirectory = FullPathOrEnvironment(documentsDirectory, Environment.SpecialFolder.MyDocuments);
        // DuckStation roots its Linux user directory at XDG_DATA_HOME (falling back to
        // ~/.local/share), and ignores the variable unless it is an absolute path.
        var configuredXdgHome = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            : xdgDataHome;
        _xdgDataHome = string.IsNullOrWhiteSpace(configuredXdgHome) ||
                       !Path.IsPathFullyQualified(configuredXdgHome)
            ? null
            : Path.GetFullPath(configuredXdgHome);
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
        _isMacOS = isMacOS ?? OperatingSystem.IsMacOS();
        _isFlatpak = isFlatpak;
    }

    public string SystemId => "playstation";

    public string UnitIdPrefix => "duckstation/";

    /// <summary>Returns DuckStation's effective user-data root.</summary>
    public Task<string> GetUserDirectoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ResolveUserDirectory();
        }, cancellationToken);

    /// <summary>Returns the directory explicitly selected by DuckStation for memory cards.</summary>
    public Task<string> GetMemoryCardsDirectoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadConfiguration(cancellationToken).MemoryCardsDirectory, cancellationToken);

    /// <summary>
    /// Returns the configured directory and whether any active slot derives card names from the
    /// launched file. File-title cards are safe to sync by exact filename, but another machine must
    /// use the same game filename for DuckStation to select the downloaded card automatically.
    /// </summary>
    public Task<DuckStationMemoryCardInfo> GetMemoryCardInfoAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var configuration = ReadConfiguration(cancellationToken);
            return new DuckStationMemoryCardInfo(
                configuration.MemoryCardsDirectory,
                configuration.Slots.Any(slot => slot.Type == MemoryCardType.PerGameFileTitle));
        }, cancellationToken);

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;

        var configuration = ReadConfiguration(CancellationToken.None);
        var localId = unitId[UnitIdPrefix.Length..];
        if (localId.StartsWith("shared/card", StringComparison.Ordinal))
        {
            if (localId.Length != "shared/card1".Length || localId[^1] is not ('1' or '2'))
                return null;

            var slot = configuration.Slots[localId[^1] - '1'];
            if (slot.Type != MemoryCardType.Shared || slot.Path is null)
                return null;

            return new SaveUnitLocation(slot.Path, Path.GetDirectoryName(slot.Path)!, SaveUnitKind.File);
        }

        const string perGamePrefix = "per-game/";
        if (!localId.StartsWith(perGamePrefix, StringComparison.Ordinal))
            return null;

        var perGameSegments = localId[perGamePrefix.Length..].Split('/', 2, StringSplitOptions.None);
        if (perGameSegments.Length != 2 ||
            !TryParsePerGameScheme(perGameSegments[0], out var requestedType))
        {
            return null;
        }

        var fileName = perGameSegments[1];
        if (!IsSafeCardFileName(fileName) ||
            !TryGetPerGameSlot(fileName, configuration.Slots, out var matchedSlot) ||
            matchedSlot?.Type != requestedType)
        {
            return null;
        }

        return new SaveUnitLocation(
            Path.Combine(configuration.MemoryCardsDirectory, fileName),
            configuration.MemoryCardsDirectory,
            SaveUnitKind.File);
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(cancellationToken);
        var units = new List<SaveUnit>();

        foreach (var slot in configuration.Slots.Where(slot => slot.Type == MemoryCardType.Shared))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slot.Path is not null && File.Exists(slot.Path))
            {
                units.Add(new SaveUnit(
                    $"{UnitIdPrefix}shared/card{slot.Number}",
                    $"Shared memory card {slot.Number} (used by every game)",
                    SaveUnitKind.File));
            }
        }

        if (Directory.Exists(configuration.MemoryCardsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(configuration.MemoryCardsDirectory, "*.mcd")
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileName = Path.GetFileName(path);
                if (TryGetPerGameSlot(fileName, configuration.Slots, out var slot) && slot is not null)
                {
                    units.Add(new SaveUnit(
                        UnitIdPrefix + "per-game/" + GetPerGameScheme(slot.Type) + "/" + fileName,
                        fileName,
                        SaveUnitKind.File));
                }
            }
        }

        return units;
    }

    private DuckStationConfiguration ReadConfiguration(CancellationToken cancellationToken)
    {
        var userDirectory = ResolveUserDirectory();
        var settingsPath = Path.Combine(userDirectory, SettingsFileName);
        try
        {
            using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return DuckStationIniAdapter.Parse(reader, userDirectory, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DuckStationConfigurationFormatException("DuckStation's settings.ini could not be read.", ex);
        }
    }

    private string ResolveUserDirectory()
    {
        if (_userDirectoryOverride is not null)
            return RequireSettings(_userDirectoryOverride, "The selected DuckStation user directory");

        // DuckStation treats either portable.txt or an existing settings.ini beside the
        // executable as portable mode. The latter matters for older/unmarked portable installs.
        if (!_isFlatpak &&
            (File.Exists(Path.Combine(_installationDirectory, PortableMarkerFileName)) ||
             File.Exists(Path.Combine(_installationDirectory, SettingsFileName))))
        {
            return RequireSettings(_installationDirectory, "DuckStation's portable directory");
        }

        if (_isFlatpak)
        {
            var flatpakHome = RequireResolvedRoot(_homeDirectory, "The home directory");
            // Inside the sandbox XDG_DATA_HOME is ~/.var/app/<id>/data, so DuckStation's Flatpak
            // user directory lands under data/. config/ is only kept as a fallback for profiles
            // moved there by hand or by older third-party layouts.
            var current = Path.Combine(
                flatpakHome, ".var", "app", "org.duckstation.DuckStation", "data", "duckstation");
            var legacy = Path.Combine(
                flatpakHome, ".var", "app", "org.duckstation.DuckStation", "config", "duckstation");
            if (Directory.Exists(current))
                return RequireSettings(current, "DuckStation's Flatpak user directory");
            if (Directory.Exists(legacy))
                return RequireSettings(legacy, "DuckStation's legacy Flatpak user directory");
            return RequireSettings(current, "DuckStation's Flatpak user directory");
        }

        if (_isWindows)
        {
            // Current DuckStation intentionally retains the legacy Documents data root whenever
            // that directory exists, and only otherwise selects LocalAppData. Checking for a
            // settings file instead of the directory would silently switch profiles before
            // DuckStation has had a chance to create/repair that file.
            if (!string.IsNullOrWhiteSpace(_documentsDirectory))
            {
                var legacy = Path.Combine(_documentsDirectory, "DuckStation");
                if (Directory.Exists(legacy))
                    return RequireSettings(legacy, "DuckStation's legacy user directory");
            }

            return RequireSettings(
                Path.Combine(
                    RequireResolvedRoot(
                        _localApplicationDataDirectory,
                        "DuckStation's application data directory"),
                    "DuckStation"),
                "DuckStation's user directory");
        }

        if (_isMacOS)
        {
            return RequireSettings(
                Path.Combine(
                    RequireResolvedRoot(_homeDirectory, "The home directory"),
                    "Library",
                    "Application Support",
                    "DuckStation"),
                "DuckStation's user directory");
        }

        var dataRoot = _xdgDataHome is null
            ? Path.Combine(
                RequireResolvedRoot(_homeDirectory, "The home directory"),
                ".local",
                "share",
                "duckstation")
            : Path.Combine(_xdgDataHome, "duckstation");
        return RequireSettings(dataRoot, "DuckStation's user directory");
    }

    private static string RequireSettings(string directory, string description)
    {
        if (!File.Exists(Path.Combine(directory, SettingsFileName)))
        {
            throw new DuckStationConfigurationFormatException(
                $"{description} does not contain a readable {SettingsFileName}.");
        }

        return directory;
    }

    private static bool TryGetPerGameSlot(
        string fileName,
        IReadOnlyList<MemoryCardSlot> slots,
        out MemoryCardSlot? matchedSlot)
    {
        matchedSlot = null;
        if (!IsSafeCardFileName(fileName))
            return false;

        foreach (var slot in slots.Where(slot => IsPerGame(slot.Type)))
        {
            var suffix = $"_{slot.Number}.mcd";
            if (!fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            var identity = fileName[..^suffix.Length];
            if (identity.Length == 0 ||
                identity.Equals("shared_card", StringComparison.OrdinalIgnoreCase) ||
                (slot.Type == MemoryCardType.PerGame && !IsPlayStationSerial(identity)))
            {
                continue;
            }

            matchedSlot = slot;
            return true;
        }

        return false;
    }

    private static bool IsSafeCardFileName(string value) =>
        value.Length is >= 6 and <= 255 &&
        value.EndsWith(".mcd", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains('/') &&
        !value.Contains('\\') &&
        !value.Contains('\0');

    private static bool IsPlayStationSerial(string value) =>
        value.Length == 10 &&
        value[..4].All(char.IsAsciiLetter) &&
        value[4] == '-' &&
        value[5..].All(char.IsAsciiDigit);

    private static string GetPerGameScheme(MemoryCardType type) => type switch
    {
        MemoryCardType.PerGame => "serial",
        MemoryCardType.PerGameTitle => "title",
        MemoryCardType.PerGameFileTitle => "file-title",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The card type is not per-game."),
    };

    private static bool TryParsePerGameScheme(string value, out MemoryCardType type)
    {
        type = value switch
        {
            "serial" => MemoryCardType.PerGame,
            "title" => MemoryCardType.PerGameTitle,
            "file-title" => MemoryCardType.PerGameFileTitle,
            _ => MemoryCardType.None,
        };
        return type != MemoryCardType.None;
    }

    // Environment.GetFolderPath returns an empty string for a folder the current platform cannot
    // resolve: MyDocuments and LocalApplicationData are empty on a Linux session without XDG
    // user-dirs, which is the normal shape of a Steam Deck Game Mode session. Path.GetFullPath
    // rejects an empty string, so keep the value unresolved here and let the platform branch that
    // actually consumes it fail with a readable message, instead of throwing while merely
    // constructing the provider on a platform that never reads that folder.
    private static string FullPathOrEnvironment(string? path, Environment.SpecialFolder fallback)
    {
        var resolved = string.IsNullOrWhiteSpace(path) ? Environment.GetFolderPath(fallback) : path;
        return string.IsNullOrWhiteSpace(resolved) ? string.Empty : Path.GetFullPath(resolved);
    }

    private static string RequireResolvedRoot(string directory, string description) =>
        string.IsNullOrWhiteSpace(directory)
            ? throw new DuckStationConfigurationFormatException(
                $"{description} could not be resolved on this system.")
            : directory;

    private sealed record DuckStationConfiguration(
        string MemoryCardsDirectory,
        IReadOnlyList<MemoryCardSlot> Slots);

    private sealed record MemoryCardSlot(int Number, MemoryCardType Type, string? Path);

    private enum MemoryCardType
    {
        None,
        Shared,
        PerGame,
        PerGameTitle,
        PerGameFileTitle,
        NonPersistent,
    }

    // DuckStation's own defaults for an unwritten [MemoryCards] entry: slot 1 keeps a card per game
    // title, slot 2 is disabled.
    private static MemoryCardType DefaultCardType(int number) =>
        number == 1 ? MemoryCardType.PerGameTitle : MemoryCardType.None;

    private static bool IsPerGame(MemoryCardType type) =>
        type is MemoryCardType.PerGame or MemoryCardType.PerGameTitle or MemoryCardType.PerGameFileTitle;

    private static class DuckStationIniAdapter
    {
        public static DuckStationConfiguration Parse(
            TextReader reader,
            string userDirectory,
            CancellationToken cancellationToken)
        {
            // DuckStation only writes settings that differ from its defaults, so a stock install has
            // no [MemoryCards] section at all. An absent key therefore means "the emulator default",
            // not "unknown layout"; only a key DuckStation itself would not accept fails closed.
            var memoryCards = ReadMemoryCardSettings(reader, cancellationToken);
            var configuredDirectory = memoryCards.GetValueOrDefault("Directory");
            var cardsDirectory = ResolvePath(
                userDirectory,
                string.IsNullOrWhiteSpace(configuredDirectory) ? DefaultCardsDirectoryName : configuredDirectory,
                "memory-card directory");
            var slots = new List<MemoryCardSlot>(2);
            for (var number = 1; number <= 2; number++)
            {
                var rawType = memoryCards.GetValueOrDefault($"Card{number}Type");
                MemoryCardType type;
                if (string.IsNullOrWhiteSpace(rawType))
                {
                    type = DefaultCardType(number);
                }
                else if (!Enum.TryParse(rawType, ignoreCase: true, out type) || !Enum.IsDefined(type))
                {
                    throw new DuckStationConfigurationFormatException(
                        $"DuckStation's settings.ini has no supported Card{number}Type.");
                }
                string? cardPath = null;
                if (type == MemoryCardType.Shared)
                {
                    var configuredPath = memoryCards.GetValueOrDefault($"Card{number}Path");
                    cardPath = string.IsNullOrWhiteSpace(configuredPath)
                        ? Path.Combine(cardsDirectory, $"shared_card_{number}.mcd")
                        : ResolvePath(cardsDirectory, configuredPath, $"card {number} path");
                }

                slots.Add(new MemoryCardSlot(number, type, cardPath));
            }

            var duplicateSharedPath = slots
                .Where(slot => slot.Path is not null)
                .GroupBy(slot => slot.Path!, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateSharedPath is not null)
            {
                throw new DuckStationConfigurationFormatException(
                    "DuckStation's enabled shared card slots resolve to the same file.");
            }

            foreach (var sharedSlot in slots.Where(slot => slot.Path is not null))
            {
                var sharedDirectory = Path.GetDirectoryName(sharedSlot.Path!);
                var sharedName = Path.GetFileName(sharedSlot.Path!);
                if (string.Equals(sharedDirectory, cardsDirectory, StringComparison.OrdinalIgnoreCase) &&
                    TryGetPerGameSlot(sharedName, slots, out _))
                {
                    throw new DuckStationConfigurationFormatException(
                        "DuckStation's shared card path is indistinguishable from an enabled per-game card.");
                }
            }

            return new DuckStationConfiguration(cardsDirectory, slots);
        }

        private static Dictionary<string, string> ReadMemoryCardSettings(
            TextReader reader,
            CancellationToken cancellationToken)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    section = line[1..^1].Trim();
                    continue;
                }

                if (!string.Equals(section, MemoryCardsSection, StringComparison.OrdinalIgnoreCase))
                    continue;

                var equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    throw new DuckStationConfigurationFormatException(
                        $"DuckStation's settings.ini has an unsupported memory-card line at {lineNumber}.");
                }

                var key = line[..equals].Trim();
                var value = line[(equals + 1)..].Trim();
                if (key.Length == 0 || !values.TryAdd(key, value))
                {
                    throw new DuckStationConfigurationFormatException(
                        $"DuckStation's settings.ini has a duplicate or empty memory-card key at {lineNumber}.");
                }
            }

            return values;
        }

        private static string ResolvePath(string baseDirectory, string configuredPath, string description)
        {
            if (configuredPath.Contains('\0') || configuredPath.Contains('\r') || configuredPath.Contains('\n'))
                throw new DuckStationConfigurationFormatException($"DuckStation's {description} is not a supported path.");

            try
            {
                var normalized = configuredPath.Trim()
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var resolved = Path.IsPathFullyQualified(normalized)
                    ? normalized
                    : Path.Combine(baseDirectory, normalized);
                return Path.GetFullPath(resolved);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new DuckStationConfigurationFormatException(
                    $"DuckStation's {description} is not a supported path.", ex);
            }
        }
    }
}

/// <summary>Raised when DuckStation's settings do not identify a supported memory-card layout.</summary>
public sealed class DuckStationConfigurationFormatException : SaveProviderConfigurationException
{
    public DuckStationConfigurationFormatException(string message) : base(message)
    {
    }

    public DuckStationConfigurationFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
