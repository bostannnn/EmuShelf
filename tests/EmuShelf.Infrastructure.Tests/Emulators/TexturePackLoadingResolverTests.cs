using EmuShelf.Core.TexturePacks;
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
    public async Task Dolphin_ReadsHiresTexturesFromItsGraphicsIni()
    {
        WriteIni(Path.Combine("Config", "GFX.ini"), "[Settings]", "HiresTextures = True");

        var resolution = await new DolphinTexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Enabled, resolution.Status);
    }

    [Fact]
    public async Task Ppsspp_ReadsReplaceTexturesFromItsConfiguration()
    {
        WriteIni("ppsspp.ini", "[Graphics]", "ReplaceTextures = False");

        var resolution = await new PpssppTexturePackLoadingResolver("i", _root).ResolveAsync();

        Assert.Equal(TexturePackLoadingStatus.Disabled, resolution.Status);
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
