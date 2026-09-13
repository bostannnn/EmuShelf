using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>Local scraped media plus a bounded, lazy cache of public Steam publisher assets.</summary>
public sealed class CompanionMediaSource(
    IGameDetailsStore details, HttpClient http, IRemoteArtworkDownloader downloader, IAppPaths paths)
    : ICompanionMediaSource, IGameMediaEnricher
{
    private readonly string _cache = Path.Combine(paths.CacheDirectory, "CompanionMedia");
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private readonly HashSet<long> _migrated = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _enrichmentGate = new(1, 1);
    private readonly SemaphoreSlim _manifestGate = new(1, 1);
    private readonly Dictionary<int, DateTimeOffset> _retryAfter = [];
    private readonly Dictionary<string, DateTimeOffset> _unavailableMedia = [];

    public event Action<long>? ArtworkChanged;

    public bool Supports(Game game) => game.SystemId == "steam" &&
        int.TryParse(game.ExternalSourceEntryId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0;

    public bool HasMissingArtwork(Game game)
    {
        if (!Supports(game)) return false;
        var media = details.GetDetails(game.Id).Media.Where(i => File.Exists(i.LocalPath)).ToArray();
        return new[] { GameMediaKind.Fanart, GameMediaKind.Wheel, GameMediaKind.Screenshot }
            .Any(kind => !media.Any(i => i.Kind == kind));
    }

    public async Task<CompanionMediaSet> GetAsync(Game game, CancellationToken token)
    {
        await MigrateCachedArtworkAsync(game, token).ConfigureAwait(false);
        var local = details.GetDetails(game.Id).Media
            .OrderByDescending(i => i.IsSelected).ThenBy(i => i.Id)
            .Where(i => File.Exists(i.LocalPath))
            .Select(i => new CompanionMediaItem(Label(i.Kind), i.Kind, i.LocalPath)).ToList();
        if (game.CoverPath is { } cover && File.Exists(cover) && !local.Any(i => i.LocalPath == cover))
            local.Add(new("Cover", GameMediaKind.BoxFront, cover));
        // Video bytes stay lazy: reading a previously fetched descriptor is local-only.
        // Resting artwork always comes from the shared media store, for every platform.
        if (Supports(game))
        {
            var remote = await ReadSteamAsync(int.Parse(game.ExternalSourceEntryId!, CultureInfo.InvariantCulture), false, token)
                .ConfigureAwait(false);
            local.AddRange(remote.Where(i => i.IsVideo && !local.Any(l => l.IsVideo)));
        }
        return new(game.Id, game.Title, local.DistinctBy(i => i.LocalPath ?? i.RemoteUri?.AbsoluteUri).ToArray());
    }

    public async Task<int> FetchMissingAsync(Game game, CancellationToken token)
    {
        if (!Supports(game)) return 0;
        await _enrichmentGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var existing = details.GetDetails(game.Id).Media.Where(i => File.Exists(i.LocalPath)).ToArray();
            var wanted = new[] { GameMediaKind.Fanart, GameMediaKind.Wheel, GameMediaKind.Screenshot }
                .Where(kind => !existing.Any(i => i.Kind == kind)).ToHashSet();
            if (wanted.Count == 0) return 0;
            var remote = await ReadSteamAsync(int.Parse(game.ExternalSourceEntryId!, CultureInfo.InvariantCulture), true, token)
                .ConfigureAwait(false);
            var applied = 0;
            foreach (var item in remote.Where(i => wanted.Contains(i.Kind)))
            {
                token.ThrowIfCancellationRequested();
                var path = await GetLocalPathAsync(item, token).ConfigureAwait(false);
                if (path is null) continue;
                // Recheck after network work: a scrape/manual choice made meanwhile wins.
                if (item.Kind != GameMediaKind.Screenshot && details.GetDetails(game.Id).Media
                    .Any(i => i.Kind == item.Kind && File.Exists(i.LocalPath))) continue;
                SaveArtwork(game, item, path);
                if (item.Kind is GameMediaKind.Fanart or GameMediaKind.Wheel) ArtworkChanged?.Invoke(game.Id);
                applied++;
            }
            if (applied > 0) ArtworkChanged?.Invoke(game.Id);
            return applied;
        }
        finally { _enrichmentGate.Release(); }
    }

    // Compatibility upgrade for artwork fetched by earlier preview builds. No requests and no
    // provider-specific display fallback: once copied, these are ordinary saved media records.
    private async Task MigrateCachedArtworkAsync(Game game, CancellationToken token)
    {
        if (!Supports(game)) return;
        await _migrationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_migrated.Contains(game.Id)) return;
            var remote = await ReadSteamAsync(int.Parse(game.ExternalSourceEntryId!, CultureInfo.InvariantCulture), false, token)
                .ConfigureAwait(false);
            var existing = details.GetDetails(game.Id).Media.Where(i => File.Exists(i.LocalPath)).ToArray();
            foreach (var item in remote.Where(i => !i.IsVideo && !existing.Any(e => e.Kind == i.Kind)))
            {
                token.ThrowIfCancellationRequested();
                var path = CachePath(item);
                if (File.Exists(path)) SaveArtwork(game, item, path);
            }
            _migrated.Add(game.Id);
        }
        finally { _migrationGate.Release(); }
    }

    private void SaveArtwork(Game game, CompanionMediaItem item, string path)
    {
        var directory = Path.Combine(paths.CoversDirectory, "Media", game.Id.ToString(CultureInfo.InvariantCulture), "steam");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(path));
        File.Copy(path, destination, overwrite: true);
        var selected = !details.GetDetails(game.Id).Media.Any(i => i.Kind == item.Kind && File.Exists(i.LocalPath));
        details.SaveMedia(new GameMediaAsset(0, game.Id, item.Kind, destination, selected,
            selected ? GameMediaSelectionOrigin.Provider : null, GameMediaOrigin.Provider,
            "steam", game.ExternalSourceEntryId, item.RemoteUri?.AbsoluteUri, null, null,
            Path.GetExtension(destination), null, null, null, null, null, DateTimeOffset.UtcNow));
    }

    public async Task<string?> GetLocalPathAsync(CompanionMediaItem item, CancellationToken token)
    {
        if (item.LocalPath is { } local && File.Exists(local)) return local;
        if (item.RemoteUri is null) return null;
        // Cached images must remain responsive while an unrelated video is downloading.
        var cachedPath = CachePath(item);
        if (File.Exists(cachedPath)) return cachedPath;
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var path = CachePath(item);
            if (File.Exists(path)) return path;
            if (_unavailableMedia.GetValueOrDefault(path) > DateTimeOffset.UtcNow) return null;
            var extension = Path.GetExtension(item.RemoteUri.AbsolutePath);
            var downloaded = await downloader.DownloadFirstAsync(
                [new ArtworkCandidate("steam", item.RemoteUri, extension, item.IsVideo ? RemoteMediaKind.Video : RemoteMediaKind.Image)], token).ConfigureAwait(false);
            if (downloaded is null && await ResolveHashedLogoAsync(item, token).ConfigureAwait(false) is { } logo)
                downloaded = await downloader.DownloadFirstAsync([new ArtworkCandidate("steam", logo, ".png")], token).ConfigureAwait(false);
            if (downloaded is null)
            {
                _unavailableMedia[path] = DateTimeOffset.UtcNow.AddMinutes(10);
                return null;
            }
            try
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(_cache);
                File.Move(downloaded.TemporaryPath, path, overwrite: true);
                TrimCache(path);
                return path;
            }
            finally { if (File.Exists(downloaded.TemporaryPath)) File.Delete(downloaded.TemporaryPath); }
        }
        finally { _gate.Release(); }
    }

    // Newer Steam library logos have content-hashed paths absent from StoreBrowse. This
    // public SteamCMD metadata mirror is consulted only after the direct publisher route fails.
    private async Task<Uri?> ResolveHashedLogoAsync(CompanionMediaItem item, CancellationToken token)
    {
        if (item.Kind != GameMediaKind.Wheel || item.RemoteUri is null) return null;
        var match = Regex.Match(item.RemoteUri.AbsolutePath, @"^/store_item_assets/steam/apps/([0-9]+)/logo\.png$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var id)) return null;
        var cache = Path.Combine(_cache, $"{id}-logo.json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if (File.Exists(cache) && new FileInfo(cache).Length < 4096 &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(7))
            {
                var saved = JsonSerializer.Deserialize<string>(await File.ReadAllTextAsync(cache, token));
                if (saved is not null && Uri.TryCreate(saved, UriKind.Absolute, out var uri) &&
                    uri.Host == "shared.fastly.steamstatic.com" && uri.Scheme == "https" &&
                    Regex.IsMatch(uri.AbsolutePath, $@"^/store_item_assets/steam/apps/{id}/[a-fA-F0-9]{{40}}/logo(?:_2x)?\.png$")) return uri;
            }
            using var response = await http.GetAsync($"https://api.steamcmd.net/v1/info/{id}",
                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 2 * 1024 * 1024) return null;
                buffer.Write(chunk, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var logo = ParseSteamLibraryLogo(document.RootElement, id);
            if (logo is not null)
            {
                Directory.CreateDirectory(_cache);
                AtomicFile.WriteAllBytes(cache, JsonSerializer.SerializeToUtf8Bytes(logo.AbsoluteUri));
            }
            return logo;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception e) when (e is IOException or HttpRequestException or JsonException) { return null; }
    }

    public static Uri? ParseSteamLibraryLogo(JsonElement root, int id)
    {
        try
        {
            var logo = root.GetProperty("data").GetProperty(id.ToString(CultureInfo.InvariantCulture))
                .GetProperty("common").GetProperty("library_assets_full").GetProperty("library_logo");
            foreach (var size in new[] { "image2x", "image" })
                if (logo.TryGetProperty(size, out var localized) && String(localized, "english") is { } path &&
                    Regex.IsMatch(path, @"^[a-fA-F0-9]{40}/logo(?:_2x)?\.png$"))
                    return new Uri($"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/{path}");
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException) { }
        return null;
    }

    private string CachePath(CompanionMediaItem item) => Path.Combine(_cache,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.RemoteUri!.AbsoluteUri))) +
        Path.GetExtension(item.RemoteUri.AbsolutePath));

    private void TrimCache(string keep)
    {
        // Only this feature's own recreatable payloads; never scraped media, covers, games or saves.
        var files = new DirectoryInfo(_cache).EnumerateFiles().Where(f => f.Extension != ".json")
            .OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        long total = 0;
        foreach (var file in files)
        {
            total += file.Length;
            if (total > 256L * 1024 * 1024 && file.FullName != keep)
                try { file.Delete(); } catch (IOException) { }
        }
    }

    private async Task<IReadOnlyList<CompanionMediaItem>> ReadSteamAsync(int id, bool allowNetwork, CancellationToken token)
    {
        // Presentation never waits behind a provider request, even during a library-wide fetch.
        if (!allowNetwork)
        {
            var savedPath = Path.Combine(_cache, $"{id}.json");
            try
            {
                if (!File.Exists(savedPath) || new FileInfo(savedPath).Length > 1024 * 1024) return [];
                using var savedDocument = JsonDocument.Parse(await File.ReadAllTextAsync(savedPath, token).ConfigureAwait(false));
                return ParseSteam(savedDocument.RootElement, id);
            }
            catch (Exception e) when (e is IOException or JsonException) { return []; }
        }
        await _manifestGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var cache = Path.Combine(_cache, $"{id}.json");
            IReadOnlyList<CompanionMediaItem> saved = [];
            if (File.Exists(cache) && new FileInfo(cache).Length <= 1024 * 1024)
            {
                try
                {
                    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(cache, token).ConfigureAwait(false));
                    saved = ParseSteam(document.RootElement, id);
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < TimeSpan.FromDays(7)) return saved;
                }
                catch (Exception e) when (e is IOException or JsonException) { }
            }
            if (!allowNetwork || _retryAfter.GetValueOrDefault(id) > DateTimeOffset.UtcNow) return saved;
            _retryAfter[id] = DateTimeOffset.UtcNow.AddMinutes(2);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var input = JsonSerializer.Serialize(new
                {
                    ids = new[] { new { appid = id } },
                    context = new { language = "english", country_code = "US" },
                    data_request = new { include_assets = true, include_screenshots = true, include_trailers = true }
                });
                using var response = await http.GetAsync(
                    "https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json=" + Uri.EscapeDataString(input),
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return saved;
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + count > 1024 * 1024) return saved;
                    buffer.Write(chunk, 0, count);
                }
                using var document = JsonDocument.Parse(buffer.ToArray());
                var result = ParseSteam(document.RootElement, id);
                // Do not replace a working offline manifest with an empty/failed store response.
                if (result.Count == 0) return saved;
                Directory.CreateDirectory(_cache);
                AtomicFile.WriteAllBytes(cache, buffer.ToArray());
                return result;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return saved; }
            catch (Exception e) when (e is HttpRequestException or IOException or JsonException) { return saved; }
        }
        finally { _manifestGate.Release(); }
    }

    public static IReadOnlyList<CompanionMediaItem> ParseSteam(JsonElement root, int appId)
    {
        try { return ParseSteamCore(root, appId); }
        catch (Exception e) when (e is InvalidOperationException or FormatException or KeyNotFoundException or UriFormatException)
        { return []; }
    }

    private static IReadOnlyList<CompanionMediaItem> ParseSteamCore(JsonElement root, int appId)
    {
        var result = new List<CompanionMediaItem>();
        if (!root.TryGetProperty("response", out var response) ||
            !response.TryGetProperty("store_items", out var items) || items.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("appid", out var id) || !id.TryGetInt32(out var value) || value != appId ||
                !item.TryGetProperty("success", out var success) || !success.TryGetInt32(out var ok) || ok != 1) continue;
            if (item.TryGetProperty("assets", out var assets))
            {
                var format = String(assets, "asset_url_format");
                if (format is not null)
                {
                    AddImage(format.Replace("${FILENAME}", String(assets, "library_hero_2x") ??
                        String(assets, "library_hero") ?? "library_hero.jpg", StringComparison.Ordinal), GameMediaKind.Fanart, "Artwork");
                    // StoreBrowse omits library logos. The publisher's legacy logo route is separate.
                    AddImage(format.Replace("${FILENAME}", String(assets, "library_logo") ?? "logo.png",
                        StringComparison.Ordinal), GameMediaKind.Wheel, "Logo");
                }
            }
            if (item.TryGetProperty("screenshots", out var screenshots))
                foreach (var group in new[] { "all_ages_screenshots", "mature_content_screenshots" })
                    if (screenshots.TryGetProperty(group, out var shots) && shots.ValueKind == JsonValueKind.Array)
                        foreach (var shot in shots.EnumerateArray().Take(12))
                            if (String(shot, "filename") is { } file) AddImage(file, GameMediaKind.Screenshot, "Screenshot");
            if (item.TryGetProperty("trailers", out var trailers))
                foreach (var group in new[] { "highlights", "other_trailers" })
                    if (trailers.TryGetProperty(group, out var videos) && videos.ValueKind == JsonValueKind.Array)
                        foreach (var video in videos.EnumerateArray().Take(2))
                            if (video.TryGetProperty("microtrailer", out var clips) && clips.ValueKind == JsonValueKind.Array)
                                foreach (var clip in clips.EnumerateArray())
                                    if (String(clip, "type") == "video/mp4" && String(clip, "filename") is { } file &&
                                        Regex.IsMatch(file, $@"^{appId}/[0-9]+/[a-fA-F0-9]{{40}}/[0-9]+/microtrailer\.mp4$"))
                                        result.Add(new("Video preview", GameMediaKind.Video, null,
                                            new Uri("https://video.fastly.steamstatic.com/store_trailers/" + file)));
        }
        return result.DistinctBy(i => i.RemoteUri).Take(30).ToArray();

        void AddImage(string relative, GameMediaKind kind, string title)
        {
            var path = relative.Split('?')[0];
            if (!Regex.IsMatch(path, $@"^steam/apps/{appId}/(?:[a-fA-F0-9]{{40}}/)?[a-zA-Z0-9_.-]+\.(?:jpg|jpeg|png|webp)$")) return;
            if (relative.Contains('#') || relative.Contains('\\')) return;
            result.Add(new(title, kind, null, new Uri("https://shared.fastly.steamstatic.com/store_item_assets/" + relative)));
        }
    }

    private static string? String(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

    private static string Label(GameMediaKind kind) => kind switch
    {
        GameMediaKind.BoxFront => "Cover", GameMediaKind.BoxBack => "Box back", GameMediaKind.BoxSpine => "Spine",
        GameMediaKind.Fanart => "Artwork", GameMediaKind.Wheel => "Logo", GameMediaKind.TitleScreen => "Title screen",
        GameMediaKind.Video => "Video", GameMediaKind.PhysicalMedia => "Cartridge / disc",
        GameMediaKind.PhysicalMediaTexture => "Media label", _ => "Screenshot"
    };
}
