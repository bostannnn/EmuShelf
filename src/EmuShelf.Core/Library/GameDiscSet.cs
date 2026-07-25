using System.Text.RegularExpressions;

namespace EmuShelf.Core.Library;

/// <summary>A launchable disc belonging to a multi-disc library title.</summary>
public sealed record GameDisc(int Number, Game Game);

/// <summary>
/// A presentation-only grouping of independently imported game files. The source <see cref="Game"/>
/// records remain independent; this type never changes media files, playlists, or emulator data.
/// </summary>
public sealed record GameDiscSet(
    Game DisplayGame,
    string DisplayTitle,
    string? SelectionKey,
    IReadOnlyList<GameDisc> Discs,
    GameDisc SelectedDisc)
{
    public bool IsMultiDisc => Discs.Count > 1;
}

/// <summary>
/// Conservatively recognizes ordinary filename conventions such as <c>Game (Disc 1)</c> and
/// <c>Game CD2</c>. It intentionally declines ambiguous names, demos, and bonus discs.
/// </summary>
public static partial class GameDiscSetBuilder
{
    private static readonly Regex DiscMarker = CreateDiscMarkerRegex();
    private static readonly Regex EmptyBrackets = CreateEmptyBracketsRegex();
    private static readonly Regex Whitespace = CreateWhitespaceRegex();
    private static readonly Regex ExcludedRelease = CreateExcludedReleaseRegex();

    public static IReadOnlyList<GameDiscSet> Build(
        IReadOnlyList<Game> games,
        IReadOnlyDictionary<string, long>? rememberedDiscIds = null)
    {
        ArgumentNullException.ThrowIfNull(games);

        var candidates = games
            .Select(game => TryCreateCandidate(game, out var candidate) ? candidate : (DiscCandidate?)null)
            .Where(candidate => candidate is not null)
             .Cast<DiscCandidate>()
             .GroupBy(candidate => candidate.SelectionKey, StringComparer.Ordinal)
            // A title set must have exactly one imported source per disc number. Decline a whole
            // candidate group when duplicate encodes (for example, CUE and CHD) are present so the
            // picker can never offer two indistinguishable "Disc 1" entries.
            .Where(group => group.Select(candidate => candidate.Number).Distinct().Count() > 1 &&
                group.GroupBy(candidate => candidate.Number).All(numberGroup => numberGroup.Count() == 1))
            .ToDictionary(group => group.Key, group => group.OrderBy(candidate => candidate.Number)
                .ThenBy(candidate => candidate.Game.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray(), StringComparer.Ordinal);

        var result = new List<GameDiscSet>();
        var groupedPaths = new HashSet<string>(
            candidates.Values.SelectMany(group => group.Select(candidate => candidate.Game.Path)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in candidates.Values)
        {
            var first = group[0];
            var discs = group.Select(candidate => new GameDisc(candidate.Number, candidate.Game)).ToArray();
            var preferredId = rememberedDiscIds is not null &&
                rememberedDiscIds.TryGetValue(first.SelectionKey, out var selectedId)
                ? selectedId
                : first.Game.Id;
            var selected = discs.FirstOrDefault(disc => disc.Game.Id == preferredId) ?? discs[0];
            result.Add(new GameDiscSet(
                first.Game,
                DisplayTitle(first.Game.Title),
                first.SelectionKey,
                discs,
                selected));
        }

        foreach (var game in games)
        {
            if (!groupedPaths.Contains(game.Path))
                result.Add(new GameDiscSet(game, game.Title, null, [new GameDisc(1, game)], new GameDisc(1, game)));
        }

        return result;
    }

    private static bool TryCreateCandidate(Game game, out DiscCandidate candidate)
    {
        var sourceTitle = Path.GetFileNameWithoutExtension(game.Path);
        var match = DiscMarker.Match(sourceTitle);
        if (!match.Success || ExcludedRelease.IsMatch(sourceTitle))
        {
            candidate = default;
            return false;
        }

        var normalized = NormalizeTitle(sourceTitle);
        if (normalized.Length == 0 || !int.TryParse(match.Groups["number"].Value, out var number) || number <= 0)
        {
            candidate = default;
            return false;
        }

        candidate = new DiscCandidate(
            $"{game.SystemId}\u001F{normalized}",
            number,
            game);
        return true;
    }

    private static string DisplayTitle(string title)
    {
        var withoutMarker = DiscMarker.Replace(title, " ");
        return NormalizeSpacing(withoutMarker);
    }

    private static string NormalizeTitle(string title) =>
        NormalizeSpacing(DiscMarker.Replace(title, " ")).ToUpperInvariant();

    private static string NormalizeSpacing(string title) =>
        Whitespace.Replace(EmptyBrackets.Replace(title, string.Empty), " ").Trim(' ', '-', '_', '.');

    private readonly record struct DiscCandidate(string SelectionKey, int Number, Game Game);

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:disc|disk|cd)\s*(?<number>[1-9][0-9]*)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex CreateDiscMarkerRegex();

    [GeneratedRegex(@"[\(\[\{]\s*[\)\]\}]", RegexOptions.CultureInvariant)]
    private static partial Regex CreateEmptyBracketsRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex CreateWhitespaceRegex();

    [GeneratedRegex(@"(?<![A-Za-z])(?:demo|bonus|rev(?:ision)?\s*[0-9]+)(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex CreateExcludedReleaseRegex();
}
