namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>
/// Reads Dolphin's <c>Config/GFX.ini</c> <c>[Settings] HiresTextures</c>. Dolphin's graphics INI
/// carries no settings-version key, so this adapter relies on the strict INI shape plus the exact
/// section and key instead, and reports Unknown when either is missing.
/// </summary>
public sealed class DolphinTexturePackLoadingResolver : IniTexturePackLoadingResolver
{
    public DolphinTexturePackLoadingResolver(string installationId, string userDirectory)
        : base(
            DolphinDefinition.Instance.Id,
            installationId,
            userDirectory,
            [Path.Combine("Config", "GFX.ini")],
            settingSection: "Settings",
            settingKeys: ["HiresTextures"],
            perGameDirectory: "GameSettings")
    {
    }
}
