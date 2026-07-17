using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.Services;

public sealed record RetroAchievementsIdentificationSummary(
    int Processed,
    int Reused,
    int Hashed,
    int Unsupported,
    int Failed);

public interface IRetroAchievementsIdentificationService
{
    Task<RetroAchievementsIdentificationSummary> IdentifyAsync(
        IEnumerable<long> gameIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Serializes local media identification and skips any game whose dependency
/// fingerprint and hash algorithm version are already cached.
/// </summary>
public sealed class RetroAchievementsIdentificationService
    : IRetroAchievementsIdentificationService
{
    private readonly IRetroAchievementsStore _store;
    private readonly IRetroAchievementsGameHasher _hasher;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _worker = new(1, 1);

    public RetroAchievementsIdentificationService(
        IRetroAchievementsStore store,
        IRetroAchievementsGameHasher hasher,
        IAppLogger? logger = null)
    {
        _store = store;
        _hasher = hasher;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<RetroAchievementsIdentificationSummary> IdentifyAsync(
        IEnumerable<long> gameIds,
        CancellationToken cancellationToken = default)
    {
        var ids = gameIds.Distinct().ToArray();
        if (ids.Length == 0)
            return new RetroAchievementsIdentificationSummary(0, 0, 0, 0, 0);

        await _worker.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () => IdentifyCore(ids, cancellationToken),
                cancellationToken);
        }
        finally
        {
            _worker.Release();
        }
    }

    private RetroAchievementsIdentificationSummary IdentifyCore(
        IReadOnlyList<long> gameIds,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        var reused = 0;
        var hashed = 0;
        var unsupported = 0;
        var failed = 0;

        foreach (var gameId in gameIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var game = _store.GetGame(gameId);
                if (game is null)
                    continue;

                processed++;
                var snapshot = _hasher.Inspect(game);
                var existing = _store.GetGameLink(gameId);
                if (existing is not null &&
                    existing.Status is not (
                        RetroAchievementsIdentificationStatus.NotAttempted or
                        RetroAchievementsIdentificationStatus.Unreadable) &&
                    string.Equals(
                        existing.HashAlgorithmVersion,
                        _hasher.AlgorithmVersion,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        existing.SourceFingerprint,
                        snapshot.Fingerprint,
                        StringComparison.Ordinal))
                {
                    reused++;
                    continue;
                }

                var result = _hasher.Identify(game, cancellationToken);
                _store.SaveIdentification(gameId, result);
                switch (result.Status)
                {
                    case RetroAchievementsIdentificationStatus.Hashed:
                        hashed++;
                        break;
                    case RetroAchievementsIdentificationStatus.UnsupportedFormat:
                        unsupported++;
                        break;
                    case RetroAchievementsIdentificationStatus.InvalidMedia:
                    case RetroAchievementsIdentificationStatus.Unreadable:
                    default:
                        failed++;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.Warning(
                    $"RetroAchievements identification failed for game id {gameId}.",
                    ex);
            }
        }

        return new RetroAchievementsIdentificationSummary(
            processed,
            reused,
            hashed,
            unsupported,
            failed);
    }
}
