using EmuShelf.Core.Launching.Android;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.Infrastructure.Tests.Launching.Android;

public class AndroidRomHandoffRulesTests
{
    [Theory]
    [InlineData("/roms/psx/Game.cue", true)]
    [InlineData("/roms/psx/Game.m3u", true)]
    [InlineData("/roms/dc/Game.gdi", true)]
    [InlineData("/roms/psx/GAME.CUE", true)] // extension match is case-insensitive
    [InlineData("/roms/psx/Game.chd", false)] // self-contained, not a multi-file descriptor
    [InlineData("/roms/psx/Game.pbp", false)]
    [InlineData("/roms/ps2/Game.iso", false)]
    [InlineData("/roms/psx/Game.bin", false)] // a track, not the descriptor
    [InlineData("", false)]
    public void IsMultiFileDescriptor_MatchesOnlyRelativeSiblingDescriptors(string path, bool expected)
    {
        Assert.Equal(expected, AndroidRomHandoffRules.IsMultiFileDescriptor(path));
    }

    [Fact]
    public void PrefersRealPath_OnlyForAnEmulatorThatResolvesSiblings_AndAMultiFileDescriptor()
    {
        // DuckStation opts in (NeedsRealPathForMultiFile) and must get a real path for a .cue …
        Assert.True(AndroidRomHandoffRules.PrefersRealPath(
            AndroidEmulatorLaunchProfiles.DuckStation, "/roms/psx/Game.cue"));

        // … but a single-file ROM stays on the FileProvider URI even for DuckStation.
        Assert.False(AndroidRomHandoffRules.PrefersRealPath(
            AndroidEmulatorLaunchProfiles.DuckStation, "/roms/psx/Game.chd"));
    }

    [Fact]
    public void PrefersRealPath_IsFalse_ForEmulatorsThatReadTheDescriptorAsADocument()
    {
        // ARMSX2/Dolphin/etc. never opt in, so even a multi-file descriptor takes the FileProvider URI.
        Assert.False(AndroidEmulatorLaunchProfiles.Armsx2.NeedsRealPathForMultiFile);
        Assert.False(AndroidRomHandoffRules.PrefersRealPath(
            AndroidEmulatorLaunchProfiles.Dolphin, "/roms/gc/Game.m3u"));
    }

    [Fact]
    public void DuckStation_IsTheOnlyProfileNeedingARealPathForMultiFile()
    {
        var optedIn = AndroidEmulatorLaunchProfiles.All
            .Where(profile => profile.NeedsRealPathForMultiFile)
            .Select(profile => profile.Id)
            .ToList();

        Assert.Equal(["android.duckstation"], optedIn);
    }
}
