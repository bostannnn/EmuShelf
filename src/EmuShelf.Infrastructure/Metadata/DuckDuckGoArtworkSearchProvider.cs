using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>
/// User-driven image search matching Grimmory's cover-picker approach. DuckDuckGo exposes no
/// stable public image-search API, so failures are surfaced to the picker and never affect the
/// automatic metadata pipeline.
/// </summary>
public sealed partial class DuckDuckGoArtworkSearchProvider : IGameArtworkSearchProvider
{
    private const int MaximumResults = 24;
    private readonly HttpClient _httpClient;
    private readonly IRemoteArtworkUriPolicy? _uriPolicy;

    public DuckDuckGoArtworkSearchProvider(
        HttpClient httpClient,
        IRemoteArtworkUriPolicy? uriPolicy = null)
    {
        _httpClient = httpClient;
        _uriPolicy = uriPolicy;
    }

    public async Task<IReadOnlyList<ArtworkSearchResult>> SearchAsync(
        string title,
        string systemName,
        double preferredAspectRatio,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);

        var query = $"{title.Trim()} {systemName.Trim()} game box art cover";
        var encodedQuery = Uri.EscapeDataString(query);
        var searchUri = new Uri(
            $"https://duckduckgo.com/?q={encodedQuery}&iax=images&ia=images");

        using var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUri);
        searchRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        searchRequest.Headers.Referrer = new Uri("https://duckduckgo.com/");
        using var searchResponse = await _httpClient.SendAsync(searchRequest, cancellationToken);
        searchResponse.EnsureSuccessStatusCode();
        var html = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
        var token = ExtractSearchToken(html);
        if (token is null)
            throw new InvalidDataException("The web image search did not return a search token.");

        var resultsUri = new Uri(
            $"https://duckduckgo.com/i.js?o=json&q={encodedQuery}&vqd={Uri.EscapeDataString(token)}" +
            "&f=,,,&p=1");
        using var resultsRequest = new HttpRequestMessage(HttpMethod.Get, resultsUri);
        resultsRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        resultsRequest.Headers.Referrer = searchUri;
        resultsRequest.Headers.TryAddWithoutValidation("x-requested-with", "XMLHttpRequest");
        resultsRequest.Headers.TryAddWithoutValidation("x-vqd-4", token);
        using var resultsResponse = await _httpClient.SendAsync(resultsRequest, cancellationToken);
        resultsResponse.EnsureSuccessStatusCode();
        await using var resultsStream = await resultsResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(resultsStream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var parsed = new List<ArtworkSearchResult>();
        var seen = new HashSet<Uri>();
        foreach (var item in results.EnumerateArray())
        {
            if (!TryGetWebUri(item, "image", out var imageUri) || !seen.Add(imageUri))
                continue;

            var thumbnailUri = TryGetWebUri(item, "thumbnail", out var thumbnail)
                ? thumbnail
                : imageUri;
            _ = TryGetWebUri(item, "url", out var sourcePageUri);
            var width = GetPositiveInt(item, "width");
            var height = GetPositiveInt(item, "height");
            if (width < 200 || height < 200)
                continue;

            parsed.Add(new ArtworkSearchResult(
                "duckduckgo-image-search",
                imageUri,
                thumbnailUri,
                sourcePageUri,
                width,
                height,
                GetString(item, "title") ?? "Web image result",
                FileExtension(imageUri)));
        }

        // Search rank stays authoritative. Aspect-ratio closeness only breaks broad groups of
        // results so a square PS1 cover or portrait disc case rises without hiding alternatives.
        var ranked = parsed
            .Select((result, index) => new
            {
                Result = result,
                SearchBand = index / 6,
                RatioDistance = RatioDistance(result.AspectRatio, preferredAspectRatio),
                Index = index,
            })
            .OrderBy(item => item.SearchBand)
            .ThenBy(item => item.RatioDistance)
            .ThenBy(item => item.Index)
            .Take(MaximumResults)
            .Select(item => item.Result)
            .ToArray();

        if (_uriPolicy is null)
            return ranked;

        var checkedResults = await Task.WhenAll(ranked.Select(async result =>
            await IsAllowedAsync(result, cancellationToken) ? result : null));
        return checkedResults.OfType<ArtworkSearchResult>().ToArray();
    }

    private static string? ExtractSearchToken(string html)
    {
        var match = SearchTokenRegex().Match(html);
        return match.Success ? match.Groups["token"].Value : null;
    }

    private static bool TryGetWebUri(JsonElement item, string property, out Uri uri)
    {
        uri = null!;
        var value = GetString(item, property);
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsAllowedAsync(
        ArtworkSearchResult result,
        CancellationToken cancellationToken) =>
        await _uriPolicy!.IsAllowedAsync(result.ImageUri, cancellationToken) &&
        await _uriPolicy.IsAllowedAsync(result.ThumbnailUri, cancellationToken);

    private static string? GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetPositiveInt(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) && number > 0
            ? number
            : 0;

    private static double RatioDistance(double actual, double preferred) =>
        actual > 0 && preferred > 0
            ? Math.Abs(Math.Log(actual / preferred))
            : double.MaxValue;

    internal static string FileExtension(Uri uri)
    {
        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp"
            ? extension
            : ".jpg";
    }

    [GeneratedRegex("vqd\\s*(?:=|:)\\s*[\\\"'](?<token>[0-9-]+)[\\\"']", RegexOptions.IgnoreCase)]
    private static partial Regex SearchTokenRegex();
}
