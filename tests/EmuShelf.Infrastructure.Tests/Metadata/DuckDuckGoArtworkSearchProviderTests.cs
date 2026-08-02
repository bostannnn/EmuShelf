using System.Net;
using System.Text;
using EmuShelf.Core.Metadata;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class DuckDuckGoArtworkSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_UsesGameContextAndReturnsOnlySafeSizedImages()
    {
        var requests = new List<Uri>();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath == "/")
                return TextResponse("<script>var vqd=\"123-456\";</script>", "text/html");

            return TextResponse(
                """
                {
                  "results": [
                    {
                      "image": "https://covers.example/landscape.jpg",
                      "thumbnail": "https://thumbs.example/landscape.jpg",
                      "url": "https://source.example/landscape",
                      "title": "Landscape",
                      "width": 1200,
                      "height": 600
                    },
                    {
                      "image": "https://covers.example/square.png",
                      "thumbnail": "https://thumbs.example/square.png",
                      "url": "https://source.example/square",
                      "title": "Square cover",
                      "width": 800,
                      "height": 800
                    },
                    {
                      "image": "javascript:alert(1)",
                      "thumbnail": "https://thumbs.example/unsafe.jpg",
                      "width": 800,
                      "height": 800
                    },
                    {
                      "image": "https://covers.example/tiny.jpg",
                      "thumbnail": "https://thumbs.example/tiny.jpg",
                      "width": 120,
                      "height": 180
                    }
                  ]
                }
                """,
                "application/json");
        }));
        var provider = new DuckDuckGoArtworkSearchProvider(httpClient);

        var results = await provider.SearchAsync("Ridge Racer", "PlayStation", 1.0);

        Assert.Equal(2, results.Count);
        Assert.Equal("Square cover", results[0].Title);
        Assert.Equal("source.example", results[0].SourcePageUri!.Host);
        Assert.Equal(".png", results[0].FileExtension);
        Assert.Equal(2, requests.Count);
        Assert.Contains("Ridge%20Racer%20PlayStation%20game%20box%20art%20cover", requests[0].Query);
        Assert.Contains("vqd=123-456", requests[1].Query);
    }

    [Fact]
    public async Task SearchAsync_MissingTokenFailsWithActionableError()
    {
        using var httpClient = new HttpClient(new DelegateHandler(_ =>
            TextResponse("<html>No image token</html>", "text/html")));
        var provider = new DuckDuckGoArtworkSearchProvider(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => provider.SearchAsync("Example", "Dreamcast", 0.708));

        Assert.Contains("search token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchAsync_FiltersResultsWhoseImageOrThumbnailAddressIsBlocked()
    {
        using var httpClient = new HttpClient(new DelegateHandler(request =>
            request.RequestUri!.AbsolutePath == "/"
                ? TextResponse("<script>var vqd=\"123-456\";</script>", "text/html")
                : TextResponse(
                    """
                    {
                      "results": [
                        {
                          "image": "https://covers.example/safe.png",
                          "thumbnail": "https://thumbs.example/safe.png",
                          "width": 600,
                          "height": 900
                        },
                        {
                          "image": "https://127.0.0.1/private.png",
                          "thumbnail": "https://thumbs.example/private.png",
                          "width": 600,
                          "height": 900
                        }
                      ]
                    }
                    """,
                    "application/json")));
        var provider = new DuckDuckGoArtworkSearchProvider(
            httpClient,
            new PredicateUriPolicy(uri => uri.Host != "127.0.0.1"));

        var results = await provider.SearchAsync("Example", "PlayStation", 0.7);

        Assert.Single(results);
        Assert.Equal("covers.example", results[0].ImageUri.Host);
    }

    [Theory]
    [InlineData("https://example.test/cover", ".jpg")]
    [InlineData("https://example.test/cover.JPEG?size=large", ".jpeg")]
    [InlineData("https://example.test/cover.webp", ".webp")]
    public void FileExtension_UsesSupportedSuffixOrSafeFallback(string uri, string expected)
    {
        Assert.Equal(expected, DuckDuckGoArtworkSearchProvider.FileExtension(new Uri(uri)));
    }

    private static HttpResponseMessage TextResponse(string body, string mediaType) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, mediaType),
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> response) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class PredicateUriPolicy(Func<Uri, bool> predicate) : IRemoteArtworkUriPolicy
    {
        public Task<bool> IsAllowedAsync(
            Uri uri,
            CancellationToken cancellationToken = default) => Task.FromResult(predicate(uri));
    }
}
