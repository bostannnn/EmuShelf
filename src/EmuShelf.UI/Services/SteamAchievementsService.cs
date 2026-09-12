using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>Local account, account-scoped cache and bounded on-demand Steam refreshes.</summary>
public sealed class SteamAchievementsService : IAchievementProvider
{
    private readonly ISteamAchievementsClient _client;
    private readonly ISteamCredentialStore _credentials;
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private readonly string _root;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _requests = new(1, 1);
    private readonly Dictionary<string, AchievementSnapshot> _memory = [];
    private int _generation;
    private DateTimeOffset _retryAt;
    private bool _authFailed;
    private SteamProfile? _profile;
    private string? _key;

    public SteamAchievementsService(ISteamAchievementsClient client, ISteamCredentialStore credentials,
        ISettingsService settings, string cacheDirectory, HttpClient http)
    {
        _client = client; _credentials = credentials; _settings = settings; _http = http;
        _root = Path.Combine(cacheDirectory, "SteamAchievements");
        _key = credentials.Read();
        var saved = settings.Load();
        if (saved.SteamAchievementsSteamId is { Length: 17 } id && id.All(char.IsAsciiDigit))
            _profile = new(id, saved.SteamAchievementsProfileName ?? id);
    }

    public string Id => "steam";
    public string DisplayName => "Steam";
    public string? AccountId { get { lock (_gate) return _profile?.SteamId; } }
    public string? ProfileName { get { lock (_gate) return _profile?.DisplayName; } }
    public bool IsConnected { get { lock (_gate) return _profile is not null && !string.IsNullOrWhiteSpace(_key); } }
    public bool IsPersistent => _credentials.IsPersistent;
    public bool SupportsPoints => false;
    public bool SupportsHardcore => false;
    public event Action? Changed;

    public async Task<AchievementStatus> ConnectAsync(string profile, string key, CancellationToken cancellationToken = default)
    {
        key = key.Trim();
        if (key.Length != 32 || !key.All(Uri.IsHexDigit)) return AchievementStatus.AuthenticationFailed;
        int generation;
        lock (_gate) generation = ++_generation;
        var response = await _client.ResolveProfileAsync(profile, key, cancellationToken).ConfigureAwait(false);
        if (response.Profile is null) return response.Status;
        try
        {
            lock (_gate)
            {
                if (generation != _generation) return AchievementStatus.AccountChanged;
                var previousKey = _credentials.Read();
                _credentials.Write(key);
                try { _settings.Update(latest => latest with
                {
                    SteamAchievementsSteamId = response.Profile.SteamId,
                    SteamAchievementsProfileName = response.Profile.DisplayName,
                }); }
                catch
                {
                    if (previousKey is null) _credentials.Clear(); else _credentials.Write(previousKey);
                    throw;
                }
                _profile = response.Profile;
                _key = key;
                _memory.Clear(); _retryAt = default; _authFailed = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { return AchievementStatus.ServerError; }
        Changed?.Invoke();
        return AchievementStatus.Success;
    }

    public void Disconnect()
    {
        try
        {
            lock (_gate)
            {
                _credentials.Clear();
                _generation++; _profile = null; _key = null; _memory.Clear(); _retryAt = default; _authFailed = false;
                _settings.Update(latest => latest with { SteamAchievementsSteamId = null, SteamAchievementsProfileName = null });
            }
        }
        finally { Changed?.Invoke(); }
    }

    public AchievementSnapshot? GetCached(AchievementGameRef game)
    {
        lock (_gate)
        {
            if (_profile is null || !Valid(game)) return null;
            if (_memory.TryGetValue(game.GameId, out var snapshot)) return snapshot;
            try
            {
                var file = SnapshotPath(_profile.SteamId, game.GameId);
                if (!File.Exists(file) || new FileInfo(file).Length > 8 * 1024 * 1024) return null;
                snapshot = JsonSerializer.Deserialize(File.ReadAllText(file), SteamAchievementJsonContext.Default.AchievementSnapshot);
                if (snapshot?.AccountId != _profile.SteamId || snapshot.Game != game) return null;
                return _memory[game.GameId] = snapshot;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
        }
    }

    public Task<AchievementResult> RefreshAsync(AchievementGameRef game, CancellationToken cancellationToken = default, bool manual = false) =>
        RefreshCoreAsync(game, cancellationToken, manual, notifyChanged: true);

    public async Task<SteamAchievementSyncResult> SyncLibraryAsync(IEnumerable<AchievementGameRef> games,
        IProgress<SteamAchievementSyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var entries = games.Where(Valid).Distinct().ToArray();
        int generation;
        lock (_gate) generation = _generation;
        var completed = 0; var updated = 0; var unavailable = 0;
        var status = AchievementStatus.Success;
        progress?.Report(new(0, entries.Length, 0, 0));
        try
        {
            foreach (var game in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate) if (generation != _generation) { status = AchievementStatus.AccountChanged; break; }
                var result = await RefreshCoreAsync(game, cancellationToken, manual: true, notifyChanged: false, expectedGeneration: generation).ConfigureAwait(false);
                completed++;
                if (result.IsSuccess && result.Snapshot!.ProgressKnown) updated++; else unavailable++;
                progress?.Report(new(completed, entries.Length, updated, unavailable));
                if (result.Status is AchievementStatus.RateLimited or AchievementStatus.AuthenticationFailed or
                    AchievementStatus.NotConnected or AchievementStatus.AccountChanged or AchievementStatus.Offline or AchievementStatus.ServerError)
                { status = result.Status; break; }
                if (completed < entries.Length) await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
            return new(completed, entries.Length, updated, unavailable, status);
        }
        finally { Changed?.Invoke(); }
    }

    private async Task<AchievementResult> RefreshCoreAsync(AchievementGameRef game, CancellationToken cancellationToken,
        bool manual, bool notifyChanged, int? expectedGeneration = null)
    {
        if (!Valid(game)) return new(AchievementStatus.MalformedResponse);
        string? account, key;
        int generation;
        lock (_gate)
        {
            if (expectedGeneration is { } expected && expected != _generation) return new(AchievementStatus.AccountChanged);
            account = _profile?.SteamId; key = _key; generation = _generation;
        }
        if (account is null || string.IsNullOrWhiteSpace(key)) return new(AchievementStatus.NotConnected);
        await _requests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (generation != _generation) return new(AchievementStatus.AccountChanged);
                if (_authFailed) return new(AchievementStatus.AuthenticationFailed);
                if (_retryAt > DateTimeOffset.UtcNow) return new(AchievementStatus.RateLimited, RetryAfter: _retryAt - DateTimeOffset.UtcNow);
            }
            var cached = GetCached(game);
            if (!manual && cached is { ProgressKnown: true } && DateTimeOffset.UtcNow - cached.RefreshedAt < TimeSpan.FromMinutes(5))
                return new(AchievementStatus.Success, cached);
            var response = await _client.GetAchievementsAsync(account, game.GameId, key, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (generation != _generation) return new(AchievementStatus.AccountChanged);
                if (response.Status == AchievementStatus.AuthenticationFailed) _authFailed = true;
                if (response.Status == AchievementStatus.RateLimited)
                    _retryAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp((response.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds, 1, 86400));
                // Preserve known progress during a transient outage. An explicit privacy failure
                // replaces it with unknown state, so stale unlocks aren't presented as current.
                if (response.Snapshot is { } snapshot &&
                    (cached is null || response.Status is AchievementStatus.Success or AchievementStatus.Unavailable))
                {
                    if (snapshot.AccountId != account || snapshot.Game != game) return new(AchievementStatus.MalformedResponse);
                    _memory[game.GameId] = snapshot;
                    try
                    {
                        var path = SnapshotPath(account, game.GameId);
                        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(snapshot, SteamAchievementJsonContext.Default.AchievementSnapshot));
                        File.Move(path + ".tmp", path, true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* In-memory result remains usable. */ }
                }
            }
            if (notifyChanged) Changed?.Invoke();
            return response with { Snapshot = GetCached(game) ?? response.Snapshot };
        }
        finally { _requests.Release(); }
    }

    public async Task<string?> GetIconPathAsync(string icon, CancellationToken cancellationToken = default)
    {
        var uri = NormalizeIconUri(icon);
        if (uri is null) return null;
        var path = Path.Combine(_root, "Icons", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))) + ".img");
        if (File.Exists(path)) return path;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 2 * 1024 * 1024) return null;
                buffer.Write(chunk, 0, count);
            }
            var bytes = buffer.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N");
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, true);
            return path;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException) { return null; }
    }

    private static Uri? NormalizeIconUri(string icon)
    {
        if (!Uri.TryCreate(icon, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return null;
        var steamHost = uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".steamcommunity.com", StringComparison.OrdinalIgnoreCase);
        var legacyHost = uri.Host.Equals("steamcdn-a.akamaihd.net", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("media.steampowered.com", StringComparison.OrdinalIgnoreCase);
        if (!steamHost && !legacyHost) return null;
        const string oldPrefix = "/steamcommunity/public/images/apps/";
        if (uri.AbsolutePath.StartsWith(oldPrefix, StringComparison.Ordinal))
        {
            var asset = uri.AbsolutePath[oldPrefix.Length..];
            if (!System.Text.RegularExpressions.Regex.IsMatch(asset, @"^[0-9]+/[a-fA-F0-9]{40}\.(jpg|png)$")) return null;
            // Steam still returns this legacy path in schemas, but its old CDNs now return 404.
            // Normalize on read so previously cached snapshots also recover without a metadata refresh.
            return new Uri("https://shared.fastly.steamstatic.com/community_assets/images/apps/" + asset);
        }
        return steamHost && uri.Scheme == "https" ? uri : null;
    }

    private string SnapshotPath(string account, string app) => Path.Combine(_root, account, app + "-english.json");
    private static bool Valid(AchievementGameRef game) => game.Provider == "steam" &&
        int.TryParse(game.GameId, out var app) && app > 0 && game.GameId.All(char.IsAsciiDigit);
}

[JsonSerializable(typeof(AchievementSnapshot))]
internal partial class SteamAchievementJsonContext : JsonSerializerContext;
