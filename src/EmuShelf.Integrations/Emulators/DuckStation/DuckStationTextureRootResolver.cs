namespace EmuShelf.Integrations.Emulators.DuckStation;

/// <summary>Resolves DuckStation SettingsVersion 3's configured Textures directory.</summary>
public sealed class DuckStationTextureRootResolver : IniTextureRootResolver
{
    public DuckStationTextureRootResolver(
        string installationId,
        string configurationDirectory,
        string? overrideDirectory = null)
        : base(
            DuckStationDefinition.Instance.Id,
            installationId,
            configurationDirectory,
            overrideDirectory,
            ["settings.ini"],
            "Main",
            "3")
    {
    }
}
