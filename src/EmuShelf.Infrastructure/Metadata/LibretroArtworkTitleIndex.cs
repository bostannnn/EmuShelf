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
        var region = NormalizeRegion(preferredRegion) ?? requested.Region;
        var exact = index.Where(entry => entry.Title.Key == requested.Key).ToArray();
        if (exact.Length > 0)
            return OrderByPreference(exact, region);

        // A publisher possessive ("Disney's Donald Duck…", "Tom Clancy's…") is carried by one
        // source and dropped by the other often enough to lose an otherwise certain match.
        // Comparing the possessive-free forms recovers those without loosening the comparison
        // into a prefix or substring search, which would mis-key sequels and spin-offs.
        var relaxed = index.Where(entry => SharesPossessiveFreeKey(entry.Title, requested)).ToArray();
        return relaxed.Length > 0 ? OrderByPreference(relaxed, region) : [];
    }

    /// <summary>
    /// True when the two titles agree once a leading publisher possessive is dropped from either
    /// side. The comparison stays a whole-key equality, so it cannot match a prefix or a sequel.
    /// </summary>
    private static bool SharesPossessiveFreeKey(NormalizedTitle indexed, NormalizedTitle requested) =>
        (indexed.AlternateKey is not null &&
         (indexed.AlternateKey == requested.Key || indexed.AlternateKey == requested.AlternateKey)) ||
        (requested.AlternateKey is not null && requested.AlternateKey == indexed.Key);

    private static IReadOnlyList<IndexedTitle> OrderByPreference(
        IEnumerable<IndexedTitle> matches,
        string? preferredRegion) =>
        matches
            .OrderByDescending(entry => string.Equals(
                entry.Title.Region,
                NormalizeRegion(preferredRegion),
                StringComparison.Ordinal))
            .ThenBy(entry => VariantPenalty(entry.FilenameWithoutExtension))
            .ThenBy(entry => entry.FilenameWithoutExtension, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// Ranks a kiosk demo, prototype, or control-scheme hack below the retail release it shares a
    /// title with. Without this the tie is broken alphabetically, which picks whichever tag sorts
    /// first rather than the release the user actually owns.
    /// </summary>
    private static int VariantPenalty(string filename)
    {
        var penalty = 0;
        foreach (var marker in VariantMarkers)
        {
            if (filename.Contains(marker, StringComparison.OrdinalIgnoreCase))
                penalty++;
        }
        return penalty;
    }

    private static readonly string[] VariantMarkers =
    [
        "(Demo", "(Kiosk", "(Beta", "(Proto", "(Sample", "(Rev ", "(DPAD",
        "(Unl", "(Aftermarket", "(Pirate", "(Debug", "(Program",
    ];

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

    internal sealed record NormalizedTitle(
        string Key,
        string[] Tokens,
        string? Region,
        string? AlternateKey = null)
    {
        public static NormalizedTitle From(string value)
        {
            // A dump tagged by a scene or romhack tool carries its version ahead of the release
            // tags ("Crazy Taxi v1.004 (1999)(Sega)"), where an artwork filename never does.
            var productTitle = VersionSuffixRegex().Replace(value.Split(['(', '['], 2)[0], string.Empty);
            var tokens = Tokenize(productTitle);
            var possessiveFree = PossessivePrefixRegex().Replace(productTitle, string.Empty);
            var alternateTokens = possessiveFree.Length == productTitle.Length
                ? []
                : Tokenize(possessiveFree);
            return new NormalizedTitle(
                string.Join(' ', tokens),
                tokens,
                FindRegion(value),
                alternateTokens.Length > 0 ? string.Join(' ', alternateTokens) : null);
        }

        private static string[] Tokenize(string value) => TokenRegex()
            .Matches(value)
            .Select(match => match.Value.ToUpperInvariant())
            .ToArray();
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

        // The single letters are GoodTools codes, which appear in older filenames ("(U)", "(J)")
        // where a DAT would spell the region out. They only bias ordering between equally valid
        // matches, so treating them as regions cannot turn a miss into a wrong cover.
        return value.Trim().ToUpperInvariant() switch
        {
            "USA" or "US" or "U" or "NORTH AMERICA" => "USA",
            "EUROPE" or "EU" or "E" => "EUROPE",
            "JAPAN" or "JP" or "J" => "JAPAN",
            "KOREA" or "K" => "KOREA",
            "BRAZIL" or "B" => "BRAZIL",
            "AUSTRALIA" or "A" => "AUSTRALIA",
            "WORLD" or "W" => "WORLD",
            _ => null,
        };
    }

    [GeneratedRegex("href=\"(?<name>[^\"]+\\.png)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("[\\p{L}\\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[\(\[]\s*(?<tag>[^\)\]]+)\s*[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalTagRegex();

    /// <summary>Leading words up to and including a possessive one, such as "Tom Clancy's ".</summary>
    [GeneratedRegex(
        @"^(?:[\p{L}\p{N}][\p{L}\p{N}.'’-]*\s+)*?[\p{L}\p{N}][\p{L}\p{N}.-]*['’]s\s+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PossessivePrefixRegex();

    [GeneratedRegex(@"\s+v\d+(?:\.\d+)*\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex VersionSuffixRegex();
}
