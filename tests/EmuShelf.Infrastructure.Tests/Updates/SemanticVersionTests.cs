using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Tests.Updates;

public class SemanticVersionTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V10.0.8", 10, 0, 8)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("2", 2, 0, 0)]
    [InlineData("1.2.3+abc123", 1, 2, 3)]
    [InlineData("1.2.3-rc1", 1, 2, 3)]
    public void TryParse_AcceptsTagsAndSuffixes(string text, int major, int minor, int patch)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(new SemanticVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("v")]
    [InlineData("abc")]
    [InlineData("1.2.3.4")]
    [InlineData("-1.0.0")]
    public void TryParse_RejectsGarbage(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Fact]
    public void Comparison_OrdersByMajorThenMinorThenPatch()
    {
        Assert.True(new SemanticVersion(1, 0, 0) < new SemanticVersion(1, 0, 1));
        Assert.True(new SemanticVersion(1, 2, 0) > new SemanticVersion(1, 1, 9));
        Assert.True(new SemanticVersion(2, 0, 0) > new SemanticVersion(1, 9, 9));
        Assert.True(new SemanticVersion(1, 0, 8) >= new SemanticVersion(1, 0, 8));
        Assert.True(new SemanticVersion(1, 0, 8) <= new SemanticVersion(1, 0, 8));
    }

    [Fact]
    public void ParseOrZero_FallsBackToZero()
    {
        Assert.Equal(SemanticVersion.Zero, SemanticVersion.ParseOrZero("not-a-version"));
        Assert.Equal(new SemanticVersion(1, 0, 8), SemanticVersion.ParseOrZero("1.0.8"));
    }
}
