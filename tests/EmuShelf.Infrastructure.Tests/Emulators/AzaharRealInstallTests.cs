using EmuShelf.Core.SaveSync;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators.Azahar;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class AzaharRealInstallTests
{
    [Fact]
    public async Task RealAzaharInstallation_ResolvesSaves_TexturePacks_AndCustomTextureState()
    {
        // Opt-in: set EMUSHELF_TEST_AZAHAR_DIR to a real Azahar installation directory (the folder
        // containing azahar.exe and its portable `user/`) to verify the read-only save-sync and
        // texture-pack adapters against a genuine SD card and qt-config.ini.
        var installation = Environment.GetEnvironmentVariable("EMUSHELF_TEST_AZAHAR_DIR");
        if (string.IsNullOrWhiteSpace(installation) || !Directory.Exists(installation))
            return;

        // Every enumerated title/extdata save unit resolves to a real folder on the SD card.
        var saves = new AzaharSaveLocationProvider(installation);
        var units = await saves.GetSaveUnitsAsync();
        Assert.NotEmpty(units);
        Assert.All(units, unit =>
        {
            Assert.StartsWith("azahar/", unit.UnitId);
            var location = saves.ResolveUnit(unit.UnitId);
            Assert.NotNull(location);
            Assert.True(Directory.Exists(location!.Path), $"missing save path for {unit.UnitId}");
        });

        // At least one installed texture pack is usable and keyed on a 16-hex title id.
        var userDirectory = saves.GetUserDirectory();
        var textures = await AzaharTexturePackSource.FromUserDirectory("real", userDirectory).ScanAsync();
        Assert.Equal(TexturePackRootStatus.Ready, textures.RootStatus);
        Assert.Contains(textures.Entries, entry =>
            entry.ContentStatus == TexturePackContentStatus.Usable &&
            entry.MatchKeys.Any(key => key.Rule == TexturePackMatchRule.Nintendo3dsTitleId));

        // qt-config.ini [Utility] custom_textures is read (a concrete Enabled/Disabled, not Unknown).
        var loading = await new AzaharTexturePackLoadingResolver(
            "real", Path.Combine(userDirectory, "config")).ResolveAsync();
        Assert.NotEqual(TexturePackLoadingStatus.Unknown, loading.Status);
    }
}
