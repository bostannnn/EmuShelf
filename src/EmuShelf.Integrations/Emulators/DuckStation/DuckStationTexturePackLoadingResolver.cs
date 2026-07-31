namespace EmuShelf.Integrations.Emulators.DuckStation;

/// <summary>
/// Reads DuckStation SettingsVersion 3's <c>[TextureReplacements]</c> switches. DuckStation splits
/// replacement loading between VRAM writes and textures, so either being on means a pack can appear.
/// </summary>
public sealed class DuckStationTexturePackLoadingResolver : IniTexturePackLoadingResolver
{
    public DuckStationTexturePackLoadingResolver(string installationId, string configurationDirectory)
        : base(
            DuckStationDefinition.Instance.Id,
            installationId,
            configurationDirectory,
            ["settings.ini"],
            settingSection: "TextureReplacements",
            settingKeys: ["EnableTextureReplacements", "EnableVRAMWriteReplacements"],
            versionSection: "Main",
            versionKey: "SettingsVersion",
            supportedVersion: "3",
            perGameDirectory: "gamesettings")
    {
    }
}
