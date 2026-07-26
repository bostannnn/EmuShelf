namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>
/// Reads PPSSPP's <c>ppsspp.ini</c> <c>[Graphics] ReplaceTextures</c>. PPSSPP keeps no per-game
/// graphics configuration files, so the global setting is the whole answer here.
/// </summary>
public sealed class PpssppTexturePackLoadingResolver : IniTexturePackLoadingResolver
{
    public PpssppTexturePackLoadingResolver(string installationId, string configurationDirectory)
        : base(
            PpssppDefinition.Instance.Id,
            installationId,
            configurationDirectory,
            ["ppsspp.ini"],
            settingSection: "Graphics",
            settingKeys: ["ReplaceTextures"])
    {
    }
}
