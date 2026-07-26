namespace EmuShelf.Integrations.Emulators.Pcsx2;

/// <summary>Resolves PCSX2 SettingsVersion 1's configured Textures directory.</summary>
public sealed class Pcsx2TextureRootResolver : IniTextureRootResolver
{
    public Pcsx2TextureRootResolver(
        string installationId,
        string configurationDirectory,
        string? overrideDirectory = null)
        : base(
            Pcsx2Definition.Instance.Id,
            installationId,
            configurationDirectory,
            overrideDirectory,
            [Path.Combine("inis", "PCSX2.ini"), "PCSX2.ini"],
            "UI",
            "1")
    {
    }
}
