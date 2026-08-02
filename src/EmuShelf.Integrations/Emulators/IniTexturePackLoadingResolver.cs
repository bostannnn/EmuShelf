using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// Resolves replacement-texture loading from a versioned INI setting, and refuses to answer when a
/// per-game configuration file exists for the game being asked about.
/// </summary>
/// <remarks>
/// The per-game refusal is the whole point of this adapter. These emulators layer a per-game file
/// over the global setting, and the layering rules differ per emulator and per version. Reporting
/// the global value while a per-game file sits on top of it would be exactly the confident wrong
/// answer the milestone forbids, so the presence of that file yields <c>Unknown</c>.
/// </remarks>
public abstract class IniTexturePackLoadingResolver : ITexturePackLoadingResolver
{
    private readonly string _configurationDirectory;
    private readonly IReadOnlyList<string> _relativeIniPaths;
    private readonly string? _versionSection;
    private readonly string? _versionKey;
    private readonly string? _supportedVersion;
    private readonly string _settingSection;
    private readonly IReadOnlyList<string> _settingKeys;
    private readonly string? _perGameDirectory;

    protected IniTexturePackLoadingResolver(
        string emulatorId,
        string installationId,
        string configurationDirectory,
        IReadOnlyList<string> relativeIniPaths,
        string settingSection,
        IReadOnlyList<string> settingKeys,
        string? versionSection = null,
        string? versionKey = null,
        string? supportedVersion = null,
        string? perGameDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationDirectory);
        EmulatorId = emulatorId;
        InstallationId = installationId;
        _configurationDirectory = Path.GetFullPath(configurationDirectory);
        _relativeIniPaths = relativeIniPaths;
        _settingSection = settingSection;
        _settingKeys = settingKeys;
        _versionSection = versionSection;
        _versionKey = versionKey;
        _supportedVersion = supportedVersion;
        _perGameDirectory = perGameDirectory;
    }

    public string EmulatorId { get; }

    public string InstallationId { get; }

    public Task<TexturePackLoadingResolution> ResolveAsync(
        string? gameKey = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Resolve(gameKey, cancellationToken), cancellationToken);

    private TexturePackLoadingResolution Resolve(string? gameKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (gameKey is not null && HasPerGameConfiguration(gameKey))
        {
            return TexturePackLoadingResolution.Unknown(
                "This game has its own emulator configuration, which can override the global replacement setting.");
        }

        var iniPath = _relativeIniPaths
            .Select(relative => Path.Combine(_configurationDirectory, relative))
            .FirstOrDefault(File.Exists);
        if (iniPath is null)
            return TexturePackLoadingResolution.Unknown("The emulator settings file was not found.");

        var ini = EmulatorIniFile.TryRead(iniPath, out var diagnostic, cancellationToken);
        if (ini is null)
            return TexturePackLoadingResolution.Unknown($"The emulator settings file could not be read safely: {diagnostic}");

        if (_versionSection is not null && _versionKey is not null && _supportedVersion is not null &&
            !ini.HasVersion(_versionSection, _versionKey, _supportedVersion))
        {
            return TexturePackLoadingResolution.Unknown(
                $"The settings file is not the supported {_versionKey} {_supportedVersion} format.");
        }

        // Several emulators split replacement loading across more than one switch. Loading counts as
        // on when any recognized switch is on, and as off only when every one of them is present and
        // off; a missing or unrecognized spelling leaves the answer unknown.
        var sawSetting = false;
        foreach (var key in _settingKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ini.TryGetBoolean(_settingSection, key, out var enabled))
                continue;
            sawSetting = true;
            if (enabled)
                return new TexturePackLoadingResolution(TexturePackLoadingStatus.Enabled);
        }

        return sawSetting
            ? new TexturePackLoadingResolution(TexturePackLoadingStatus.Disabled)
            : TexturePackLoadingResolution.Unknown(
                "The replacement-texture setting is absent or written in an unrecognized form.");
    }

    private bool HasPerGameConfiguration(string gameKey)
    {
        if (_perGameDirectory is null)
            return false;

        var directory = Path.Combine(_configurationDirectory, _perGameDirectory);
        try
        {
            if (!Directory.Exists(directory))
                return false;

            // Emulators name these after the identifier with assorted suffixes
            // (`SLUS-12345.ini`, `SLUS-12345_Some Title.ini`), so match on the prefix.
            return Directory.EnumerateFiles(directory, "*.ini")
                .Any(file => Path.GetFileNameWithoutExtension(file)
                    .StartsWith(gameKey, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable per-game directory means precedence cannot be ruled out either.
            return true;
        }
    }
}
