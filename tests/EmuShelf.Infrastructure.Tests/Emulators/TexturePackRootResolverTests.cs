using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackRootResolverTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task Pcsx2_ResolvesLivePortableIniShapeRelativeToDataDirectory()
    {
        var dataDirectory = Path.Combine(BaseDirectory, "Emulators", "pcsx2-qt");
        await WriteFileAsync(
            Path.Combine(dataDirectory, "inis", "PCSX2.ini"),
            "[UI]\nSettingsVersion = 1\n[Folders]\nTextures = ..\\..\\bios\\pcsx2\\textures\n");

        var result = await new Pcsx2TextureRootResolver("pcsx2-main", dataDirectory).ResolveAsync();

        Assert.Equal(TexturePackRootResolutionStatus.Resolved, result.Status);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(dataDirectory, "..", "..", "bios", "pcsx2", "textures")),
            result.RootDirectory);
    }

    [Fact]
    public async Task DuckStation_ResolvesLivePortableIniShapeRelativeToDataDirectory()
    {
        var dataDirectory = Path.Combine(BaseDirectory, "Emulators", "duckstation");
        await WriteFileAsync(
            Path.Combine(dataDirectory, "settings.ini"),
            "[Main]\nSettingsVersion = 3\n[Folders]\nTextures = ..\\..\\saves\\psx\\duckstation\\textures\n");

        var result = await new DuckStationTextureRootResolver("duckstation-main", dataDirectory).ResolveAsync();

        Assert.Equal(TexturePackRootResolutionStatus.Resolved, result.Status);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(dataDirectory, "..", "..", "saves", "psx", "duckstation", "textures")),
            result.RootDirectory);
    }

    [Fact]
    public async Task UnknownIniVersion_FailsVisiblyInsteadOfGuessing()
    {
        var dataDirectory = Path.Combine(BaseDirectory, "pcsx2");
        await WriteFileAsync(
            Path.Combine(dataDirectory, "inis", "PCSX2.ini"),
            "[UI]\nSettingsVersion = 99\n[Folders]\nTextures = textures\n");

        var result = await new Pcsx2TextureRootResolver("pcsx2-main", dataDirectory).ResolveAsync();

        Assert.Equal(TexturePackRootResolutionStatus.ConfigurationUnsupported, result.Status);
        Assert.Null(result.RootDirectory);
    }

    [Fact]
    public async Task ExplicitOverride_DoesNotRequireAnEmulatorConfigurationFile()
    {
        var overrideDirectory = Path.Combine(BaseDirectory, "custom", "textures");

        var result = await new DuckStationTextureRootResolver(
            "duckstation-main",
            Path.Combine(BaseDirectory, "missing-configuration"),
            overrideDirectory).ResolveAsync();

        Assert.Equal(TexturePackRootResolutionStatus.Resolved, result.Status);
        Assert.Equal(Path.GetFullPath(overrideDirectory), result.RootDirectory);
    }

    [Fact]
    public async Task Dolphin_UsesEffectiveUserDirectoryAndPpssppReusesMemoryStickResolution()
    {
        var dolphinUserDirectory = Path.Combine(BaseDirectory, "saves", "dolphin", "User");
        var dolphin = await new DolphinTextureRootResolver(
            "dolphin-main",
            dolphinUserDirectory).ResolveAsync();

        var ppssppInstallation = Path.Combine(BaseDirectory, "Emulators", "ppsspp");
        var ppsspp = await new PpssppTextureRootResolver(
            "ppsspp-main",
            new PpssppSaveLocationProvider(
                ppssppInstallation,
                isWindows: true)).ResolveAsync();

        Assert.Equal(
            Path.Combine(dolphinUserDirectory, "Load", "Textures"),
            dolphin.RootDirectory);
        Assert.Equal(
            Path.Combine(ppssppInstallation, "memstick", "PSP", "TEXTURES"),
            ppsspp.RootDirectory);
    }

    private static async Task WriteFileAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }
}
