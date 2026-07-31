using System.Text;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators;

/// <summary>Shared strict reader for versioned INI texture-root settings.</summary>
public abstract class IniTextureRootResolver : ITexturePackRootResolver
{
    private readonly string _configurationDirectory;
    private readonly string? _overrideDirectory;
    private readonly IReadOnlyList<string> _relativeIniPaths;
    private readonly string _versionSection;
    private readonly string _supportedVersion;

    protected IniTextureRootResolver(
        string emulatorId,
        string installationId,
        string configurationDirectory,
        string? overrideDirectory,
        IReadOnlyList<string> relativeIniPaths,
        string versionSection,
        string supportedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        EmulatorId = emulatorId;
        InstallationId = installationId;
        _configurationDirectory = Path.GetFullPath(configurationDirectory);
        _overrideDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? null
            : Path.GetFullPath(overrideDirectory);
        _relativeIniPaths = relativeIniPaths;
        _versionSection = versionSection;
        _supportedVersion = supportedVersion;
    }

    public string EmulatorId { get; }

    public string InstallationId { get; }

    public Task<TexturePackRootResolution> ResolveAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Resolve(cancellationToken), cancellationToken);

    private TexturePackRootResolution Resolve(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_overrideDirectory is not null)
            return Resolved(_overrideDirectory);

        var iniPath = _relativeIniPaths
            .Select(relative => Path.Combine(_configurationDirectory, relative))
            .FirstOrDefault(File.Exists);
        if (iniPath is null)
        {
            return new TexturePackRootResolution(
                TexturePackRootResolutionStatus.ConfigurationMissing,
                null,
                "The emulator settings file was not found. Select the texture folder manually.");
        }

        try
        {
            using var stream = new FileStream(iniPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var values = Parse(reader, cancellationToken);
            if (!TryGet(values, _versionSection, "SettingsVersion", out var version) ||
                version != _supportedVersion ||
                !TryGet(values, "Folders", "Textures", out var configuredPath) ||
                string.IsNullOrWhiteSpace(configuredPath))
            {
                return new TexturePackRootResolution(
                    TexturePackRootResolutionStatus.ConfigurationUnsupported,
                    null,
                    $"The settings file is not the supported SettingsVersion {_supportedVersion} texture-folder format.");
            }

            var normalized = configuredPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var resolved = Path.IsPathFullyQualified(normalized)
                ? normalized
                : Path.Combine(_configurationDirectory, normalized);
            return Resolved(Path.GetFullPath(resolved));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new TexturePackRootResolution(
                TexturePackRootResolutionStatus.ConfigurationUnsupported,
                null,
                $"The emulator settings file could not be read safely: {ex.Message}");
        }
    }

    private static Dictionary<string, Dictionary<string, string>> Parse(
        TextReader reader,
        CancellationToken cancellationToken) =>
        EmulatorIniFile.Parse(reader, cancellationToken);

    private static bool TryGet(
        IReadOnlyDictionary<string, Dictionary<string, string>> values,
        string section,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(section, out var sectionValues) ||
            !sectionValues.TryGetValue(key, out var found))
        {
            return false;
        }

        value = found;
        return true;
    }

    protected static TexturePackRootResolution Resolved(string path) =>
        new(TexturePackRootResolutionStatus.Resolved, Path.GetFullPath(path));
}
