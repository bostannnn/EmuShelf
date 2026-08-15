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
        string? filenameHint = null,
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
            if (index.TryGetValue(identifier.Kind, key, filenameHint, out var entry))
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
            _ => new Dictionary<string, List<CatalogEntry>>(StringComparer.OrdinalIgnoreCase));
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
                        Accumulate(entriesByKind[kind], normalizedKey, new CatalogEntry(name, region));
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

        return BuildIndex(distinctKinds, entriesByKind);
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
        var entries = new Dictionary<string, List<CatalogEntry>>(StringComparer.OrdinalIgnoreCase);

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
            Accumulate(entries, NormalizeKey(keyKind, name), new CatalogEntry(title, null));
        }

        return BuildIndex(
            [keyKind],
            new Dictionary<GameIdentifierKind, Dictionary<string, List<CatalogEntry>>>
            {
                [keyKind] = entries,
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

    // Every game that carries a key is kept, in first-seen order. Most keys are unique, but a
    // region-free cartridge's serial (a late 3DS Pokémon title, say) is shared by every regional
    // dump, so the region is only decidable at query time against the game's own filename.
    private static void Accumulate(
        Dictionary<string, List<CatalogEntry>> entries,
        string key,
        CatalogEntry candidate)
    {
        if (!entries.TryGetValue(key, out var list))
        {
            list = [];
            entries[key] = list;
        }
        list.Add(candidate);
    }

    private static CatalogIndex BuildIndex(
        IReadOnlyList<GameIdentifierKind> orderedKinds,
        IReadOnlyDictionary<GameIdentifierKind, Dictionary<string, List<CatalogEntry>>> entriesByKind)
    {
        var byKind = entriesByKind.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, IReadOnlyList<CatalogEntry>>)pair.Value.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<CatalogEntry>)entry.Value,
                StringComparer.OrdinalIgnoreCase));

        var primary = byKind[orderedKinds[0]];
        var entries = primary.ToDictionary(
            entry => entry.Key,
            entry => PreferredEntry(entry.Value),
            StringComparer.OrdinalIgnoreCase);
        return new CatalogIndex(entries, byKind);
    }

    // The region-agnostic pick, kept identical to the historical behaviour: the lowest preference
    // score, ties broken by first-seen order. This is what a caller with no region hint still gets.
    private static CatalogEntry PreferredEntry(IReadOnlyList<CatalogEntry> candidates)
    {
        var best = candidates[0];
        var bestScore = PreferenceScore(best.Title);
        for (var i = 1; i < candidates.Count; i++)
        {
            var score = PreferenceScore(candidates[i].Title);
            if (score < bestScore)
            {
                best = candidates[i];
                bestScore = score;
            }
        }
        return best;
    }

    // Among entries sharing a key, prefer one whose region the filename advertises; otherwise fall
    // back to the region-agnostic preferred entry. The filename's parenthetical tags include both
    // the region ("(Europe)") and language codes ("(En,Ja,Fr,…)"); DAT regions are spelled-out
    // words that never collide with the two-letter language codes, so a token intersection is safe.
    //
    // Disc and revision are settled first, because neither is decidable by region or by
    // PreferenceScore: every disc of a multi-disc title shares one product number, and its DAT
    // entries differ only in a "(Disc N)" suffix of identical length, so the score ties and the
    // first-seen entry — always Disc 1 — used to win for all of them.
    private static CatalogEntry SelectEntry(IReadOnlyList<CatalogEntry> candidates, string? filenameHint)
    {
        if (candidates.Count == 1)
            return candidates[0];

        candidates = NarrowBy(candidates, filenameHint, DiscMarkerRegex());
        candidates = NarrowBy(candidates, filenameHint, RevisionRegex());
        if (candidates.Count == 1)
            return candidates[0];

        var hintRegions = RegionTokens(FilenameTags(filenameHint));
        if (hintRegions.Count > 0)
        {
            CatalogEntry? best = null;
            var bestScore = int.MaxValue;
            foreach (var candidate in candidates)
            {
                if (!RegionTokens(candidate.Region).Overlaps(hintRegions))
                    continue;
                var score = PreferenceScore(candidate.Title);
                if (best is null || score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            if (best is not null)
                return best;
        }
        return PreferredEntry(candidates);
    }

    // Keeps the candidates whose own marker matches the filename's. An absent marker on both sides
    // counts as a match, so a plain dump still prefers the plain entry over a revision. When nothing
    // matches — the DAT numbers its discs but the filename does not, say — this field cannot decide
    // and every candidate stays in the running for the next one.
    private static IReadOnlyList<CatalogEntry> NarrowBy(
        IReadOnlyList<CatalogEntry> candidates,
        string? filenameHint,
        Regex marker)
    {
        if (candidates.Count == 1)
            return candidates;

        var hint = MarkerValue(marker, filenameHint);
        var matching = candidates
            .Where(candidate => string.Equals(
                MarkerValue(marker, candidate.Title),
                hint,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matching.Length > 0 ? matching : candidates;
    }

    private static string? MarkerValue(Regex marker, string? value) =>
        value is not null && marker.Match(value) is { Success: true } match
            ? match.Groups["value"].Value
            : null;

    private static IEnumerable<string> FilenameTags(string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return [];
        return ParentheticalTagRegex().Matches(filename).Select(match => match.Groups["tag"].Value);
    }

    private static readonly char[] RegionSeparators = [',', '/', '&', '+'];

    private static HashSet<string> RegionTokens(string? region) =>
        RegionTokens(region is null ? [] : [region]);

    private static HashSet<string> RegionTokens(IEnumerable<string> tags)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            foreach (var piece in tag.Split(
                         RegionSeparators,
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                tokens.Add(piece.ToUpperInvariant());
            }
        }
        return tokens;
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
        IReadOnlyDictionary<GameIdentifierKind, IReadOnlyDictionary<string, IReadOnlyList<CatalogEntry>>> EntriesByKind)
    {
        // Hintless lookup: the preferred entry for the key. Asking without naming a disc or a
        // revision is itself an answer — an entry carrying neither wins over one that does — and
        // among equals this is the historical region-agnostic pick.
        public bool TryGetValue(GameIdentifierKind kind, string key, out CatalogEntry entry) =>
            TryGetValue(kind, key, filenameHint: null, out entry);

        // Filename-aware lookup: when a key is shared by several releases, the hint (the game's
        // filename) selects the matching disc, revision and region; otherwise the preferred entry
        // is returned.
        public bool TryGetValue(
            GameIdentifierKind kind,
            string key,
            string? filenameHint,
            out CatalogEntry entry)
        {
            if (EntriesByKind.TryGetValue(kind, out var entries) &&
                entries.TryGetValue(key, out var candidates) &&
                candidates.Count > 0)
            {
                entry = SelectEntry(candidates, filenameHint);
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

    [GeneratedRegex(@"[\(\[]\s*(?<tag>[^\)\]]+?)\s*[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex ParentheticalTagRegex();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?:disc|disk|cd)\s*(?<value>[0-9]+)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiscMarkerRegex();

    // Redump writes "(Rev 1)", No-Intro also uses letters ("(Rev A)"). The trailing boundary keeps
    // an ordinary word such as "Revenge" from reading as revision "e".
    [GeneratedRegex(
        @"(?<![A-Za-z])rev(?:ision)?\s*(?<value>[0-9]+|[A-Z])(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RevisionRegex();
}
