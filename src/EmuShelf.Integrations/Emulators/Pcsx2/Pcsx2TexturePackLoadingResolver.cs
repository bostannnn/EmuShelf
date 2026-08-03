namespace EmuShelf.Integrations.Emulators.Pcsx2;

/// <summary>Reads PCSX2 SettingsVersion 1's <c>[EmuCore/GS] LoadTextureReplacements</c>.</summary>
public sealed class Pcsx2TexturePackLoadingResolver : IniTexturePackLoadingResolver
{
    public Pcsx2TexturePackLoadingResolver(string installationId, string configurationDirectory)
        : base(
            Pcsx2Definition.Instance.Id,
            installationId,
            configurationDirectory,
            [Path.Combine("inis", "PCSX2.ini"), "PCSX2.ini"],
            settingSection: "EmuCore/GS",
            settingKeys: ["LoadTextureReplacements"],
            versionSection: "UI",
            versionKey: "SettingsVersion",
            supportedVersion: "1",
            perGameDirectory: "gamesettings")
    {
    }
}
