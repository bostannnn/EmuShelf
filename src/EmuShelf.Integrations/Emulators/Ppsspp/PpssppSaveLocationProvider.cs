using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>
/// Locates PPSSPP's emulated Memory Stick without modifying it and exposes each immediate
/// <c>PSP/SAVEDATA</c> child as one complete save unit. Save states live elsewhere and are never
/// enumerated.
/// </summary>
public sealed class PpssppSaveLocationProvider : ISaveLocationProvider
{
    private const string InstalledFileName = "installed.txt";
    private readonly string _installationDirectory;
    private readonly string? _memoryStickDirectoryOverride;
    private readonly string _homeDirectory;
    private readonly string _documentsDirectory;
    private readonly bool _isWindows;
    private readonly bool _isMacOS;
    private readonly bool _isFlatpak;

    public PpssppSaveLocationProvider(
        string installationDirectory,
        string? memoryStickDirectoryOverride = null,
        string? homeDirectory = null,
        string? documentsDirectory = null,
        bool? isWindows = null,
        bool isFlatpak = false,
        bool? isMacOS = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        _installationDirectory = Path.GetFullPath(installationDirectory);
        _memoryStickDirectoryOverride = string.IsNullOrWhiteSpace(memoryStickDirectoryOverride)
            ? null
            : Path.GetFullPath(memoryStickDirectoryOverride);
        _homeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(homeDirectory);
        _documentsDirectory = string.IsNullOrWhiteSpace(documentsDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetFullPath(documentsDirectory);
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
        _isMacOS = isMacOS ?? OperatingSystem.IsMacOS();
        _isFlatpak = isFlatpak;
    }

    public string SystemId => "psp";

    public string UnitIdPrefix => "ppsspp/";

    public Task<string> GetMemoryStickDirectoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => GetMemoryStickDirectory(cancellationToken), cancellationToken);

    public async Task<string> GetSaveDataDirectoryAsync(CancellationToken cancellationToken = default) =>
        Path.Combine(await GetMemoryStickDirectoryAsync(cancellationToken), "PSP", "SAVEDATA");

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;

        var saveName = unitId[UnitIdPrefix.Length..];
        if (!IsSafeSaveName(saveName))
            return null;

        var saveDataDirectory = Path.Combine(GetMemoryStickDirectory(CancellationToken.None), "PSP", "SAVEDATA");
        return new SaveUnitLocation(
            Path.Combine(saveDataDirectory, saveName),
            saveDataDirectory,
            SaveUnitKind.Folder);
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        var saveDataDirectory = Path.Combine(GetMemoryStickDirectory(cancellationToken), "PSP", "SAVEDATA");
        if (!Directory.Exists(saveDataDirectory))
            return [];

        var units = new List<SaveUnit>();
        foreach (var saveDirectory in Directory.EnumerateDirectories(saveDataDirectory)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var saveName = Path.GetFileName(saveDirectory);
            if (IsSafeSaveName(saveName))
            {
                units.Add(new SaveUnit(
                    UnitIdPrefix + saveName,
                    saveName,
                    SaveUnitKind.Folder));
            }
        }

        return units;
    }

    private string GetMemoryStickDirectory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_memoryStickDirectoryOverride is not null)
            return NormalizeMemoryStickDirectory(_memoryStickDirectoryOverride);

        if (!_isWindows)
        {
            return GetDefaultMemoryStickDirectory(
                _installationDirectory, _homeDirectory, _documentsDirectory, false, _isFlatpak, _isMacOS);
        }

        var installedPath = Path.Combine(_installationDirectory, InstalledFileName);
        if (!File.Exists(installedPath))
            return Path.Combine(_installationDirectory, "memstick");

        string configuredPath;
        try
        {
            using var stream = new FileStream(installedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            configuredPath = reader.ReadToEnd().Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PpssppConfigurationFormatException("PPSSPP's installed.txt could not be read.", ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (configuredPath.Length == 0)
            return Path.Combine(_documentsDirectory, "PPSSPP");
        if (configuredPath.Contains('\0') || configuredPath.Contains('\r') || configuredPath.Contains('\n'))
            throw new PpssppConfigurationFormatException("PPSSPP's installed.txt does not contain one supported path.");

        try
        {
            var normalized = configuredPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var resolved = Path.IsPathFullyQualified(normalized)
                ? normalized
                : Path.Combine(_installationDirectory, normalized);
            return NormalizeMemoryStickDirectory(Path.GetFullPath(resolved));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new PpssppConfigurationFormatException("PPSSPP's installed.txt contains an unsupported path.", ex);
        }
    }

    internal static string GetDefaultMemoryStickDirectory(
        string installationDirectory,
        string homeDirectory,
        string documentsDirectory,
        bool isWindows,
        bool isFlatpak,
        bool isMacOS = false)
    {
        if (isWindows)
            return Path.Combine(installationDirectory, "memstick");
        if (isFlatpak)
        {
            return Path.Combine(
                homeDirectory,
                ".var",
                "app",
                "org.ppsspp.PPSSPP",
                "config",
                "ppsspp");
        }

        // macOS PPSSPP keeps its Memory Stick under Application Support, which is where the texture
        // resolver already looks for the same installation's ppsspp.ini.
        if (isMacOS)
            return Path.Combine(homeDirectory, "Library", "Application Support", "PPSSPP");

        return Path.Combine(homeDirectory, ".config", "ppsspp");
    }

    // PPSSPP may let the user choose the PSP directory itself. Normalize that presentation to
    // the Memory Stick root so every caller appends PSP/SAVEDATA exactly once.
    private static string NormalizeMemoryStickDirectory(string path) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(path)).Equals("PSP", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? path
            : path;

    private static bool IsSafeSaveName(string value) =>
        value.Length is >= 1 and <= 128 &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
}

/// <summary>Raised when PPSSPP's readable Memory Stick selector has an unsupported shape.</summary>
public sealed class PpssppConfigurationFormatException : SaveProviderConfigurationException
{
    public PpssppConfigurationFormatException(string message) : base(message)
    {
    }

    public PpssppConfigurationFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
