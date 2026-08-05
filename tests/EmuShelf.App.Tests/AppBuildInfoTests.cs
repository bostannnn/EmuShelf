using System;
using EmuShelf.App;
using Xunit;

namespace EmuShelf.App.Tests;

public class AppBuildInfoTests
{
    [Fact]
    public void ParseVersion_StripsTheCommitMetadataSuffix()
    {
        Assert.Equal("1.0.8", AppBuildInfo.ParseVersion("1.0.8+3f2383650", new Version(1, 0, 8)));
    }

    [Fact]
    public void ParseVersion_KeepsAPlainInformationalVersion()
    {
        Assert.Equal("1.0.8", AppBuildInfo.ParseVersion("1.0.8", new Version(1, 0, 8)));
    }

    [Fact]
    public void ParseVersion_FallsBackToTheAssemblyVersionWhenInformationalIsMissing()
    {
        Assert.Equal("2.3.4", AppBuildInfo.ParseVersion(null, new Version(2, 3, 4)));
        Assert.Equal("0.0.0", AppBuildInfo.ParseVersion("   ", null));
    }

    [Fact]
    public void ParseCommitDate_ReadsAnIso8601Stamp()
    {
        var parsed = AppBuildInfo.ParseCommitDate("2026-08-05T18:57:36+03:00");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTimeOffset(2026, 8, 5, 18, 57, 36, TimeSpan.FromHours(3)), parsed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData(null)]
    public void ParseCommitDate_ReturnsNullForMissingOrGarbage(string? raw)
    {
        Assert.Null(AppBuildInfo.ParseCommitDate(raw));
    }

    [Fact]
    public void Version_IsStampedIntoTheAppAssembly()
    {
        // The StampGitVersion target runs for the App build the tests reference, so the real
        // version must be a non-empty string rather than blank.
        Assert.False(string.IsNullOrWhiteSpace(AppBuildInfo.Version));
    }
}
