using EmuShelf.Core.Metadata;

namespace EmuShelf.Core.TexturePacks;

/// <summary>
/// How one discovered pack relates to this library. These are presentation states, not health
/// judgements: <see cref="NoLibraryMatch"/> in particular says only that no imported game declares
/// the identifier the pack is keyed on, never that the pack is broken or safe to delete.
/// </summary>
public enum TexturePackEntryStatus
{
    /// <summary>Usable content whose key matches at least one imported game.</summary>
    Matched,

    /// <summary>Usable content keyed on an identifier no imported game declares.</summary>
    NoLibraryMatch,

    /// <summary>A usable emulator-wide pack that is not keyed on any single game.</summary>
    SharedPack,

    /// <summary>An identifier-shaped folder holding only dumps, or nothing loadable.</summary>
    EmptyOrDumpsOnly,

    /// <summary>A folder this emulator's loader would not recognize at all.</summary>
    UnrecognizedLayout,

    /// <summary>The texture root or this folder could not be read during the scan.</summary>
    FolderUnavailable,

    /// <summary>
    /// Usable content that cannot be judged yet: the library has not extracted any identifier of
    /// the kind this pack is keyed on, so calling it unmatched would be a guess.
    /// </summary>
    IdentifierPending,
}

/// <summary>One pack, its scan-time classification, and the games it was matched to.</summary>
public sealed record TexturePackClassification(
    string EmulatorId,
    string InstallationId,
    TexturePackInventoryEntry Entry,
    TexturePackEntryStatus Status,
    IReadOnlyList<long> MatchedGameIds)
{
    public string PackKey => Entry.PackKey;

    public string SourcePath => Entry.SourcePath;

    public string? Diagnostic => Entry.Diagnostic;
}

/// <summary>One matched pack as a single game sees it.</summary>
public sealed record TexturePackMatch(
    string EmulatorId,
    string InstallationId,
    string PackKey,
    string SourcePath,
    string MatchedIdentifier);

/// <summary>
/// The whole-library result of one classification pass: every pack's status for Settings, and the
/// matched packs per game id for the library marks. Built once from already-loaded snapshots and
/// bulk-loaded identifiers, so no view or row performs its own database read or disc parsing.
/// </summary>
public sealed class TexturePackLibraryMap
{
    private static readonly IReadOnlyList<TexturePackMatch> None = [];

    private readonly IReadOnlyDictionary<long, IReadOnlyList<TexturePackMatch>> _matchesByGame;

    private TexturePackLibraryMap(
        IReadOnlyList<TexturePackClassification> classifications,
        IReadOnlyDictionary<long, IReadOnlyList<TexturePackMatch>> matchesByGame,
        DateTimeOffset? lastScannedAt)
    {
        Classifications = classifications;
        _matchesByGame = matchesByGame;
        LastScannedAt = lastScannedAt;
    }

    /// <summary>An empty map, used before the first scan completes.</summary>
    public static TexturePackLibraryMap Empty { get; } = new([], new Dictionary<long, IReadOnlyList<TexturePackMatch>>(), null);

    public IReadOnlyList<TexturePackClassification> Classifications { get; }

    /// <summary>The oldest scan time across contributing snapshots, or null when there are none.</summary>
    public DateTimeOffset? LastScannedAt { get; }

    public int MatchedCount => Classifications.Count(c => c.Status == TexturePackEntryStatus.Matched);

    public int NoMatchCount => Classifications.Count(c => c.Status == TexturePackEntryStatus.NoLibraryMatch);

    /// <summary>Packs that need a human look: attention states, not failures of this library.</summary>
    public int AttentionCount => Classifications.Count(c => c.Status
        is TexturePackEntryStatus.EmptyOrDumpsOnly
        or TexturePackEntryStatus.UnrecognizedLayout
        or TexturePackEntryStatus.FolderUnavailable
        or TexturePackEntryStatus.IdentifierPending);

    public IReadOnlyList<TexturePackMatch> GetMatches(long gameId) =>
        _matchesByGame.TryGetValue(gameId, out var matches) ? matches : None;

    /// <summary>
    /// Matches for a displayed title, which may be a multi-disc set of separately imported games.
    /// A set is matched when any of its discs is, mirroring the emulators' own behavior of keying
    /// a multi-disc pack on one disc's identifier.
    /// </summary>
    public IReadOnlyList<TexturePackMatch> GetMatches(IEnumerable<long> gameIds)
    {
        ArgumentNullException.ThrowIfNull(gameIds);
        List<TexturePackMatch>? merged = null;
        var seen = new HashSet<(string, string, string)>();
        foreach (var gameId in gameIds)
        {
            foreach (var match in GetMatches(gameId))
            {
                if (!seen.Add((match.EmulatorId, match.InstallationId, match.PackKey)))
                    continue;
                merged ??= [];
                merged.Add(match);
            }
        }

        return merged ?? None;
    }

    public static TexturePackLibraryMap Build(
        IEnumerable<TexturePackInventorySnapshot> snapshots,
        IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> identifiersByGame)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(identifiersByGame);

        var index = LibraryIdentifierIndex.Build(identifiersByGame);
        var classifications = new List<TexturePackClassification>();
        var matchesByGame = new Dictionary<long, List<TexturePackMatch>>();
        DateTimeOffset? lastScannedAt = null;

        foreach (var snapshot in snapshots)
        {
            lastScannedAt = lastScannedAt is null || snapshot.ScannedAt < lastScannedAt
                ? snapshot.ScannedAt
                : lastScannedAt;

            // Dolphin consults an exact six-character directory before a region-free three-character
            // one, and the exact directory wins even when it holds nothing loadable. That precedence
            // is a property of the whole installation, so it is computed once per snapshot.
            var blockingDirectories = snapshot.Entries
                .SelectMany(entry => entry.MatchKeys)
                .Where(key => key.Rule == TexturePackMatchRule.DolphinDirectoryExact)
                .Select(key => key.Value)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var entry in snapshot.Entries)
            {
                var (status, matchedGameIds) = Classify(snapshot, entry, index, blockingDirectories);
                classifications.Add(new TexturePackClassification(
                    snapshot.EmulatorId,
                    snapshot.InstallationId,
                    entry,
                    status,
                    matchedGameIds));

                if (status != TexturePackEntryStatus.Matched)
                    continue;

                foreach (var gameId in matchedGameIds)
                {
                    if (!matchesByGame.TryGetValue(gameId, out var matches))
                    {
                        matches = [];
                        matchesByGame[gameId] = matches;
                    }

                    matches.Add(new TexturePackMatch(
                        snapshot.EmulatorId,
                        snapshot.InstallationId,
                        entry.PackKey,
                        entry.SourcePath,
                        entry.MatchKeys.Count > 0 ? entry.MatchKeys[0].Value : entry.PackKey));
                }
            }
        }

        return new TexturePackLibraryMap(
            classifications,
            matchesByGame.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<TexturePackMatch>)pair.Value),
            lastScannedAt);
    }

    private static (TexturePackEntryStatus Status, IReadOnlyList<long> GameIds) Classify(
        TexturePackInventorySnapshot snapshot,
        TexturePackInventoryEntry entry,
        LibraryIdentifierIndex index,
        IReadOnlySet<string> blockingDirectories)
    {
        if (snapshot.RootStatus is not TexturePackRootStatus.Ready)
            return (TexturePackEntryStatus.FolderUnavailable, []);

        switch (entry.ContentStatus)
        {
            case TexturePackContentStatus.Unreadable:
                return (TexturePackEntryStatus.FolderUnavailable, []);
            case TexturePackContentStatus.EmptyOrDumpsOnly:
                return (TexturePackEntryStatus.EmptyOrDumpsOnly, []);
            case TexturePackContentStatus.UnrecognizedLayout:
            case TexturePackContentStatus.Unknown:
                return (TexturePackEntryStatus.UnrecognizedLayout, []);
        }

        // Usable content from here on.
        if (entry.MatchKeys.Any(key => key.Rule == TexturePackMatchRule.DolphinShared))
            return (TexturePackEntryStatus.SharedPack, []);

        if (entry.MatchKeys.Count == 0)
            return (TexturePackEntryStatus.UnrecognizedLayout, []);

        var gameIds = new List<long>();
        foreach (var key in entry.MatchKeys)
            index.Collect(key, blockingDirectories, gameIds);

        if (gameIds.Count > 0)
            return (TexturePackEntryStatus.Matched, gameIds);

        // Nothing matched. Only call that "no library match" when the library actually holds
        // identifiers of the kind this pack is keyed on; otherwise identification is still pending
        // and an unmatched pack says nothing yet.
        return entry.MatchKeys.Any(index.HasComparableIdentifiers)
            ? (TexturePackEntryStatus.NoLibraryMatch, [])
            : (TexturePackEntryStatus.IdentifierPending, []);
    }

    /// <summary>Library identifiers arranged for the exact lookups each emulator rule performs.</summary>
    private sealed class LibraryIdentifierIndex
    {
        private readonly Dictionary<string, List<long>> _serials = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<long>> _pspIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<long>> _discIds = new(StringComparer.Ordinal);

        public static LibraryIdentifierIndex Build(
            IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> identifiersByGame)
        {
            var index = new LibraryIdentifierIndex();
            foreach (var (gameId, identifiers) in identifiersByGame)
            {
                foreach (var identifier in identifiers)
                {
                    var value = identifier.Value.Trim().ToUpperInvariant();
                    if (value.Length == 0)
                        continue;

                    switch (identifier.Kind)
                    {
                        case GameIdentifierKind.Serial:
                            Add(index._serials, value, gameId);
                            var pspId = NormalizePspGameId(value);
                            if (pspId.Length > 0)
                                Add(index._pspIds, pspId, gameId);
                            break;
                        case GameIdentifierKind.DiscId:
                            Add(index._discIds, value, gameId);
                            break;
                    }
                }
            }

            return index;
        }

        /// <summary>Whether this library could ever answer the lookup the rule performs.</summary>
        public bool HasComparableIdentifiers(TexturePackMatchKey key) => key.Rule switch
        {
            TexturePackMatchRule.ExactSerial => _serials.Count > 0,
            TexturePackMatchRule.PspGameId => _pspIds.Count > 0,
            TexturePackMatchRule.DolphinDirectoryExact
                or TexturePackMatchRule.DolphinDirectoryPrefix
                or TexturePackMatchRule.DolphinMarkerExact
                or TexturePackMatchRule.DolphinMarkerPrefix
                or TexturePackMatchRule.DolphinShared => _discIds.Count > 0,
            _ => false,
        };

        public void Collect(
            TexturePackMatchKey key,
            IReadOnlySet<string> blockingDirectories,
            List<long> gameIds)
        {
            switch (key.Rule)
            {
                case TexturePackMatchRule.ExactSerial:
                    AddRange(_serials, key.Value, gameIds);
                    break;
                case TexturePackMatchRule.PspGameId:
                    AddRange(_pspIds, key.Value, gameIds);
                    break;
                case TexturePackMatchRule.DolphinDirectoryExact:
                case TexturePackMatchRule.DolphinMarkerExact:
                    AddRange(_discIds, key.Value, gameIds);
                    break;
                case TexturePackMatchRule.DolphinDirectoryPrefix:
                    CollectByPrefix(key.Value, blockingDirectories, gameIds);
                    break;
                case TexturePackMatchRule.DolphinMarkerPrefix:
                    CollectByPrefix(key.Value, blockingDirectories: null, gameIds);
                    break;
            }
        }

        private void CollectByPrefix(
            string prefix,
            IReadOnlySet<string>? blockingDirectories,
            List<long> gameIds)
        {
            foreach (var (discId, ids) in _discIds)
            {
                if (!discId.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (blockingDirectories?.Contains(discId) == true)
                    continue;
                AddDistinct(gameIds, ids);
            }
        }

        private static void AddRange(
            IReadOnlyDictionary<string, List<long>> source,
            string key,
            List<long> gameIds)
        {
            if (source.TryGetValue(key, out var ids))
                AddDistinct(gameIds, ids);
        }

        private static void AddDistinct(List<long> gameIds, List<long> ids)
        {
            foreach (var id in ids)
            {
                if (!gameIds.Contains(id))
                    gameIds.Add(id);
            }
        }

        private static void Add(Dictionary<string, List<long>> target, string key, long gameId)
        {
            if (!target.TryGetValue(key, out var ids))
            {
                ids = [];
                target[key] = ids;
            }

            if (!ids.Contains(gameId))
                ids.Add(gameId);
        }

        private static string NormalizePspGameId(string value) =>
            string.Concat(value.Where(char.IsAsciiLetterOrDigit));
    }
}
