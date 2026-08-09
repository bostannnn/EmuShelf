namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>
/// Reads Dolphin's <c>GFX.ini</c> <c>[Settings] HiresTextures</c>. Dolphin's graphics INI carries no
/// settings-version key, so this adapter relies on the strict INI shape plus the exact section and key
/// instead, and reports Unknown when either is missing.
/// </summary>
/// <remarks>
/// The global <c>GFX.ini</c> lives in Dolphin's config directory, which on native Linux and Flatpak is a
/// separate XDG tree, not <c>&lt;User&gt;/Config</c> — so it is passed in resolved (see
/// <see cref="EmulatorUserDirectories.FindDolphinConfigDirectory"/>). Per-game <c>GameSettings/</c> stays
/// under the data user directory, so that root is supplied separately.
/// </remarks>
public sealed class DolphinTexturePackLoadingResolver : IniTexturePackLoadingResolver
{
    public DolphinTexturePackLoadingResolver(
        string installationId,
        string configurationDirectory,
        string dataDirectory)
        : base(
            DolphinDefinition.Instance.Id,
            installationId,
            configurationDirectory,
            ["GFX.ini"],
            settingSection: "Settings",
            settingKeys: ["HiresTextures"],
            perGameDirectory: "GameSettings",
            perGameRootDirectory: dataDirectory)
    {
    }
}
