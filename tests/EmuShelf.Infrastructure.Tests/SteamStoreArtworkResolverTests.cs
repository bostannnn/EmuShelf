using System.Net;
using EmuShelf.Core.Metadata;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.Infrastructure.Tests;

public class SteamStoreArtworkResolverTests
{
    [Theory]
    [InlineData("portrait.png", ".png")]
    [InlineData("b6cabe1940c55119820eee4ed2d0b604bd5b3af4/library_600x900.jpg", ".jpg")]
    public async Task ResolvesPublishedPortraitPathsRatherThanGuessing(string filename, string extension)
    {
        using var handler = new Response("""{"response":{"store_items":[{"appid":2062430,"success":1,"assets":{"asset_url_format":"steam/apps/2062430/${FILENAME}?t=123","library_capsule":"$FILE","header":"header.jpg"}}]}}""".Replace("$FILE", filename));
        using var http = new HttpClient(handler);
        var candidates = await new SteamStoreArtworkResolver(http).ResolveAsync("steam",
            [new(GameIdentifierKind.SteamAppId, "2062430", "test")], CancellationToken.None);
        Assert.Equal("https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/2062430/" + filename + "?t=123", candidates[0].SourceUri.AbsoluteUri);
        Assert.Equal(extension, candidates[0].FileExtension);
        Assert.EndsWith("header.jpg?t=123", candidates[1].SourceUri.AbsoluteUri);
        Assert.DoesNotContain("key=", handler.Uri);
    }

    [Theory]
    [InlineData("../123/portrait.png")]
    [InlineData("https://example.com/cover.jpg")]
    [InlineData("file:///cover.jpg")]
    public async Task RejectsUntrustedPaths(string filename)
    {
        using var handler = new Response("""{"response":{"store_items":[{"appid":2062430,"success":1,"assets":{"asset_url_format":"steam/apps/2062430/${FILENAME}","library_capsule":"$FILE"}}]}}""".Replace("$FILE", filename));
        using var http = new HttpClient(handler);
        Assert.Empty(await new SteamStoreArtworkResolver(http).ResolveAsync("steam",
            [new(GameIdentifierKind.SteamAppId, "2062430", "test")], CancellationToken.None));
    }

    [Theory]
    [InlineData("{bad json")]
    [InlineData("{\"response\":{\"store_items\":[]}}")]
    [InlineData("{\"response\":{\"store_items\":[{\"appid\":123,\"success\":1}]}}")]
    public async Task MissingOrMalformedAssetsAllowLegacyFallback(string json)
    {
        using var http = new HttpClient(new Response(json));
        Assert.Empty(await new SteamStoreArtworkResolver(http).ResolveAsync("steam",
            [new(GameIdentifierKind.SteamAppId, "2062430", "test")], CancellationToken.None));
    }

    private sealed class Response(string json) : HttpMessageHandler
    {
        public string Uri = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri!.AbsoluteUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
