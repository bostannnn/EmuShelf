using System.Text.Json;
using EmuShelf.Infrastructure.Emulators;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public class GitHubEmulatorReleaseParserTests
{
    [Fact]
    public void Parse_ReadsNonSemverTagAndAssets()
    {
        const string json = """
        {
          "tag_name": "latest",
          "name": "Rolling preview",
          "published_at": "2026-01-02T03:04:05Z",
          "assets": [
            { "name": "duckstation-windows-x64-release.zip", "browser_download_url": "https://example/win.zip", "size": 42 },
            { "name": "DuckStation-x64.AppImage", "browser_download_url": "https://example/linux.AppImage", "size": 7 }
          ]
        }
        """;

        var release = GitHubEmulatorReleaseParser.Parse(json);

        Assert.NotNull(release);
        Assert.Equal("latest", release!.Tag);
        Assert.Equal("Rolling preview", release.Name);
        Assert.Equal(2, release.Assets.Count);
        Assert.Equal("https://example/win.zip", release.Assets[0].DownloadUrl);
        Assert.Equal(42, release.Assets[0].SizeBytes);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenTagMissing()
    {
        Assert.Null(GitHubEmulatorReleaseParser.Parse("""{ "name": "no tag" }"""));
    }

    [Fact]
    public void Parse_SkipsAssetsMissingNameOrUrl()
    {
        const string json = """
        {
          "tag_name": "v1",
          "assets": [
            { "name": "good.zip", "browser_download_url": "https://example/good.zip", "size": 1 },
            { "name": "no-url.zip" },
            { "browser_download_url": "https://example/no-name" }
          ]
        }
        """;

        var release = GitHubEmulatorReleaseParser.Parse(json);

        Assert.NotNull(release);
        Assert.Equal("good.zip", Assert.Single(release!.Assets).Name);
    }

    [Fact]
    public void Parse_Throws_OnMalformedJson()
    {
        // JsonDocument surfaces a JsonReaderException, which derives from JsonException.
        Assert.ThrowsAny<JsonException>(() => GitHubEmulatorReleaseParser.Parse("{ not json"));
    }
}
