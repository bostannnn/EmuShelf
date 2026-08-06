using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Tests.Storage;

public class FilePathComparisonTests
{
    // Guards the intent that macOS is grouped WITH Windows as case-insensitive (its default APFS/HFS+
    // is), matching the case-insensitive Games.Path database collation. A regression back to
    // "Windows only" — which quietly split one on-disk file into two identities on macOS — fails here.
    [Fact]
    public void CaseInsensitive_CoversWindowsAndMacOs_ButNotOtherPlatforms()
    {
        Assert.Equal(
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(),
            FilePathComparison.IsCaseInsensitive);
    }

    [Fact]
    public void ComparerAndComparison_TrackTheCaseSensitivityFlag()
    {
        var comparerIgnoresCase =
            FilePathComparison.Comparer.Equals("/Games/Sonic.CUE", "/games/sonic.cue");
        var comparisonIgnoresCase =
            string.Equals("/Games/Sonic.CUE", "/games/sonic.cue", FilePathComparison.Comparison);

        Assert.Equal(FilePathComparison.IsCaseInsensitive, comparerIgnoresCase);
        Assert.Equal(FilePathComparison.IsCaseInsensitive, comparisonIgnoresCase);
    }

    [Fact]
    public void Comparer_MatchesIdenticalPaths_AndSeparatesDistinctOnes_OnEveryPlatform()
    {
        Assert.True(FilePathComparison.Comparer.Equals("/games/sonic.cue", "/games/sonic.cue"));
        Assert.False(FilePathComparison.Comparer.Equals("/games/sonic.cue", "/games/sonic2.cue"));
    }
}
