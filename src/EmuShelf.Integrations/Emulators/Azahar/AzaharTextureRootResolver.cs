using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Azahar;

/// <summary>
/// Resolves Azahar's custom-texture root. Azahar always loads from <c>&lt;user&gt;/load/textures</c>
/// (no configurable path key), so this simply appends that fixed sub-path to the resolved user
/// directory, or returns the user's explicit override.
/// </summary>
public sealed class AzaharTextureRootResolver : ITexturePackRootResolver
{
    private readonly string _userDirectory;
    private readonly string? _overrideDirectory;

    public AzaharTextureRootResolver(
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

    public string EmulatorId => AzaharDefinition.Instance.Id;

    public string InstallationId { get; }

    public Task<TexturePackRootResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var root = _overrideDirectory ?? Path.Combine(_userDirectory, "load", "textures");
        return Task.FromResult(new TexturePackRootResolution(
            TexturePackRootResolutionStatus.Resolved,
            root));
    }
}
