using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>
/// Resolves Dolphin's texture directory from its own configuration, an explicit override, or the
/// documented default.
/// </summary>
/// <remarks>
/// Dolphin keeps the Load directory in <c>Config/Dolphin.ini</c> under <c>[General] LoadPath</c>,
/// and its Paths settings let a user move it anywhere — frontends routinely redirect it out of the
/// user directory entirely. Reading that key is the only way to follow the folder when it moves;
/// inferring it from the user directory's layout works only until someone relocates it.
/// An absent key is not an error: Dolphin then uses <c>&lt;User&gt;/Load</c>, so that is the fallback.
/// </remarks>
public sealed class DolphinTextureRootResolver : ITexturePackRootResolver
{
    private const string TexturesDirectoryName = "Textures";
    private readonly string _userDirectory;
    private readonly string? _overrideDirectory;

    public DolphinTextureRootResolver(
        string installationId,
        string userDirectory,
        string? overrideDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        InstallationId = installationId;
        _userDirectory = Path.GetFullPath(userDirectory);
        _overrideDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? null
            : Path.GetFullPath(overrideDirectory);
    }

    public string EmulatorId => DolphinDefinition.Instance.Id;

    public string InstallationId { get; }

    public Task<TexturePackRootResolution> ResolveAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Resolve(cancellationToken), cancellationToken);

    private TexturePackRootResolution Resolve(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_overrideDirectory is not null)
            return Resolved(_overrideDirectory);

        var defaultRoot = Path.Combine(_userDirectory, "Load", TexturesDirectoryName);
        var configurationPath = Path.Combine(_userDirectory, "Config", "Dolphin.ini");
        if (!File.Exists(configurationPath))
            return Resolved(defaultRoot);

        var ini = EmulatorIniFile.TryRead(configurationPath, out var diagnostic, cancellationToken);
        if (ini is null)
        {
            // An unreadable config does not make the default wrong, so still resolve — but say why
            // a configured path could not be honoured.
            return new TexturePackRootResolution(
                TexturePackRootResolutionStatus.Resolved,
                Path.GetFullPath(defaultRoot),
                $"Dolphin.ini could not be read, so the default Load folder is used: {diagnostic}");
        }

        if (!ini.TryGet("General", "LoadPath", out var loadPath) || string.IsNullOrWhiteSpace(loadPath))
            return Resolved(defaultRoot);

        return Resolved(Path.Combine(NormalizeConfiguredPath(loadPath), TexturesDirectoryName));
    }

    /// <summary>
    /// Dolphin writes these paths with mixed separators and a trailing slash
    /// (<c>F:\ES-DE\saves/dolphin/User/Load/</c>), and treats a relative value as relative to the
    /// user directory.
    /// </summary>
    private string NormalizeConfiguredPath(string configured)
    {
        var normalized = configured.Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar);

        return Path.IsPathFullyQualified(normalized)
            ? normalized
            : Path.Combine(_userDirectory, normalized);
    }

    private static TexturePackRootResolution Resolved(string path) =>
        new(TexturePackRootResolutionStatus.Resolved, Path.GetFullPath(path));
}
