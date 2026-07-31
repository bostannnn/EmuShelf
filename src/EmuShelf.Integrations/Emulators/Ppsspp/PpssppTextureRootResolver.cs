using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>Resolves PPSSPP's texture root through its existing Memory Stick adapter.</summary>
public sealed class PpssppTextureRootResolver : ITexturePackRootResolver
{
    private readonly PpssppSaveLocationProvider _saveLocationProvider;
    private readonly string? _overrideDirectory;

    public PpssppTextureRootResolver(
        string installationId,
        PpssppSaveLocationProvider saveLocationProvider,
        string? overrideDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentNullException.ThrowIfNull(saveLocationProvider);
        InstallationId = installationId;
        _saveLocationProvider = saveLocationProvider;
        _overrideDirectory = string.IsNullOrWhiteSpace(overrideDirectory)
            ? null
            : Path.GetFullPath(overrideDirectory);
    }

    public string EmulatorId => PpssppDefinition.Instance.Id;

    public string InstallationId { get; }

    public async Task<TexturePackRootResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (_overrideDirectory is not null)
        {
            return new TexturePackRootResolution(
                TexturePackRootResolutionStatus.Resolved,
                _overrideDirectory);
        }

        try
        {
            var memoryStick = await _saveLocationProvider.GetMemoryStickDirectoryAsync(cancellationToken);
            return new TexturePackRootResolution(
                TexturePackRootResolutionStatus.Resolved,
                Path.Combine(memoryStick, "PSP", "TEXTURES"));
        }
        catch (PpssppConfigurationFormatException ex)
        {
            return new TexturePackRootResolution(
                TexturePackRootResolutionStatus.ConfigurationUnsupported,
                null,
                ex.Message);
        }
    }
}
