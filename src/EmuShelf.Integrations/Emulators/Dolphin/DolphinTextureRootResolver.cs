using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>Resolves textures from Dolphin's effective user directory or an explicit override.</summary>
public sealed class DolphinTextureRootResolver : ITexturePackRootResolver
{
    private readonly string _rootDirectory;

    public DolphinTextureRootResolver(
        string installationId,
        string userDirectory,
        string? overrideDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDirectory);
        InstallationId = installationId;
        _rootDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? Path.Combine(Path.GetFullPath(userDirectory), "Load", "Textures")
            : Path.GetFullPath(overrideDirectory);
    }

    public string EmulatorId => DolphinDefinition.Instance.Id;

    public string InstallationId { get; }

    public Task<TexturePackRootResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new TexturePackRootResolution(
            TexturePackRootResolutionStatus.Resolved,
            Path.GetFullPath(_rootDirectory)));
    }
}
