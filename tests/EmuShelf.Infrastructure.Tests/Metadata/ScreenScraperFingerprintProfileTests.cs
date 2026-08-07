using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperFingerprintProfileTests
{
    [Fact]
    public void EverySupportedSystemHasAnExplicitFingerprintPolicy()
    {
        foreach (var system in KnownSystems.All)
        {
            Assert.True(
                KnownScreenScraperFingerprintProfiles.TryGet(system.Id, out _),
                $"Missing fingerprint policy for {system.Id}.");
        }
    }

    [Theory]
    [InlineData("playstation", ".cue")]
    [InlineData("playstation", ".m3u")]
    [InlineData("playstation2", ".chd")]
    [InlineData("psp", ".cso")]
    [InlineData("gamecube", ".rvz")]
    [InlineData("wii", ".wbfs")]
    [InlineData("arcade", ".zip")]
    [InlineData("dreamcast", ".gdi")]
    // 3DS whole-file hashing covers only the No-Intro NCSD cartridge dump; the installable and
    // single-title packagings are a different file whose hash is not in the catalogue.
    [InlineData("3ds", ".cia")]
    [InlineData("3ds", ".cxi")]
    public void ContainerAndDescriptorFormats_AreNeverWholeFileHashed(string systemId, string extension)
    {
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(systemId, out var profile));
        Assert.DoesNotContain(extension, profile!.WholeFileExtensions);
    }
}
