using System.Net;
using System.Text.Json;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Read-only RetroAchievements Web API client. It authenticates with the caller's username and
/// Web API key (query keys <c>z</c> and <c>y</c>) and maps transport and server conditions to
/// distinct <see cref="RetroAchievementsRequestStatus"/> values. The API key is never logged and
/// never appears in a logged URI: only the endpoint name and outcome are recorded.
/// </summary>
public sealed class RetroAchievementsWebClient : IRetroAchievementsClient
{
    public const string DefaultBaseAddress = "https://retroachievements.org/API/";

    /// <summary>Laravel (RetroAchievements' framework) returns 419 for an unauthenticated request.</summary>
    private const int AuthenticationTimeout = 419;

    /// <summary>
    /// Upper bound on ids per <see cref="GetUserProgressAsync"/> call. A larger set must be split
    /// by the caller (the §6 refresh coordinator) so the request URI cannot exceed server limits.
    /// </summary>
    public const int MaxUserProgressBatchSize = 100;

    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly IAppLogger _logger;

    public RetroAchievementsWebClient(
        HttpClient httpClient,
        IAppLogger? logger = null,
        string baseAddress = DefaultBaseAddress)
    {
        _httpClient = httpClient;
        _baseAddress = new Uri(baseAddress, UriKind.Absolute);
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<RetroAchievementsResponse<RetroAchievementsProfile>> GetUserProfileAsync(
        RetroAchievementsCredentials credentials,
        CancellationToken cancellationToken = default) =>
        await SendAsync(
            "API_GetUserProfile.php",
            credentials,
            new Dictionary<string, string> { ["u"] = credentials.Username },
            ParseProfile,
            cancellationToken);

    public async Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsCatalogueGame>>> GetGameListAsync(
        RetroAchievementsCredentials credentials,
        int consoleId,
        CancellationToken cancellationToken = default) =>
        await SendAsync(
            "API_GetGameList.php",
            credentials,
            new Dictionary<string, string>
            {
                ["i"] = consoleId.ToString(),
                ["f"] = "1", // only games that have achievements
                ["h"] = "1", // include the hashes that map to each game
            },
            ParseGameList,
            cancellationToken);

    public async Task<RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>> GetUserProgressAsync(
        RetroAchievementsCredentials credentials,
        IReadOnlyList<int> gameIds,
        CancellationToken cancellationToken = default)
    {
        if (gameIds.Count == 0)
            return RetroAchievementsResponse<IReadOnlyList<RetroAchievementsGameProgress>>.Success([]);
        if (gameIds.Count > MaxUserProgressBatchSize)
            throw new ArgumentOutOfRangeException(
                nameof(gameIds),
                $"At most {MaxUserProgressBatchSize} game ids may be requested at once; split larger sets.");

        return await SendAsync(
            "API_GetUserProgress.php",
            credentials,
            new Dictionary<string, string>
            {
                ["u"] = credentials.Username,
                ["i"] = string.Join(',', gameIds),
            },
            ParseUserProgress,
            cancellationToken);
    }

    private async Task<RetroAchievementsResponse<T>> SendAsync<T>(
        string endpoint,
        RetroAchievementsCredentials credentials,
        IReadOnlyDictionary<string, string> parameters,
        Func<JsonElement, T?> parse,
        CancellationToken cancellationToken)
    {
        var requestUri = BuildUri(endpoint, credentials, parameters);
        try
        {
            using var response = await _httpClient.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var status = MapStatusCode(response.StatusCode);
            if (status != RetroAchievementsRequestStatus.Success)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta;
                _logger.Information($"RetroAchievements {endpoint} → {(int)response.StatusCode} ({status}).");
                return RetroAchievementsResponse<T>.Failure(status, retryAfter);
            }

            // Parse from the response stream so the large per-console catalogue is never
            // materialized as one big intermediate string.
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var value = parse(document.RootElement);
            if (value is null)
            {
                _logger.Warning($"RetroAchievements {endpoint} returned an unparseable body.");
                return RetroAchievementsResponse<T>.Failure(
                    RetroAchievementsRequestStatus.MalformedResponse);
            }

            return RetroAchievementsResponse<T>.Success(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // A timeout or a transport failure is treated as offline so callers keep cached data.
            _logger.Information($"RetroAchievements {endpoint} is unreachable ({ex.GetType().Name}).");
            return RetroAchievementsResponse<T>.Failure(RetroAchievementsRequestStatus.Offline);
        }
        catch (JsonException)
        {
            return RetroAchievementsResponse<T>.Failure(
                RetroAchievementsRequestStatus.MalformedResponse);
        }
    }

    private static RetroAchievementsRequestStatus MapStatusCode(HttpStatusCode code) => code switch
    {
        HttpStatusCode.OK => RetroAchievementsRequestStatus.Success,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or (HttpStatusCode)AuthenticationTimeout =>
            RetroAchievementsRequestStatus.AuthenticationFailed,
        HttpStatusCode.TooManyRequests => RetroAchievementsRequestStatus.RateLimited,
        _ => RetroAchievementsRequestStatus.ServerError,
    };

    private Uri BuildUri(
        string endpoint,
        RetroAchievementsCredentials credentials,
        IReadOnlyDictionary<string, string> parameters)
    {
        var query = new List<string>
        {
            "z=" + Uri.EscapeDataString(credentials.Username),
            "y=" + Uri.EscapeDataString(credentials.ApiKey),
        };
        foreach (var (key, value) in parameters)
            query.Add($"{key}={Uri.EscapeDataString(value)}");

        return new Uri(_baseAddress, $"{endpoint}?{string.Join('&', query)}");
    }

    private static RetroAchievementsProfile? ParseProfile(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var username = GetString(root, "User");
        if (username is null)
            return null;

        var ulid = GetString(root, "ULID");
        return new RetroAchievementsProfile(
            username,
            string.IsNullOrEmpty(ulid) ? username : ulid,
            GetInt(root, "TotalPoints"),
            GetInt(root, "TotalSoftcorePoints"));
    }

    private static IReadOnlyList<RetroAchievementsCatalogueGame>? ParseGameList(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            return null;

        var games = new List<RetroAchievementsCatalogueGame>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
                continue;

            var hashes = new List<string>();
            if (element.TryGetProperty("Hashes", out var hashArray) &&
                hashArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var hash in hashArray.EnumerateArray())
                {
                    var value = hash.GetString();
                    if (!string.IsNullOrEmpty(value))
                        hashes.Add(value.ToLowerInvariant());
                }
            }

            games.Add(new RetroAchievementsCatalogueGame(
                GetInt(element, "ID"),
                GetString(element, "Title") ?? string.Empty,
                GetInt(element, "NumAchievements"),
                hashes));
        }

        return games;
    }

    private static IReadOnlyList<RetroAchievementsGameProgress>? ParseUserProgress(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var progress = new List<RetroAchievementsGameProgress>();
        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object ||
                !int.TryParse(property.Name, out var gameId))
            {
                continue;
            }

            progress.Add(new RetroAchievementsGameProgress(
                gameId,
                GetInt(property.Value, "NumPossibleAchievements"),
                GetInt(property.Value, "NumAchieved"),
                GetInt(property.Value, "NumAchievedHardcore")));
        }

        return progress;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => 0,
        };
    }
}
