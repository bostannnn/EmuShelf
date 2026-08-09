using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators.Azahar;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackLoadingResolverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("emushelf-texture-loading").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Pcsx2_ReportsEnabledWhenTheSupportedSettingsVersionTurnsReplacementsOn()
    {
        WriteIni(
            Path.Combine("inis", "PCSX2.ini"),
            "[UI]",
            "SettingsVersion = 1",
            "[EmuCore/GS]",
            "LoadTextureReplacements = true");

        var resolution = await new Pcsx2TexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Enabled, resolution.Status);
    }

    [Fact]
    public async Task Pcsx2_ReportsDisabledOnlyWhenTheSettingIsPresentAndOff()
    {
        WriteIni(
            Path.Combine("inis", "PCSX2.ini"),
            "[UI]",
            "SettingsVersion = 1",
            "[EmuCore/GS]",
            "LoadTextureReplacements = false");

        var resolution = await new Pcsx2TexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Disabled, resolution.Status);
    }

    [Fact]
    public async Task UnsupportedSettingsVersion_IsUnknownRatherThanAPlausibleGuess()
    {
        WriteIni(
            Path.Combine("inis", "PCSX2.ini"),
            "[UI]",
            "SettingsVersion = 99",
            "[EmuCore/GS]",
            "LoadTextureReplacements = true");

        var resolution = await new Pcsx2TexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Unknown, resolution.Status);
        Assert.NotNull(resolution.Diagnostic);
    }

    [Fact]
    public async Task MissingSetting_IsUnknownRatherThanDisabled()
    {
        WriteIni(Path.Combine("inis", "PCSX2.ini"), "[UI]", "SettingsVersion = 1");

        var resolution = await new Pcsx2TexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Unknown, resolution.Status);
    }

    [Fact]
    public async Task MissingConfigurationFile_IsUnknown()
    {
        var resolution = await new Pcsx2TexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Unknown, resolution.Status);
    }

    [Fact]
    public async Task PerGameConfiguration_MakesTheAnswerUnknownForThatGameOnly()
    {
        WriteIni(
            Path.Combine("inis", "PCSX2.ini"),
            "[UI]",
            "SettingsVersion = 1",
            "[EmuCore/GS]",
            "LoadTextureReplacements = true");
        WriteIni(Path.Combine("gamesettings", "SLUS-20946_Some Title.ini"), "[EmuCore/GS]");

        var resolver = new Pcsx2TexturePackLoadingResolver("i", _root);

        // The per-game file can override the global switch, and the precedence rules differ per
        // version, so EmuShelf refuses to answer for that game rather than reporting the global one.
        Assert.Equal(TexturePackLoadingStatus.Unknown, (await resolver.ResolveAsync("SLUS-20946")).Status);
        Assert.Equal(TexturePackLoadingStatus.Enabled, (await resolver.ResolveAsync("SLUS-99999")).Status);
        Assert.Equal(TexturePackLoadingStatus.Enabled, (await resolver.ResolveAsync()).Status);
    }

    [Fact]
    public async Task DuckStation_TreatsEitherReplacementSwitchBeingOnAsEnabled()
    {
        WriteIni(
            "settings.ini",
            "[Main]",
            "SettingsVersion = 3",
            "[TextureReplacements]",
            "EnableTextureReplacements = false",
            "EnableVRAMWriteReplacements = true");

        var resolution = await new DuckStationTexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Enabled, resolution.Status);
    }

    [Fact]
    public async Task DuckStation_IsDisabledOnlyWhenEveryReplacementSwitchIsPresentAndOff()
    {
        WriteIni(
            "settings.ini",
            "[Main]",
            "SettingsVersion = 3",
            "[TextureReplacements]",
            "EnableTextureReplacements = false",
            "EnableVRAMWriteReplacements = false");

        var resolution = await new DuckStationTexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Disabled, resolution.Status);
    }

    [Fact]
    public async Task Dolphin_ReadsHiresTexturesFromTheConfigTree_NotTheOldDataDirConfig()
    {
        // On native Linux and Flatpak, GFX.ini lives in Dolphin's separate config tree, not
        // <data>/Config. A stale GFX.ini in the old data-dir location must be ignored.
        WriteIni(Path.Combine("config", "GFX.ini"), "[Settings]", "HiresTextures = True");
        WriteIni(Path.Combine("data", "Config", "GFX.ini"), "[Settings]", "HiresTextures = False");

        var resolution = await new DolphinTexturePackLoadingResolver(
            "i", Path.Combine(_root, "config"), Path.Combine(_root, "data")).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Enabled, resolution.Status);
    }

    [Fact]
    public async Task Dolphin_FindsPerGameOverridesInTheDataTreeGameSettings_NotTheConfigTree()
    {
        // GameSettings stays under the data user directory even when the global GFX.ini is split into
        // the config tree, so a per-game file there must make that game's answer Unknown.
        WriteIni(Path.Combine("config", "GFX.ini"), "[Settings]", "HiresTextures = True");
        WriteIni(Path.Combine("data", "GameSettings", "GALE01.ini"), "[Video_Settings]");
        // A decoy under the config tree's GameSettings must not be consulted.
        WriteIni(Path.Combine("config", "GameSettings", "RMGE01.ini"), "[Video_Settings]");

        var resolver = new DolphinTexturePackLoadingResolver(
            "i", Path.Combine(_root, "config"), Path.Combine(_root, "data"));

        Assert.Equal(TexturePackLoadingStatus.Unknown, (await resolver.ResolveAsync("GALE01")).Status);
        Assert.Equal(TexturePackLoadingStatus.Enabled, (await resolver.ResolveAsync("RMGE01")).Status);
        Assert.Equal(TexturePackLoadingStatus.Enabled, (await resolver.ResolveAsync()).Status);
    }

    [Fact]
    public async Task Ppsspp_ReadsReplaceTexturesFromItsConfiguration()
    {
        WriteIni("ppsspp.ini", "[Graphics]", "ReplaceTextures = False");

        var resolution = await new PpssppTexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Disabled, resolution.Status);
    }

    [Fact]
    public async Task Azahar_ReadsCustomTexturesFromQtConfigUtilitySection()
    {
        WriteIni("qt-config.ini", "[Utility]", "custom_textures=true");
        Assert.Equal(
            TexturePackLoadingStatus.Enabled,
            (await new AzaharTexturePackLoadingResolver("i", _root).ResolveAsync()).Status);

        WriteIni("qt-config.ini", "[Utility]", "custom_textures=false");
        Assert.Equal(
            TexturePackLoadingStatus.Disabled,
            (await new AzaharTexturePackLoadingResolver("i", _root).ResolveAsync()).Status);
    }

    [Fact]
    public async Task MalformedConfiguration_IsUnknownAndNeverThrows()
    {
        WriteIni("ppsspp.ini", "ReplaceTextures = True");

        var resolution = await new PpssppTexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Unknown, resolution.Status);
    }

    [Fact]
    public async Task ResolvingLoading_LeavesTheConfigurationBytesAndTimestampUnchanged()
    {
        var relative = "ppsspp.ini";
        WriteIni(relative, "[Graphics]", "ReplaceTextures = True");
        var path = Path.Combine(_root, relative);
        var before = File.ReadAllBytes(path);
        var writtenAt = File.GetLastWriteTimeUtc(path);

        await new PpssppTexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(path));
    }

    private void WriteIni(string relativePath, params string[] lines)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }
}
