using System.Globalization;
using System.Net;
using System.Text.Json;
using EmuShelf.Core.Achievements;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>Public, read-only Steam Web API. Secrets never enter request URIs or errors.</summary>
public sealed class SteamAchievementsClient(HttpClient http) : ISteamAchievementsClient
{
    // Definitions are account-independent. Progress is never stored in this cache.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (string Json, DateTimeOffset Updated)> _schemas = new();
    public async Task<SteamProfileResult> ResolveProfileAsync(string profile, string apiKey, CancellationToken cancellationToken)
    {
        try { return await ResolveProfileCoreAsync(profile, apiKey, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException)
        { return new(AchievementStatus.MalformedResponse); }
    }

    private async Task<SteamProfileResult> ResolveProfileCoreAsync(string profile, string apiKey, CancellationToken cancellationToken)
    {
        var input = profile.Trim();
        string? vanity = null;
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != "https" || !uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase))
                return new(AchievementStatus.MalformedResponse);
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[0] is not ("id" or "profiles"))
                return new(AchievementStatus.MalformedResponse);
            if (parts[0] == "id") vanity = parts[1];
            else input = parts[1];
        }
        if (vanity is not null)
        {
            var resolved = await GetAsync("ISteamUser/ResolveVanityURL/v1/?vanityurl=" + Uri.EscapeDataString(vanity), apiKey, cancellationToken);
            using var doc = resolved.Document;
            if (doc is null) return new(resolved.Status);
            if (!doc.RootElement.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("steamid", out var id)) return new(AchievementStatus.Unavailable);
            input = id.GetString() ?? "";
        }
        if (input.Length != 17 || !input.All(char.IsAsciiDigit) || !ulong.TryParse(input, out _))
            return new(AchievementStatus.MalformedResponse);
        var result = await GetAsync("ISteamUser/GetPlayerSummaries/v2/?steamids=" + input, apiKey, cancellationToken);
        using var profileDoc = result.Document;
        if (profileDoc is null) return new(result.Status);
        try
        {
            var players = profileDoc.RootElement.GetProperty("response").GetProperty("players");
            foreach (var player in players.EnumerateArray())
                if (player.GetProperty("steamid").GetString() == input)
                    return new(AchievementStatus.Success, new(input, Text(player, "personaname", input)));
            return new(AchievementStatus.Unavailable);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException)
        { return new(AchievementStatus.MalformedResponse); }
    }

    public async Task<AchievementResult> GetAchievementsAsync(string steamId, string appId, string apiKey,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return new(AchievementStatus.MalformedResponse);
        var schema = _schemas.TryGetValue(id, out var cachedSchema) && DateTimeOffset.UtcNow - cachedSchema.Updated < TimeSpan.FromHours(24)
            ? (Status: AchievementStatus.Success, Document: (JsonDocument?)JsonDocument.Parse(cachedSchema.Json), RetryAfter: (TimeSpan?)null)
            : await GetAsync($"ISteamUserStats/GetSchemaForGame/v2/?appid={id}&l=english", apiKey, cancellationToken);
        using var schemaDoc = schema.Document;
        if (schemaDoc is null) return new(schema.Status, RetryAfter: schema.RetryAfter);
        try
        {
            var game = schemaDoc.RootElement.GetProperty("game");
            if (game.ValueKind != JsonValueKind.Object || !game.TryGetProperty("gameName", out _))
                return new(AchievementStatus.Unavailable);
            var definitions = game.TryGetProperty("availableGameStats", out var stats) &&
                stats.TryGetProperty("achievements", out var array) ? array : default;
            if (definitions.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Array))
                return new(AchievementStatus.MalformedResponse);
            _schemas[id] = (schemaDoc.RootElement.GetRawText(), cachedSchema.Updated == default || DateTimeOffset.UtcNow - cachedSchema.Updated >= TimeSpan.FromHours(24) ? DateTimeOffset.UtcNow : cachedSchema.Updated);
            var reference = new AchievementGameRef("steam", id.ToString(CultureInfo.InvariantCulture));
            if (definitions.ValueKind == JsonValueKind.Undefined || definitions.GetArrayLength() == 0)
                return new(AchievementStatus.Success, new(reference, steamId, Text(game, "gameName"), [], DateTimeOffset.UtcNow));

            var progress = await GetAsync($"ISteamUserStats/GetPlayerAchievements/v1/?appid={id}&steamid={Uri.EscapeDataString(steamId)}&l=english", apiKey, cancellationToken, privateProgress: true);
            using var progressDoc = progress.Document;
            var unlocks = new Dictionary<string, (bool Unlocked, DateTimeOffset? Date)>(StringComparer.Ordinal);
            var status = progress.Status;
            if (progressDoc is not null)
            {
                var player = progressDoc.RootElement.GetProperty("playerstats");
                if (player.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True &&
                    player.TryGetProperty("achievements", out var achievements) && achievements.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in achievements.EnumerateArray())
                    {
                        var unlocked = item.GetProperty("achieved").GetInt32() == 1;
                        var seconds = item.TryGetProperty("unlocktime", out var time) ? time.GetInt64() : 0;
                        unlocks[Text(item, "apiname")] = (unlocked,
                            unlocked && seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null);
                    }
                    status = AchievementStatus.Success;
                }
                else status = AchievementStatus.Unavailable;
            }
            var entries = new List<AchievementEntry>();
            foreach (var definition in definitions.EnumerateArray())
            {
                var name = definition.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name)) return new(AchievementStatus.MalformedResponse);
                var known = status == AchievementStatus.Success && unlocks.TryGetValue(name, out _);
                unlocks.TryGetValue(name, out var earned);
                entries.Add(new(name, Text(definition, "displayName", name), Text(definition, "description"),
                    Text(definition, known && earned.Unlocked ? "icon" : "icongray"), entries.Count,
                    known ? earned.Unlocked : null, known ? earned.Date : null,
                    IsHidden: definition.TryGetProperty("hidden", out var hidden) && hidden.GetInt32() != 0));
            }
            var snapshot = new AchievementSnapshot(reference, steamId, Text(game, "gameName"), entries,
                DateTimeOffset.UtcNow, entries.All(entry => entry.IsUnlocked is not null));
            return new(status, snapshot, progress.RetryAfter);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException or ArgumentOutOfRangeException)
        { return new(AchievementStatus.MalformedResponse); }
    }

    private static string Text(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;

    private async Task<(AchievementStatus Status, JsonDocument? Document, TimeSpan? RetryAfter)> GetAsync(
        string path, string apiKey, CancellationToken cancellationToken, bool privateProgress = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.steampowered.com/" + path);
            request.Headers.Add("x-webapi-key", apiKey);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (privateProgress && response.StatusCode == HttpStatusCode.Forbidden)
                return (AchievementStatus.Unavailable, null, null);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (AchievementStatus.AuthenticationFailed, null, null);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return (AchievementStatus.RateLimited, null, response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                return (AchievementStatus.Unavailable, null, null);
            if (!response.IsSuccessStatusCode) return (AchievementStatus.ServerError, null, null);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 8 * 1024 * 1024) return (AchievementStatus.MalformedResponse, null, null);
                buffer.Write(chunk, 0, count);
            }
            var bytes = buffer.ToArray();
            if (bytes.Length > 8 * 1024 * 1024) return (AchievementStatus.MalformedResponse, null, null);
            return (AchievementStatus.Success, JsonDocument.Parse(bytes), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return (AchievementStatus.Offline, null, null); }
        catch (HttpRequestException) { return (AchievementStatus.Offline, null, null); }
        catch (IOException) { return (AchievementStatus.Offline, null, null); }
        catch (JsonException) { return (AchievementStatus.MalformedResponse, null, null); }
        catch (FormatException) { return (AchievementStatus.AuthenticationFailed, null, null); }
    }
}
