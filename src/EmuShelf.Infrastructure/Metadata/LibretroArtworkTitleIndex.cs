using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>
/// Caches Libretro's per-playlist boxart directory and resolves a catalog-verified title against
/// actual filenames. This avoids probing fabricated URLs when the DAT and artwork repositories
/// use different regional naming conventions.
/// </summary>
public sealed partial class LibretroArtworkTitleIndex : IGameArtworkTitleIndex
{
    private const long MaximumIndexBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan IndexFreshness = TimeSpan.FromDays(14);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<IndexedTitle>>>> _indexes =
        new(StringComparer.Ordinal);

    public LibretroArtworkTitleIndex(IAppPaths paths, HttpClient httpClient)
    {
        _httpClient = httpClient;
        _cacheDirectory = Path.Combine(paths.CacheDirectory, "Metadata", "ArtworkIndexes");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<IReadOnlyList<ArtworkCandidate>> FindCandidatesAsync(
        IArtworkTitleIndexProvider provider,
        GameCatalogMatch match,
        CancellationToken cancellationToken = default)
    {
        var index = await GetIndexAsync(provider.ArtworkIndexKey, cancellationToken);
        var matched = provider.GetIndexedTitleQueries(match)
            .Select(NormalizedTitle.From)
            .Where(requested => requested.Tokens.Length > 0)
            .SelectMany(requested => FindMatches(index, requested, match.Region))
            .DistinctBy(entry => entry.FilenameWithoutExtension, StringComparer.Ordinal)
            .ToArray();
        return matched
            .Select(entry => provider.CreateCandidate(entry.FilenameWithoutExtension))
            .DistinctBy(candidate => candidate.SourceUri)
            .ToArray();
    }

    private async Task<IReadOnlyList<IndexedTitle>> GetIndexAsync(
        string playlist,
        CancellationToken cancellationToken)
    {
        var lazy = _indexes.GetOrAdd(
            playlist,
            key => new Lazy<Task<IReadOnlyList<IndexedTitle>>>(
                () => LoadIndexAsync(key, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The shared load continues for other games. Removing it here would make a later
            // request download the same playlist a second time while this one is still running.
            throw;
        }
        catch
        {
            _indexes.TryRemove(playlist, out _);
            throw;
        }
    }

    private async Task<IReadOnlyList<IndexedTitle>> LoadIndexAsync(
        string playlist,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_cacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(playlist))) + ".html");
        if (!File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > IndexFreshness)
        {
            var uri = new Uri(
                $"https://thumbnails.libretro.com/{Uri.EscapeDataString(playlist)}/Named_Boxarts/");
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumIndexBytes)
                throw new InvalidDataException("The Libretro artwork index is larger than EmuShelf's safety limit.");

            var temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var destination = new FileStream(
                    temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    await CopyWithLimitAsync(source, destination, cancellationToken);
                }
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }

        return await Task.Run(() => Parse(File.ReadAllText(path)), cancellationToken);
    }

    internal static IReadOnlyList<IndexedTitle> Parse(string html) => LinkRegex()
        .Matches(html)
        .Select(match => Uri.UnescapeDataString(WebUtility.HtmlDecode(match.Groups["name"].Value)))
        .Where(name => name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        .Select(name => Path.GetFileNameWithoutExtension(name))
        .Distinct(StringComparer.Ordinal)
        .Select(name => new IndexedTitle(name, NormalizedTitle.From(name)))
        .Where(entry => entry.Title.Tokens.Length > 0)
        .ToArray();

    internal static IReadOnlyList<IndexedTitle> FindMatches(
        IReadOnlyList<IndexedTitle> index,
        NormalizedTitle requested,
        string? preferredRegion = null)
    {
        var exact = index.Where(entry => entry.Title.Key == requested.Key).ToArray();
        if (exact.Length > 0)
            return OrderByRegion(exact, NormalizeRegion(preferredRegion) ?? requested.Region);

        return [];
    }

    private static IReadOnlyList<IndexedTitle> OrderByRegion(
        IEnumerable<IndexedTitle> matches,
        string? preferredRegion) =>
        matches
            .OrderByDescending(entry => string.Equals(
                entry.Title.Region,
                NormalizeRegion(preferredRegion),
                StringComparison.Ordinal))
            .ThenBy(entry => entry.FilenameWithoutExtension, StringComparer.Ordinal)
            .ToArray();

    private static async Task CopyWithLimitAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var total = 0L;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return;
            total += read;
            if (total > MaximumIndexBytes)
                throw new InvalidDataException("The Libretro artwork index exceeded EmuShelf's safety limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    internal sealed record IndexedTitle(string FilenameWithoutExtension, NormalizedTitle Title);

    internal sealed record NormalizedTitle(string Key, string[] Tokens, string? Region)
    {
        public static NormalizedTitle From(string value)
        {
            var productTitle = value.Split(['(', '['], 2)[0];
            var tokens = TokenRegex().Matches(productTitle)
                .Select(match => match.Value.ToUpperInvariant())
                .ToArray();
            return new NormalizedTitle(string.Join(' ', tokens), tokens, FindRegion(value));
        }
    }

    private static string? FindRegion(string value)
    {
        foreach (Match match in ParentheticalTagRegex().Matches(value))
        {
            var region = NormalizeRegion(match.Groups["tag"].Value);
            if (region is not null)
                return region;
        }
        return null;
    }

    private static string? NormalizeRegion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToUpperInvariant() switch
        {
            "USA" or "US" or "NORTH AMERICA" => "USA",
            "EUROPE" or "EU" => "EUROPE",
            "JAPAN" or "JP" => "JAPAN",
            "KOREA" => "KOREA",
            "BRAZIL" => "BRAZIL",
            "AUSTRALIA" => "AUSTRALIA",
            "WORLD" => "WORLD",
            _ => null,
        };
    }

    [GeneratedRegex("href=\"(?<name>[^\"]+\\.png)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[\(\[]\s*(?<tag>[^\)\]]+)\s*[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalTagRegex();
}
