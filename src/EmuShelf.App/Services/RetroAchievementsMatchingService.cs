using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Services;

public sealed record RetroAchievementsMatchSummary(
    int Processed,
    int Matched,
    int NoAchievements,
    int Unresolved,
    int Unsupported);

public interface IRetroAchievementsMatchingService
{
    /// <summary>
    /// Resolves every locally hashed game against its console's cached catalogue, updating each
    /// link. A hash present in the catalogue is a match; a hash absent from a <em>fresh</em>
    /// catalogue is recorded as "no achievements"; a miss against a stale catalogue (or no
    /// catalogue at all) is left unresolved so it never becomes a false "no".
    /// </summary>
    Task<RetroAchievementsMatchSummary> MatchAsync(
        RetroAchievementsCredentials? credentials,
        bool forceRefreshCatalogues,
        CancellationToken cancellationToken = default,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress = null);
}

public sealed class RetroAchievementsMatchingService : IRetroAchievementsMatchingService
{
    private readonly IRetroAchievementsStore _store;
    private readonly IRetroAchievementsCatalogueCache _catalogue;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _worker = new(1, 1);

    public RetroAchievementsMatchingService(
        IRetroAchievementsStore store,
        IRetroAchievementsCatalogueCache catalogue,
        IAppLogger? logger = null)
    {
        _store = store;
        _catalogue = catalogue;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<RetroAchievementsMatchSummary> MatchAsync(
        RetroAchievementsCredentials? credentials,
        bool forceRefreshCatalogues,
        CancellationToken cancellationToken = default,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
    {
        await _worker.WaitAsync(cancellationToken);
        try
        {
            var byConsole = new Dictionary<int, List<RetroAchievementsHashedGame>>();
            var unsupported = 0;
            foreach (var game in _store.GetHashedGames())
            {
                var consoleId = RetroAchievementsConsoles.ForSystem(game.SystemId);
                if (consoleId is null)
                {
                    unsupported++;
                    continue;
                }

                if (!byConsole.TryGetValue(consoleId.Value, out var list))
                    byConsole[consoleId.Value] = list = [];
                list.Add(game);
            }

            var processed = 0;
            var matched = 0;
            var noAchievements = 0;
            var unresolved = 0;
            var total = byConsole.Values.Sum(games => games.Count);
            var completed = 0;

            progress?.Report(new RetroAchievementsLibrarySyncProgress(
                RetroAchievementsLibrarySyncPhase.Matching,
                Completed: 0,
                Total: total));

            foreach (var (consoleId, games) in byConsole)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new RetroAchievementsLibrarySyncProgress(
                    RetroAchievementsLibrarySyncPhase.Matching,
                    Completed: completed,
                    Total: total,
                    CurrentGameTitle: games[0].Title));
                var lookup = await _catalogue.GetLookupAsync(
                    consoleId, credentials, forceRefreshCatalogues, cancellationToken);

                foreach (var game in games)
                {
                    progress?.Report(new RetroAchievementsLibrarySyncProgress(
                        RetroAchievementsLibrarySyncPhase.Matching,
                        Completed: completed,
                        Total: total,
                        CurrentGameTitle: game.Title));
                    processed++;
                    if (lookup is null)
                    {
                        unresolved++;
                    }
                    else
                    {
                        var match = lookup.Find(game.CanonicalHash);
                        if (match is not null)
                        {
                            _store.SaveCatalogueMatch(game.GameId, match.GameId, hasAchievements: true);
                            matched++;
                        }
                        else if (lookup.IsFresh)
                        {
                            _store.SaveCatalogueMatch(game.GameId, null, hasAchievements: false);
                            noAchievements++;
                        }
                        else
                        {
                            unresolved++;
                        }
                    }

                    completed++;
                    progress?.Report(new RetroAchievementsLibrarySyncProgress(
                        RetroAchievementsLibrarySyncPhase.Matching,
                        Completed: completed,
                        Total: total));
                }
            }

            var summary = new RetroAchievementsMatchSummary(
                processed, matched, noAchievements, unresolved, unsupported);
            _logger.Information(
                $"RetroAchievements matching: {matched} matched, {noAchievements} without achievements, " +
                $"{unresolved} unresolved of {processed}.");
            return summary;
        }
        finally
        {
            _worker.Release();
        }
    }
}
