using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.Infrastructure.Tests.Emulators;

/// <summary>
/// Opt-in, read-only verification against installed emulator packs. Enabled only when
/// EMUSHELF_TEST_LIVE_TEXTURE_PACKS=1 and the four root/configuration variables are supplied.
/// </summary>
public sealed class LiveTexturePackInventoryTests
{
    [Fact]
    public async Task ConfiguredRootsResolveAndContainUsablePacks()
    {
        if (Environment.GetEnvironmentVariable("EMUSHELF_TEST_LIVE_TEXTURE_PACKS") != "1")
            return;

        var pcsx2Configuration = Required("EMUSHELF_TEST_PCSX2_CONFIG");
        var pcsx2Root = Required("EMUSHELF_TEST_PCSX2_TEXTURES");
        var duckStationConfiguration = Required("EMUSHELF_TEST_DUCKSTATION_CONFIG");
        var duckStationRoot = Required("EMUSHELF_TEST_DUCKSTATION_TEXTURES");
        var dolphinRoot = Required("EMUSHELF_TEST_DOLPHIN_TEXTURES");
        var ppssppInstallation = Required("EMUSHELF_TEST_PPSSPP_INSTALLATION");
        var ppssppRoot = Required("EMUSHELF_TEST_PPSSPP_TEXTURES");

        var resolvedPcsx2 = await new Pcsx2TextureRootResolver(
            "live-pcsx2",
            pcsx2Configuration).ResolveAsync();
        var resolvedDuckStation = await new DuckStationTextureRootResolver(
            "live-duckstation",
            duckStationConfiguration).ResolveAsync();
        var resolvedPpsspp = await new PpssppTextureRootResolver(
            "live-ppsspp",
            new PpssppSaveLocationProvider(ppssppInstallation, isWindows: true)).ResolveAsync();

        Assert.Equal(Path.GetFullPath(pcsx2Root), resolvedPcsx2.RootDirectory);
        Assert.Equal(Path.GetFullPath(duckStationRoot), resolvedDuckStation.RootDirectory);
        Assert.Equal(Path.GetFullPath(ppssppRoot), resolvedPpsspp.RootDirectory);

        var snapshots = await Task.WhenAll(
            new Pcsx2TexturePackSource("live-pcsx2", pcsx2Root).ScanAsync(),
            new DuckStationTexturePackSource("live-duckstation", duckStationRoot).ScanAsync(),
            new DolphinTexturePackSource("live-dolphin", dolphinRoot).ScanAsync(),
            new PpssppTexturePackSource("live-ppsspp", ppssppRoot).ScanAsync());

        Assert.All(snapshots, snapshot =>
        {
            Assert.Equal(TexturePackRootStatus.Ready, snapshot.RootStatus);
            Assert.NotEmpty(snapshot.Entries);
            Assert.Contains(snapshot.Entries, entry => entry.IsUsable);
        });
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException($"{name} is required for the live texture-pack test.");
}
