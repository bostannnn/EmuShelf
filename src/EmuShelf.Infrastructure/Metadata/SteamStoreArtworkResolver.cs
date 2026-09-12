using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>Public Steam store assets: no account or Web API key is required.</summary>
public sealed class SteamStoreArtworkResolver(HttpClient http) : IGameArtworkResolver
{
    public async Task<IReadOnlyList<ArtworkCandidate>> ResolveAsync(string systemId,
        IReadOnlyList<GameIdentifier> identifiers, CancellationToken cancellationToken = default)
    {
        if (systemId != "steam" || !int.TryParse(
            identifiers.FirstOrDefault(i => i.Kind == GameIdentifierKind.SteamAppId)?.Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId <= 0) return [];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var input = """{"ids":[{"appid":$APPID}],"context":{"language":"english","country_code":"US"},"data_request":{"include_assets":true}}""".Replace("$APPID", appId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            using var response = await http.GetAsync(
                "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(input),
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024) return [];
                buffer.Write(chunk, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            return Parse(document.RootElement, appId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return []; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
        { return []; }
    }

    private static IReadOnlyList<ArtworkCandidate> Parse(JsonElement root, int appId)
    {
        var items = root.GetProperty("response").GetProperty("store_items");
        foreach (var item in items.EnumerateArray())
        {
            if (item.GetProperty("appid").GetInt32() != appId || item.GetProperty("success").GetInt32() != 1 ||
                !item.TryGetProperty("assets", out var assets)) continue;
            var format = assets.GetProperty("asset_url_format").GetString();
            if (format is null || !format.StartsWith($"steam/apps/{appId}/", StringComparison.Ordinal) ||
                !format.Contains("${FILENAME}", StringComparison.Ordinal)) return [];
            var result = new List<ArtworkCandidate>();
            foreach (var key in new[] { "library_capsule_2x", "library_capsule", "header_2x", "header" })
            {
                if (!assets.TryGetProperty(key, out var field) || field.GetString() is not { Length: > 0 } filename) continue;
                var relative = format.Replace("${FILENAME}", filename, StringComparison.Ordinal);
                var path = relative.Split('?')[0];
                // Only publisher image paths for the requested app may reach the downloader.
                if (!Regex.IsMatch(path, $@"^steam/apps/{appId}/(?:[a-fA-F0-9]{{40}}/)?[a-zA-Z0-9_.-]+\.(?:jpg|jpeg|png|webp)$")) continue;
                var uri = new Uri("https://shared.fastly.steamstatic.com/store_item_assets/" + relative);
                result.Add(new("steam", uri, Path.GetExtension(path)));
            }
            return result.DistinctBy(candidate => candidate.SourceUri).ToArray();
        }
        return [];
    }
}
