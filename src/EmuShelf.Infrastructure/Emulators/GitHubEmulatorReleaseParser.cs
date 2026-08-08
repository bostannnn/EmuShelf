using System.Text.Json;

namespace EmuShelf.Infrastructure.Emulators;

/// <summary>
/// Pure parsing of the GitHub Releases API for the emulator install manager. Kept separate from the HTTP
/// client so it can be unit tested against fixtures. Unlike <c>GitHubReleaseParser</c> (the app updater) it
/// does not require a semantic-version tag, because emulator tags are often not semver.
/// </summary>
public static class GitHubEmulatorReleaseParser
{
    /// <summary>
    /// Parses a <c>/releases/latest</c> JSON body into a <see cref="GitHubEmulatorRelease"/>, or null when
    /// the body has no usable <c>tag_name</c>. Malformed JSON throws <see cref="JsonException"/>.
    /// </summary>
    public static GitHubEmulatorRelease? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        DateTimeOffset? publishedAt =
            root.TryGetProperty("published_at", out var publishedElement) &&
            publishedElement.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(publishedElement.GetString(), out var parsed)
                ? parsed
                : null;

        var assets = new List<GitHubEmulatorReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) &&
            assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() : null;
                var url = asset.TryGetProperty("browser_download_url", out var au) ? au.GetString() : null;
                if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(url))
                    continue;
                var size = asset.TryGetProperty("size", out var se) && se.TryGetInt64(out var bytes) ? bytes : 0;
                assets.Add(new GitHubEmulatorReleaseAsset(assetName, url, size));
            }
        }

        return new GitHubEmulatorRelease(tag.Trim(), name, publishedAt, assets);
    }
}
