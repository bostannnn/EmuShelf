namespace EmuShelf.Integrations.Emulators.Azahar;

/// <summary>
/// Reads Azahar's <c>qt-config.ini</c> <c>[Utility] custom_textures</c> switch. Azahar keeps no
/// per-game graphics configuration files, so the global setting is the whole answer here.
/// </summary>
public sealed class AzaharTexturePackLoadingResolver : IniTexturePackLoadingResolver
{
    public AzaharTexturePackLoadingResolver(string installationId, string configurationDirectory)
        : base(
            AzaharDefinition.Instance.Id,
            installationId,
            configurationDirectory,
            ["qt-config.ini"],
            settingSection: "Utility",
            settingKeys: ["custom_textures"])
    {
    }
}
