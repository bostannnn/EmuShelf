using EmuShelf.Infrastructure.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class FlatpakApplicationDiscoveryTests
{
    [Fact]
    public void ParseInstalledRefs_ReadsApplicationAndBranchColumns()
    {
        var refs = FlatpakApplicationDiscovery.ParseInstalledRefs(
            "net.pcsx2.PCSX2\tstable\nnet.pcsx2.PCSX2\tbeta\norg.libretro.RetroArch\tstable");

        Assert.Equal(
            [
                new FlatpakApplicationDiscovery.InstalledRef("net.pcsx2.PCSX2", "stable"),
                new FlatpakApplicationDiscovery.InstalledRef("net.pcsx2.PCSX2", "beta"),
                new FlatpakApplicationDiscovery.InstalledRef("org.libretro.RetroArch", "stable"),
            ],
            refs);
    }

    [Fact]
    public void ParseInstalledRefs_SkipsRowsMissingABranch()
    {
        var refs = FlatpakApplicationDiscovery.ParseInstalledRefs(
            "net.pcsx2.PCSX2\nnet.pcsx2.PCSX2\t\nnet.pcsx2.PCSX2\tbeta");

        Assert.Equal(
            [new FlatpakApplicationDiscovery.InstalledRef("net.pcsx2.PCSX2", "beta")],
            refs);
    }

    [Fact]
    public void ParseInstalledRefs_EmptyOutput_YieldsNothing()
    {
        Assert.Empty(FlatpakApplicationDiscovery.ParseInstalledRefs(string.Empty));
    }
}
