using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>
/// Downloads a platform DAT only after metadata is requested, caches it in the
/// portable Cache directory, and indexes only the identifier/title fields EmuShelf uses.
/// </summary>
public sealed partial class LibretroDatCatalog : IGameMetadataCatalog
{
    private const long MaximumCatalogBytes = 12 * 1024 * 1024;
    private static readonly TimeSpan CatalogFreshness = TimeSpan.FromDays(30);

    private readonly HttpClient _httpClient;
    private readonly string _catalogDirectory;
    private readonly ConcurrentDictionary<string, Lazy<Task<CatalogIndex>>> _indexes =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DownloadLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public LibretroDatCatalog(IAppPaths paths, HttpClient httpClient)
    {
        _httpClient = httpClient;
        _catalogDirectory = Path.Combine(paths.CacheDirectory, "Metadata", "Catalogs");
        Directory.CreateDirectory(_catalogDirectory);
    }

    public async Task<GameCatalogMatch?> FindMatchAsync(
        MetadataSystemProfile profile,
        IReadOnlyList<GameIdentifier> identifiers,
        CancellationToken cancellationToken = default)
    {
        var relevant = identifiers
            .Where(identifier => identifier.Kind == profile.CatalogKeyKind)
            .OrderByDescending(identifier => identifier.IsPrimary)
            .ToArray();
        if (relevant.Length == 0)
            return null;

        var lazy = _indexes.GetOrAdd(
            profile.SystemId,
            _ => new Lazy<Task<CatalogIndex>>(
                () => LoadIndexAsync(profile, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        CatalogIndex index;
        try
        {
            index = await lazy.Value.WaitAsync(cancellationToken);
        }
        catch
        {
            _indexes.TryRemove(profile.SystemId, out _);
            throw;
        }

        foreach (var identifier in relevant)
        {
            var key = NormalizeKey(identifier.Kind, identifier.Value);
            if (index.Entries.TryGetValue(key, out var entry))
            {
                return new GameCatalogMatch(
                    "libretro-database",
                    key,
                    entry.Title,
                    entry.Region);
            }
        }
        return null;
    }

    private async Task<CatalogIndex> LoadIndexAsync(
        MetadataSystemProfile profile,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_catalogDirectory, $"{profile.SystemId}.dat");
        await EnsureCurrentCatalogAsync(profile.CatalogUri, path, cancellationToken);

        return await Task.Run(() => Parse(path, profile.CatalogKeyKind), cancellationToken);
    }

    private async Task EnsureCurrentCatalogAsync(
        Uri uri,
        string path,
        CancellationToken cancellationToken)
    {
        if (IsCurrent(path))
            return;

        var downloadLock = DownloadLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(path))
                await DownloadCatalogAsync(uri, path, cancellationToken);
        }
        finally
        {
            downloadLock.Release();
        }
    }

    private static bool IsCurrent(string path) =>
        File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) <= CatalogFreshness;

    private async Task DownloadCatalogAsync(
        Uri uri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumCatalogBytes)
            throw new InvalidDataException("The metadata catalog is larger than EmuShelf's safety limit.");

        var tempPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var destination = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous))
            {
                await CopyWithLimitAsync(source, destination, MaximumCatalogBytes, cancellationToken);
            }
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    internal static CatalogIndex Parse(string path, GameIdentifierKind keyKind)
    {
        using var reader = File.OpenText(path);
        return Parse(reader, keyKind);
    }

    internal static CatalogIndex Parse(TextReader reader, GameIdentifierKind keyKind)
    {
        var entries = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var depth = 0;
        string? name = null;
        string? region = null;
        string? key = null;

        while (reader.ReadLine() is { } line)
        {
            var value = line.Trim();
            if (depth == 0)
            {
                if (value.Equals("game (", StringComparison.Ordinal))
                {
                    depth = 1;
                    name = null;
                    region = null;
                    key = null;
                }
                continue;
            }

            if (value.EndsWith('('))
            {
                depth++;
                continue;
            }

            if (value.Equals(")", StringComparison.Ordinal))
            {
                depth--;
                if (depth == 0 && name is not null && key is not null)
                {
                    var normalizedKey = NormalizeKey(keyKind, key);
                    AddPreferred(entries, normalizedKey, new CatalogEntry(name, region));
                }
                continue;
            }

            // A clrmamepro ROM record may keep its checksum on the `rom (` line or on a
            // nested line. SHA-1/CRC are ROM-content keys, unlike the top-level disc serial.
            if (keyKind == GameIdentifierKind.Sha1 &&
                TryReadTokenField(value, "sha1", out var parsedSha1))
            {
                key ??= parsedSha1;
            }
            else if (keyKind == GameIdentifierKind.Crc32 &&
                     TryReadTokenField(value, "crc", out var parsedCrc))
            {
                key ??= parsedCrc;
            }

            if (depth != 1)
                continue;

            if (TryReadQuotedField(value, "name", out var parsedName))
                name = parsedName;
            else if (TryReadQuotedField(value, "region", out var parsedRegion))
                region = parsedRegion;
            else if (keyKind != GameIdentifierKind.Sha1 &&
                     TryReadQuotedField(value, "serial", out var parsedSerial))
                key ??= parsedSerial;
        }

        return new CatalogIndex(entries);
    }

    internal static string NormalizeKey(GameIdentifierKind kind, string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (kind == GameIdentifierKind.Serial && PlayStationSerialRegex().Match(normalized) is
            { Success: true } match)
        {
            return $"{match.Groups[1].Value}-{match.Groups[2].Value}{match.Groups[3].Value}";
        }
        return normalized;
    }

    private static bool TryReadQuotedField(string line, string field, out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(field, StringComparison.Ordinal) ||
            line.Length <= field.Length ||
            !char.IsWhiteSpace(line[field.Length]))
        {
            return false;
        }

        var firstQuote = line.IndexOf('"', field.Length);
        var lastQuote = line.LastIndexOf('"');
        if (firstQuote < 0 || lastQuote <= firstQuote)
            return false;

        value = line[(firstQuote + 1)..lastQuote]
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
        return true;
    }

    private static bool TryReadTokenField(string line, string field, out string value)
    {
        var match = Regex.Match(
            line,
            $@"(?:^|\s){Regex.Escape(field)}\s+([^\s\)]+)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            value = string.Empty;
            return false;
        }

        value = match.Groups[1].Value;
        return true;
    }

    private static void AddPreferred(
        Dictionary<string, CatalogEntry> entries,
        string key,
        CatalogEntry candidate)
    {
        if (!entries.TryGetValue(key, out var existing) ||
            PreferenceScore(candidate.Title) < PreferenceScore(existing.Title))
        {
            entries[key] = candidate;
        }
    }

    private static int PreferenceScore(string title)
    {
        var score = title.Length;
        foreach (var marker in new[] { "(Beta", "(Proto", "(Demo", "(Sample", "(Rev " })
        {
            if (title.Contains(marker, StringComparison.OrdinalIgnoreCase))
                score += 10_000;
        }
        return score;
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long limit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var total = 0L;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            total += read;
            if (total > limit)
                throw new InvalidDataException("The metadata catalog exceeded EmuShelf's safety limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    internal sealed record CatalogEntry(string Title, string? Region);

    internal sealed record CatalogIndex(IReadOnlyDictionary<string, CatalogEntry> Entries);

    [GeneratedRegex(
        @"^([A-Z]{4})[\s_-]*([0-9]{3})[.\s_-]*([0-9]{2})(?:$|[-/])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlayStationSerialRegex();
}
