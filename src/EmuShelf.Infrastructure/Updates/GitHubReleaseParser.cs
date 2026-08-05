using System.Text.Json;
using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>
/// Pure parsing helpers for the GitHub Releases API and the checksum files EmuShelf publishes. Kept
/// separate from the HTTP service so they can be unit tested against fixtures without a network call.
/// </summary>
public static class GitHubReleaseParser
{
    /// <summary>
    /// Parses a <c>/releases/latest</c> JSON body into a <see cref="ReleaseInfo"/>, or null when the
    /// tag is not a parseable <c>vX.Y.Z</c> version. Malformed JSON throws <see cref="JsonException"/>.
    /// </summary>
    public static ReleaseInfo? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag) || !SemanticVersion.TryParse(tag, out var version))
            return null;

        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;
        DateTimeOffset? publishedAt =
            root.TryGetProperty("published_at", out var publishedElement) &&
            publishedElement.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(publishedElement.GetString(), out var parsed)
                ? parsed
                : null;

        var assets = new List<UpdateAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement)
                    ? urlElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                    continue;
                var size = asset.TryGetProperty("size", out var sizeElement) &&
                    sizeElement.TryGetInt64(out var bytes)
                        ? bytes
                        : 0;
                assets.Add(new UpdateAsset(name, url, size));
            }
        }

        return new ReleaseInfo(tag.Trim(), version, notes, publishedAt, assets);
    }

    /// <summary>
    /// Extracts the hex digest from a <c>sha256sum</c>/<c>shasum</c>-style line ("&lt;hash&gt;  file").
    /// Returns null when the content has no leading 64-character hex token.
    /// </summary>
    public static string? ParseChecksum(string checksumFileContent)
    {
        if (string.IsNullOrWhiteSpace(checksumFileContent))
            return null;

        var firstToken = checksumFileContent
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (firstToken is null || firstToken.Length != 64)
            return null;

        return firstToken.All(Uri.IsHexDigit) ? firstToken.ToLowerInvariant() : null;
    }
}
