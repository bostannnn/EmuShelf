using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml;
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
            .Where(identifier => profile.CatalogKeyKinds.Contains(identifier.Kind))
            .OrderBy(identifier => CatalogKeyPriority(profile.CatalogKeyKinds, identifier.Kind))
            .ThenByDescending(identifier => identifier.IsPrimary)
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
            if (index.TryGetValue(identifier.Kind, key, out var entry))
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
        var maxBytes = profile.MaxCatalogBytes ?? MaximumCatalogBytes;
        await EnsureCurrentCatalogAsync(profile.CatalogUri, path, maxBytes, cancellationToken);

        return await Task.Run(
            () => profile.CatalogFormat == DatFormat.LogiqxXml
                ? ParseLogiqxXml(path, profile.CatalogKeyKind)
                : Parse(path, profile.CatalogKeyKinds, profile.ReadRomSerials),
            cancellationToken);
    }

    private async Task EnsureCurrentCatalogAsync(
        Uri uri,
        string path,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (IsCurrent(path))
            return;

        var downloadLock = DownloadLocks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
        await downloadLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrent(path))
                await DownloadCatalogAsync(uri, path, maxBytes, cancellationToken);
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
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
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
                await CopyWithLimitAsync(source, destination, maxBytes, cancellationToken);
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
        => Parse(reader, [keyKind], readRomSerials: false);

    internal static CatalogIndex Parse(
        string path,
        IReadOnlyList<GameIdentifierKind> keyKinds,
        bool readRomSerials)
    {
        using var reader = File.OpenText(path);
        return Parse(reader, keyKinds, readRomSerials);
    }

    internal static CatalogIndex Parse(
        TextReader reader,
        IReadOnlyList<GameIdentifierKind> keyKinds,
        bool readRomSerials)
    {
        var distinctKinds = keyKinds.Distinct().ToArray();
        if (distinctKinds.Length == 0)
            throw new ArgumentException("At least one catalogue key kind is required.", nameof(keyKinds));

        var entriesByKind = distinctKinds.ToDictionary(
            kind => kind,
            _ => new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase));
        var depth = 0;
        string? name = null;
        string? region = null;
        Dictionary<GameIdentifierKind, string?>? keys = null;

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
                    keys = distinctKinds.ToDictionary(kind => kind, _ => (string?)null);
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
                if (depth == 0 && name is not null && keys is not null)
                {
                    foreach (var kind in distinctKinds)
                    {
                        if (keys[kind] is not { } key)
                            continue;

                        var normalizedKey = NormalizeKey(kind, key);
                        AddPreferred(entriesByKind[kind], normalizedKey, new CatalogEntry(name, region));
                    }
                }
                continue;
            }

            // A clrmamepro ROM record may keep its checksum on the `rom (` line or on a
            // nested line. SHA-1/CRC are ROM-content keys, unlike the top-level disc serial.
            if (keys is null)
                continue;

            if (keys.ContainsKey(GameIdentifierKind.Sha1) &&
                TryReadTokenField(value, "sha1", out var parsedSha1))
            {
                keys[GameIdentifierKind.Sha1] ??= parsedSha1;
            }
            else if (keys.ContainsKey(GameIdentifierKind.Crc32) &&
                     TryReadTokenField(value, "crc", out var parsedCrc))
            {
                keys[GameIdentifierKind.Crc32] ??= parsedCrc;
            }

            // `depth == 1` does not identify a game-level field: clrmamepro writes the whole
            // `rom ( … )` record on one line, so it sits at depth 1 alongside `name` and `serial`.
            // A serial inside that record describes the ROM, not the game, and only a profile
            // that opted in may key on it.
            var isGameLevelField = depth == 1 && !IsRecordLine(value, "rom");
            if (keys.ContainsKey(GameIdentifierKind.Serial) &&
                (isGameLevelField || readRomSerials) &&
                TryReadEmbeddedQuotedField(value, "serial", out var parsedSerial))
            {
                keys[GameIdentifierKind.Serial] ??= parsedSerial;
            }

            if (depth != 1)
                continue;

            if (TryReadQuotedField(value, "name", out var parsedName))
                name = parsedName;
            else if (TryReadQuotedField(value, "region", out var parsedRegion))
                region = parsedRegion;
        }

        var readonlyIndexes = entriesByKind.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, CatalogEntry>)pair.Value);
        return new CatalogIndex(readonlyIndexes[distinctKinds[0]], readonlyIndexes);
    }

    // The FinalBurn Neo arcade DAT is Logiqx XML, not clrmamepro text. Its game `name` attribute is
    // the romset short id (the zip basename FBNeo loads by), and the human title is a `description`
    // element — the inverse of the console DATs, where the game name *is* the title. BIOS and device
    // archives (isbios/isdevice, e.g. neogeo) are skipped so they never become library entries.
    internal static CatalogIndex ParseLogiqxXml(string path, GameIdentifierKind keyKind)
    {
        using var stream = File.OpenRead(path);
        var settings = CreateXmlSettings();
        using var reader = XmlReader.Create(stream, settings);
        return ParseLogiqxXml(reader, keyKind);
    }

    internal static CatalogIndex ParseLogiqxXml(TextReader textReader, GameIdentifierKind keyKind)
    {
        var settings = CreateXmlSettings();
        using var reader = XmlReader.Create(textReader, settings);
        return ParseLogiqxXml(reader, keyKind);
    }

    private static XmlReaderSettings CreateXmlSettings() => new()
    {
        // The DAT declares a DOCTYPE pointing at logiqx.com; never fetch it.
        DtdProcessing = DtdProcessing.Ignore,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = true,
        CloseInput = false,
    };

    private static CatalogIndex ParseLogiqxXml(XmlReader reader, GameIdentifierKind keyKind)
    {
        var entries = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                (!reader.Name.Equals("game", StringComparison.Ordinal) &&
                 !reader.Name.Equals("machine", StringComparison.Ordinal)))
            {
                continue;
            }

            var name = reader.GetAttribute("name");
            var isBios = reader.GetAttribute("isbios");
            var isDevice = reader.GetAttribute("isdevice");
            var runnable = reader.GetAttribute("runnable");
            var description = ReadDescription(reader);

            if (string.IsNullOrWhiteSpace(name) ||
                IsYes(isBios) ||
                IsYes(isDevice) ||
                string.Equals(runnable, "no", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(description) ? name : description.Trim();
            AddPreferred(entries, NormalizeKey(keyKind, name), new CatalogEntry(title, null));
        }

        IReadOnlyDictionary<string, CatalogEntry> readonlyEntries = entries;
        return new CatalogIndex(
            readonlyEntries,
            new Dictionary<GameIdentifierKind, IReadOnlyDictionary<string, CatalogEntry>>
            {
                [keyKind] = readonlyEntries,
            });
    }

    /// <summary>
    /// Reads the current game element's <c>description</c> child, draining the element's subtree so
    /// the outer reader is left positioned at the game's end tag.
    /// </summary>
    private static string? ReadDescription(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return null;

        string? description = null;
        using var sub = reader.ReadSubtree();
        sub.Read();
        while (sub.Read())
        {
            if (sub.NodeType == XmlNodeType.Element &&
                sub.Name.Equals("description", StringComparison.Ordinal))
            {
                description = sub.ReadElementContentAsString();
            }
        }
        return description;
    }

    private static bool IsYes(string? value) =>
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeKey(GameIdentifierKind kind, string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (kind == GameIdentifierKind.Serial && PlayStationSerialRegex().Match(normalized) is
            { Success: true } match)
        {
            return $"{match.Groups[1].Value}-{match.Groups[2].Value}{match.Groups[3].Value}";
        }
        if (kind == GameIdentifierKind.Serial)
            return string.Concat(normalized.Where(char.IsLetterOrDigit));
        return normalized;
    }

    private static int CatalogKeyPriority(
        IReadOnlyList<GameIdentifierKind> keyKinds,
        GameIdentifierKind kind)
    {
        for (var index = 0; index < keyKinds.Count; index++)
        {
            if (keyKinds[index] == kind)
                return index;
        }
        return int.MaxValue;
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

    /// <summary>True when the line opens or contains a whole clrmamepro record of this kind.</summary>
    private static bool IsRecordLine(string line, string record) =>
        line.StartsWith(record, StringComparison.OrdinalIgnoreCase) &&
        (line.Length == record.Length ||
         line[record.Length] == '(' ||
         char.IsWhiteSpace(line[record.Length]));

    private static bool TryReadEmbeddedQuotedField(string line, string field, out string value)
    {
        var match = Regex.Match(
            line,
            $@"(?:^|\s){Regex.Escape(field)}\s+""((?:\\.|[^""])*)""",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            value = string.Empty;
            return false;
        }

        value = match.Groups[1].Value
            .Replace("\\\"", "\"", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
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

    internal sealed record CatalogIndex(
        IReadOnlyDictionary<string, CatalogEntry> Entries,
        IReadOnlyDictionary<GameIdentifierKind, IReadOnlyDictionary<string, CatalogEntry>> EntriesByKind)
    {
        public bool TryGetValue(GameIdentifierKind kind, string key, out CatalogEntry entry)
        {
            if (EntriesByKind.TryGetValue(kind, out var entries) &&
                entries.TryGetValue(key, out entry!))
            {
                return true;
            }

            entry = default!;
            return false;
        }
    }

    [GeneratedRegex(
        @"^([A-Z]{4})[\s_-]*([0-9]{3})[.\s_-]*([0-9]{2})(?:$|[-/])",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlayStationSerialRegex();
}
