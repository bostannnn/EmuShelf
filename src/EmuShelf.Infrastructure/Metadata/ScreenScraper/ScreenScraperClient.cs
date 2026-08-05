using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using EmuShelf.Core.Metadata.ScreenScraper;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

public sealed class ScreenScraperClient : IScreenScraperClient
{
    public static readonly Uri ApiBaseUri = new("https://api.screenscraper.fr/api2/");

    private readonly HttpClient _httpClient;
    private readonly ScreenScraperDeveloperCredentials _developerCredentials;
    private readonly ScreenScraperRequestCoordinator _requestCoordinator;
    private readonly ScreenScraperDebugOptions? _debugOptions;

    public ScreenScraperClient(
        HttpClient httpClient,
        ScreenScraperDeveloperCredentials developerCredentials,
        ScreenScraperRequestCoordinator? requestCoordinator = null,
        ScreenScraperDebugOptions? debugOptions = null)
    {
        _httpClient = httpClient;
        _developerCredentials = developerCredentials;
        _requestCoordinator = requestCoordinator ?? new ScreenScraperRequestCoordinator();
        _debugOptions = debugOptions;
        ValidateDeveloperCredentials(developerCredentials);
        if (debugOptions is not null && string.IsNullOrWhiteSpace(debugOptions.DebugPassword))
            throw new ArgumentException("ScreenScraper debug password cannot be empty.", nameof(debugOptions));
    }

    public Task<ScreenScraperResult<ScreenScraperAccountInfo>> GetAccountInfoAsync(
        ScreenScraperUserCredentials userCredentials,
        CancellationToken cancellationToken = default)
    {
        ValidateUserCredentials(userCredentials);
        return SendAsync(
            "ssuserInfos.php",
            userCredentials,
            [],
            response =>
            {
                if (!response.TryGetProperty("ssuser", out var user))
                    return null;
                var quota = ParseQuota(user);
                return new ScreenScraperAccountInfo(
                    ReadString(user, "numid"),
                    ReadString(user, "id"),
                    ReadString(user, "niveau"),
                    quota);
            },
            cancellationToken);
    }

    public async Task<ScreenScraperResult<ScreenScraperGameInfo>> GetGameInfoAsync(
        ScreenScraperUserCredentials userCredentials,
        ScreenScraperGameRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateUserCredentials(userCredentials);
        ValidateGameRequest(request);

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("systemeid", request.SystemId.ToString(CultureInfo.InvariantCulture)),
            new("romtype", "rom"),
            new("romnom", request.RomName.Trim()),
        };
        AddIfPresent(parameters, "gameid", request.ProviderGameId);
        if (request.RomSize > 0)
            parameters.Add(new("romtaille", request.RomSize.ToString(CultureInfo.InvariantCulture)));
        AddIfPresent(parameters, "crc", request.Crc32);
        AddIfPresent(parameters, "md5", request.Md5);
        AddIfPresent(parameters, "sha1", request.Sha1);
        AddIfPresent(parameters, "serialnum", request.Serial);
        AddIfPresent(parameters, "langue", request.Language);

        var result = await SendAsync(
            "jeuInfos.php",
            userCredentials,
            parameters,
            response => response.TryGetProperty("jeu", out var game) ? ParseGame(game) : null,
            cancellationToken);

        // ScreenScraper always receives romnom (the file name) alongside any hash, and documents a
        // filename fallback: when the queried hash is not in its database it can still return a
        // DIFFERENT game that merely shares the name — a rom-hack, a bad dump, or a renamed file. The
        // response never states which criterion matched, so verify it here. If the lookup asked by
        // hash and the returned ROM carries a hash of the same kind that does not equal ours, the
        // match was by name only; reject it so a wrong game is never reported as an exact hash match
        // and can never overwrite correct metadata.
        if (result.IsSuccess && !ReturnedRomMatchesRequestedHash(request, result.Data!))
        {
            return new ScreenScraperResult<ScreenScraperGameInfo>(
                ScreenScraperRequestStatus.NotFound,
                null,
                result.Quota,
                "ScreenScraper matched by file name only, not by the ROM hash, so the result was ignored.");
        }

        return result;
    }

    // True unless the returned ROM can be proven to be a different game than the one asked for. Only a
    // hash-initiated lookup is verifiable here (a serial-only or game-id lookup carries no ROM hash),
    // and only when ScreenScraper echoes a hash of a kind we queried — otherwise there is nothing to
    // compare and the result is accepted rather than risk rejecting a legitimate match on missing data.
    private static bool ReturnedRomMatchesRequestedHash(
        ScreenScraperGameRequest request,
        ScreenScraperGameInfo game)
    {
        var comparisons = new[]
        {
            (Queried: request.Crc32, Returned: game.RomCrc32),
            (Queried: request.Md5, Returned: game.RomMd5),
            (Queried: request.Sha1, Returned: game.RomSha1),
        };
        var comparable = comparisons
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Queried) && !string.IsNullOrWhiteSpace(pair.Returned))
            .ToList();
        if (comparable.Count == 0)
            return true;
        return comparable.Any(pair =>
            string.Equals(pair.Queried!.Trim(), pair.Returned!.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperSystem>>> GetSystemsAsync(
        ScreenScraperUserCredentials userCredentials,
        CancellationToken cancellationToken = default)
    {
        ValidateUserCredentials(userCredentials);
        return SendAsync(
            "systemesListe.php",
            userCredentials,
            [],
            ParseSystems,
            cancellationToken);
    }

    public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchGamesAsync(
        ScreenScraperUserCredentials userCredentials,
        int systemId,
        string query,
        CancellationToken cancellationToken = default)
    {
        ValidateUserCredentials(userCredentials);
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query cannot be empty.", nameof(query));

        var parameters = new List<KeyValuePair<string, string>>
        {
            new("recherche", query.Trim()),
        };
        if (systemId > 0)
            parameters.Add(new("systemeid", systemId.ToString(CultureInfo.InvariantCulture)));

        return SendAsync("jeuRecherche.php", userCredentials, parameters, ParseSearchResults, cancellationToken);
    }

    private static IReadOnlyList<ScreenScraperGameMatch> ParseSearchResults(JsonElement response)
    {
        var matches = new List<ScreenScraperGameMatch>();
        if (!response.TryGetProperty("jeux", out var games) || games.ValueKind != JsonValueKind.Array)
            return matches;

        foreach (var game in games.EnumerateArray())
        {
            var id = ReadString(game, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var names = ParseLocalizedArray(game, "noms", languageProperty: null, regionProperty: "region");
            var name = names.FirstOrDefault()?.Value ?? id;
            var system = game.TryGetProperty("systeme", out var systemElement)
                ? ReadString(systemElement, "text")
                : null;
            matches.Add(new ScreenScraperGameMatch(id, name, system));
        }

        return matches;
    }

    private async Task<ScreenScraperResult<T>> SendAsync<T>(
        string endpoint,
        ScreenScraperUserCredentials userCredentials,
        IReadOnlyList<KeyValuePair<string, string>> requestParameters,
        Func<JsonElement, T?> parse,
        CancellationToken cancellationToken)
        where T : class
    {
        var admission = await _requestCoordinator.EnterAsync(cancellationToken);
        if (admission.Lease is null)
        {
            return new ScreenScraperResult<T>(
                admission.Status,
                null,
                _requestCoordinator.LatestQuota,
                StatusMessage(admission.Status));
        }

        using var lease = admission.Lease;
        var uri = BuildUri(endpoint, userCredentials, requestParameters);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("User-Agent", _developerCredentials.SoftwareName);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            var mappedStatus = MapStatusCode(response.StatusCode);
            _requestCoordinator.ObserveStatus(mappedStatus, GetRetryAfter(response));
            if (mappedStatus != ScreenScraperRequestStatus.Success)
            {
                return new ScreenScraperResult<T>(
                    mappedStatus,
                    null,
                    _requestCoordinator.LatestQuota,
                    StatusMessage(mappedStatus));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!HeaderSucceeded(root))
            {
                var error = ReadHeaderError(root, userCredentials);
                return new ScreenScraperResult<T>(
                    ScreenScraperRequestStatus.ApiRejected,
                    null,
                    null,
                    error ?? "ScreenScraper rejected the request.");
            }

            if (!root.TryGetProperty("response", out var responseElement))
                return InvalidResponse<T>();

            var data = parse(responseElement);
            if (data is null)
                return InvalidResponse<T>();

            var quota = responseElement.TryGetProperty("ssuser", out var user)
                ? ParseQuota(user)
                : null;
            _requestCoordinator.ObserveQuota(quota);
            return new ScreenScraperResult<T>(
                ScreenScraperRequestStatus.Success,
                data,
                quota,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            // HttpClient exception messages can contain the full credential-bearing request URI.
            return new ScreenScraperResult<T>(
                ScreenScraperRequestStatus.NetworkError,
                null,
                null,
                "ScreenScraper could not be reached.");
        }
        catch (OperationCanceledException)
        {
            return new ScreenScraperResult<T>(
                ScreenScraperRequestStatus.NetworkError,
                null,
                null,
                "The ScreenScraper request timed out.");
        }
        catch (JsonException)
        {
            return InvalidResponse<T>();
        }
    }

    private Uri BuildUri(
        string endpoint,
        ScreenScraperUserCredentials userCredentials,
        IReadOnlyList<KeyValuePair<string, string>> requestParameters)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("devid", _developerCredentials.DeveloperId),
            new("devpassword", _developerCredentials.DeveloperPassword),
            new("softname", _developerCredentials.SoftwareName),
            new("output", "json"),
            new("ssid", userCredentials.Username),
            new("sspassword", userCredentials.Password),
        };
        parameters.AddRange(requestParameters);
        AppendDebugParameters(parameters);
        var query = string.Join("&", parameters.Select(parameter =>
            $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        return new UriBuilder(new Uri(ApiBaseUri, endpoint)) { Query = query }.Uri;
    }

    private void AppendDebugParameters(ICollection<KeyValuePair<string, string>> parameters)
    {
        if (_debugOptions is not { } debug)
            return;

        parameters.Add(new("devdebugpassword", debug.DebugPassword));
        if (debug.ForceUpdate)
            parameters.Add(new("forceupdate", "1"));
        AddIfPresent(parameters, "forceip", debug.ForceIp);
        AddIntIfPresent(parameters, "forcelevel", debug.ForceLevel);
        AddIntIfPresent(parameters, "forcerequestok", debug.ForceRequestOk);
        AddIntIfPresent(parameters, "forcerequestko", debug.ForceRequestKo);
        AddIntIfPresent(parameters, "forcerequestmin", debug.ForceRequestMin);
    }

    private static IReadOnlyList<ScreenScraperSystem>? ParseSystems(JsonElement response)
    {
        if (!response.TryGetProperty("systemes", out var systemes) || systemes.ValueKind != JsonValueKind.Array)
            return null;

        var systems = new List<ScreenScraperSystem>();
        foreach (var system in systemes.EnumerateArray())
        {
            var id = ReadInt(system, "id");
            if (id is null)
                continue;

            var names = new List<string>();
            if (system.TryGetProperty("noms", out var noms) && noms.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in noms.EnumerateObject())
                {
                    if (name.Value.ValueKind == JsonValueKind.String &&
                        name.Value.GetString() is { Length: > 0 } text)
                    {
                        names.Add(text);
                    }
                }
            }

            var primary = ReadString(system, "nom") ?? names.FirstOrDefault();
            systems.Add(new ScreenScraperSystem(id.Value, primary, names));
        }

        return systems;
    }

    private static ScreenScraperGameInfo? ParseGame(JsonElement game)
    {
        var gameId = ReadString(game, "id");
        if (string.IsNullOrWhiteSpace(gameId))
            return null;

        string? romId = null;
        string? romCrc32 = null;
        string? romMd5 = null;
        string? romSha1 = null;
        if (game.TryGetProperty("rom", out var rom) && rom.ValueKind == JsonValueKind.Object)
        {
            romId = ReadString(rom, "id");
            romCrc32 = ReadString(rom, "romcrc");
            romMd5 = ReadString(rom, "rommd5");
            romSha1 = ReadString(rom, "romsha1");
        }

        return new ScreenScraperGameInfo(
            gameId,
            romId,
            romCrc32,
            romMd5,
            romSha1,
            ParseLocalizedArray(game, "noms", languageProperty: null, regionProperty: "region"),
            ParseLocalizedArray(game, "synopsis", "langue", regionProperty: null),
            ParseGenres(game),
            ParseReleaseDates(game),
            ReadNestedText(game, "developpeur"),
            ReadNestedText(game, "editeur"),
            ReadNestedText(game, "joueurs"),
            ReadNestedText(game, "note"),
            ParseMedia(game));
    }

    private static IReadOnlyList<ScreenScraperLocalizedText> ParseLocalizedArray(
        JsonElement parent,
        string propertyName,
        string? languageProperty,
        string? regionProperty)
    {
        var values = new List<ScreenScraperLocalizedText>();
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var item in array.EnumerateArray())
        {
            var text = ReadString(item, "text");
            if (string.IsNullOrWhiteSpace(text))
                continue;
            values.Add(new ScreenScraperLocalizedText(
                text,
                languageProperty is null ? null : ReadString(item, languageProperty),
                regionProperty is null ? null : ReadString(item, regionProperty)));
        }
        return values;
    }

    private static IReadOnlyList<ScreenScraperLocalizedText> ParseGenres(JsonElement game)
    {
        var values = new List<ScreenScraperLocalizedText>();
        if (!game.TryGetProperty("genres", out var genres) || genres.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var genre in genres.EnumerateArray())
            values.AddRange(ParseLocalizedArray(genre, "noms", "langue", regionProperty: null));
        return values;
    }

    private static IReadOnlyList<ScreenScraperReleaseDate> ParseReleaseDates(JsonElement game)
    {
        var values = new List<ScreenScraperReleaseDate>();
        if (!game.TryGetProperty("dates", out var dates) || dates.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var date in dates.EnumerateArray())
        {
            var text = ReadString(date, "text");
            if (!string.IsNullOrWhiteSpace(text))
                values.Add(new ScreenScraperReleaseDate(text, ReadString(date, "region")));
        }
        return values;
    }

    private static IReadOnlyList<ScreenScraperMediaCandidate> ParseMedia(JsonElement game)
    {
        var values = new List<ScreenScraperMediaCandidate>();
        if (!game.TryGetProperty("medias", out var media) || media.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var item in media.EnumerateArray())
        {
            var type = ReadString(item, "type");
            var url = ReadString(item, "url");
            if (string.IsNullOrWhiteSpace(type) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var sourceUri) ||
                sourceUri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            var format = ReadString(item, "format");
            var extension = string.IsNullOrWhiteSpace(format)
                ? Path.GetExtension(sourceUri.AbsolutePath)
                : "." + format.Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension))
                continue;

            values.Add(new ScreenScraperMediaCandidate(
                type,
                sourceUri,
                extension,
                ReadString(item, "id"),
                ReadString(item, "region"),
                ReadString(item, "langue"),
                ReadInt(item, "width"),
                ReadInt(item, "height"),
                ReadLong(item, "size"),
                ReadString(item, "crc"),
                ReadString(item, "md5"),
                ReadString(item, "sha1")));
        }
        return values;
    }

    private static ScreenScraperQuota ParseQuota(JsonElement user) =>
        new(
            ReadInt(user, "maxthreads"),
            ReadInt(user, "requeststoday"),
            ReadInt(user, "maxrequestsperday"),
            ReadInt(user, "requestskotoday"),
            ReadInt(user, "maxrequestskoperday"),
            ReadInt(user, "maxdownloadspeed"));

    private static ScreenScraperRequestStatus MapStatusCode(HttpStatusCode statusCode) =>
        (int)statusCode switch
        {
            200 => ScreenScraperRequestStatus.Success,
            403 => ScreenScraperRequestStatus.AuthenticationFailed,
            404 => ScreenScraperRequestStatus.NotFound,
            401 or 423 => ScreenScraperRequestStatus.ServiceUnavailable,
            426 => ScreenScraperRequestStatus.ClientUpdateRequired,
            429 => ScreenScraperRequestStatus.RateLimited,
            430 => ScreenScraperRequestStatus.DailyQuotaExceeded,
            431 => ScreenScraperRequestStatus.FailedLookupQuotaExceeded,
            _ => ScreenScraperRequestStatus.ServiceUnavailable,
        };

    private static string StatusMessage(ScreenScraperRequestStatus status) => status switch
    {
        ScreenScraperRequestStatus.AuthenticationFailed => "ScreenScraper rejected the credentials.",
        ScreenScraperRequestStatus.NotFound => "ScreenScraper has no matching game.",
        ScreenScraperRequestStatus.ClientUpdateRequired => "ScreenScraper requires a newer approved client.",
        ScreenScraperRequestStatus.RateLimited => "ScreenScraper's concurrency or minute limit was reached.",
        ScreenScraperRequestStatus.DailyQuotaExceeded => "The ScreenScraper daily request quota was reached.",
        ScreenScraperRequestStatus.FailedLookupQuotaExceeded => "The ScreenScraper failed-lookup quota was reached.",
        _ => "ScreenScraper is temporarily unavailable.",
    };

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
        return null;
    }

    private static bool HeaderSucceeded(JsonElement root)
    {
        if (!root.TryGetProperty("header", out var header) ||
            !header.TryGetProperty("success", out var success))
        {
            return false;
        }

        return success.ValueKind == JsonValueKind.True ||
            (success.ValueKind == JsonValueKind.String &&
             string.Equals(success.GetString(), "true", StringComparison.OrdinalIgnoreCase));
    }

    private string? ReadHeaderError(JsonElement root, ScreenScraperUserCredentials userCredentials)
    {
        if (!root.TryGetProperty("header", out var header))
            return null;
        var error = ReadString(header, "error");
        if (string.IsNullOrWhiteSpace(error))
            return null;

        var redacted = error
            .Replace(_developerCredentials.DeveloperPassword, "[redacted]", StringComparison.Ordinal)
            .Replace(userCredentials.Password, "[redacted]", StringComparison.Ordinal);
        if (_debugOptions is { } debug)
            redacted = redacted.Replace(debug.DebugPassword, "[redacted]", StringComparison.Ordinal);
        return redacted.Length <= 300 ? redacted : redacted[..300];
    }

    private static string? ReadNestedText(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return null;
        return ReadString(value, "text");
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement element, string propertyName) =>
        int.TryParse(ReadString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static long? ReadLong(JsonElement element, string propertyName) =>
        long.TryParse(ReadString(element, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static ScreenScraperResult<T> InvalidResponse<T>() where T : class =>
        new(
            ScreenScraperRequestStatus.InvalidResponse,
            null,
            null,
            "ScreenScraper returned an invalid response.");

    private static void ValidateDeveloperCredentials(ScreenScraperDeveloperCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.DeveloperId) ||
            string.IsNullOrWhiteSpace(credentials.DeveloperPassword) ||
            string.IsNullOrWhiteSpace(credentials.SoftwareName))
        {
            throw new ArgumentException("ScreenScraper developer credentials are incomplete.", nameof(credentials));
        }
    }

    private static void ValidateUserCredentials(ScreenScraperUserCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            throw new ArgumentException("ScreenScraper account credentials are incomplete.", nameof(credentials));
    }

    private static void ValidateGameRequest(ScreenScraperGameRequest request)
    {
        if (request.SystemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "ScreenScraper system ID must be positive.");
        if (string.IsNullOrWhiteSpace(request.RomName))
            throw new ArgumentException("ROM name cannot be empty.", nameof(request));

        var hasForcedGameId = !string.IsNullOrWhiteSpace(request.ProviderGameId);
        var hasSerial = !string.IsNullOrWhiteSpace(request.Serial);
        var hasHashAndSize = request.RomSize > 0 &&
            (!string.IsNullOrWhiteSpace(request.Crc32) ||
             !string.IsNullOrWhiteSpace(request.Md5) ||
             !string.IsNullOrWhiteSpace(request.Sha1));
        // ScreenScraper matches by a hash+size, a disc serial, a confirmed game id, or — only when the
        // caller opts in — the ROM file name (arcade romsets, whose file name is the set identity).
        // Serial is the route for compressed disc containers (CHD/CSO/…) whose container bytes are not
        // the ROM. A file-name-only lookup stays opt-in so an under-specified request for a hashable
        // system, where a name-only match could silently return a rom hack, is still rejected.
        if (!hasForcedGameId && !hasSerial && !hasHashAndSize && !request.AllowFileNameMatch)
        {
            throw new ArgumentException(
                "Automatic ScreenScraper lookup requires a ROM hash and byte size, a serial, or a game id.",
                nameof(request));
        }
    }

    private static void AddIfPresent(
        ICollection<KeyValuePair<string, string>> parameters,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            parameters.Add(new(key, value.Trim()));
    }

    private static void AddIntIfPresent(
        ICollection<KeyValuePair<string, string>> parameters,
        string key,
        int? value)
    {
        if (value is { } number)
            parameters.Add(new(key, number.ToString(CultureInfo.InvariantCulture)));
    }
}
