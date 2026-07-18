using System.Text.Json;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Caches each RA console's achievement-bearing game/hash catalogue as JSON under
/// <c>Cache/RetroAchievements/</c>. A within-TTL cache is served without any network; a missing,
/// stale, or explicitly refreshed catalogue is refetched when credentials are available; and a
/// stale cache is still served when a fetch cannot run so matching keeps working offline.
/// </summary>
public sealed class RetroAchievementsCatalogueCache : IRetroAchievementsCatalogueCache
{
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromDays(7);

    private readonly string _directory;
    private readonly IRetroAchievementsClient _client;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _timeToLive;

    public RetroAchievementsCatalogueCache(
        IAppPaths paths,
        IRetroAchievementsClient client,
        IAppLogger? logger = null,
        TimeSpan? timeToLive = null)
    {
        _directory = Path.Combine(paths.CacheDirectory, "RetroAchievements");
        _client = client;
        _logger = logger ?? NullAppLogger.Instance;
        _timeToLive = timeToLive ?? DefaultTimeToLive;
    }

    public async Task<RetroAchievementsCatalogueLookup?> GetLookupAsync(
        int consoleId,
        RetroAchievementsCredentials? credentials,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_directory, $"console-{consoleId}.json");

        // 1. Serve a within-TTL cache without any network, unless a refresh is forced.
        if (!forceRefresh && IsFresh(path))
        {
            var cached = await TryLoadAsync(path, cancellationToken);
            if (cached is not null)
                return new RetroAchievementsCatalogueLookup(isFresh: true, BuildIndex(cached));
            // A corrupt within-TTL cache falls through to a fetch.
        }

        // 2. Fetch when the cache is missing, stale, forced, or unreadable — needs credentials.
        if (credentials is not null)
        {
            var response = await _client.GetGameListAsync(credentials, consoleId, cancellationToken);
            if (response.IsSuccess)
            {
                await SaveAsync(path, response.Value!, cancellationToken);
                return new RetroAchievementsCatalogueLookup(isFresh: true, BuildIndex(response.Value!));
            }

            _logger.Information(
                $"RetroAchievements catalogue for console {consoleId} could not be refreshed ({response.Status}).");
        }

        // 3. Fall back to whatever cache exists; freshness reflects its real age.
        var fallback = await TryLoadAsync(path, cancellationToken);
        return fallback is null
            ? null
            : new RetroAchievementsCatalogueLookup(IsFresh(path), BuildIndex(fallback));
    }

    private bool IsFresh(string path) =>
        File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < _timeToLive;

    private static IReadOnlyDictionary<string, RetroAchievementsCatalogueMatch> BuildIndex(
        IReadOnlyList<RetroAchievementsCatalogueGame> games)
    {
        var index = new Dictionary<string, RetroAchievementsCatalogueMatch>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            var match = new RetroAchievementsCatalogueMatch(
                game.GameId, game.Title, game.AchievementCount);
            foreach (var hash in game.Hashes)
                index[hash] = match;
        }
        return index;
    }

    private static async Task<IReadOnlyList<RetroAchievementsCatalogueGame>?> TryLoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<RetroAchievementsCatalogueGame>>(
                stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private async Task SaveAsync(
        string path,
        IReadOnlyList<RetroAchievementsCatalogueGame> games,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        await AtomicFile.WriteAsync(
            path,
            (stream, token) => JsonSerializer.SerializeAsync(stream, games, cancellationToken: token),
            cancellationToken);
    }
}
