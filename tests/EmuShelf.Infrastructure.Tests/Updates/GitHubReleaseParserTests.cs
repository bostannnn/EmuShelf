using EmuShelf.Core.Updates;
using EmuShelf.Infrastructure.Updates;

namespace EmuShelf.Infrastructure.Tests.Updates;

public class GitHubReleaseParserTests
{
    private const string SampleRelease = """
    {
      "tag_name": "v1.2.3",
      "body": "Fixes and polish.",
      "published_at": "2026-08-05T12:00:00Z",
      "assets": [
        { "name": "EmuShelf-win-x64.zip", "browser_download_url": "https://example.test/win.zip", "size": 12345 },
        { "name": "EmuShelf-win-x64.sha256", "browser_download_url": "https://example.test/win.sha256", "size": 70 }
      ]
    }
    """;

    [Fact]
    public void ParseRelease_ReadsTagNotesAssets()
    {
        var release = GitHubReleaseParser.ParseRelease(SampleRelease);

        Assert.NotNull(release);
        Assert.Equal("v1.2.3", release!.TagName);
        Assert.Equal(new SemanticVersion(1, 2, 3), release.Version);
        Assert.Equal("Fixes and polish.", release.Notes);
        Assert.Equal(2, release.Assets.Count);
        var payload = release.Assets[0];
        Assert.Equal("EmuShelf-win-x64.zip", payload.Name);
        Assert.Equal("https://example.test/win.zip", payload.DownloadUrl);
        Assert.Equal(12345, payload.SizeBytes);
    }

    [Fact]
    public void ParseRelease_ReturnsNullForUnparsableTag()
    {
        Assert.Null(GitHubReleaseParser.ParseRelease("""{ "tag_name": "nightly" }"""));
    }

    [Theory]
    [InlineData("abc123def4567890abc123def4567890abc123def4567890abc123def4567890  EmuShelf-win-x64.zip",
        "abc123def4567890abc123def4567890abc123def4567890abc123def4567890")]
    [InlineData("ABC123DEF4567890ABC123DEF4567890ABC123DEF4567890ABC123DEF4567890",
        "abc123def4567890abc123def4567890abc123def4567890abc123def4567890")]
    public void ParseChecksum_ExtractsTheLeadingHexDigest(string content, string expected)
    {
        Assert.Equal(expected, GitHubReleaseParser.ParseChecksum(content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash file")]
    [InlineData("short  file")]
    public void ParseChecksum_ReturnsNullForNonDigest(string content)
    {
        Assert.Null(GitHubReleaseParser.ParseChecksum(content));
    }
}
