using System.Text.Json;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.Infrastructure.Tests;

public class CompanionMediaSourceTests
{
    [Fact]
    public void ParsesPublisherHeroScreenshotsAndMp4Preview()
    {
        using var document = JsonDocument.Parse("""
        {"response":{"store_items":[{"appid":250180,"success":1,
          "assets":{"asset_url_format":"steam/apps/250180/${FILENAME}?t=1","library_hero":"library_hero.jpg","library_logo":"logo.png"},
          "screenshots":{"all_ages_screenshots":[{"filename":"steam/apps/250180/ss_123.jpg?t=1"}]},
          "trailers":{"highlights":[{"microtrailer":[
            {"filename":"250180/16879/6c5e9ecdabb2ec0297cd8a12d0e952a8e0356f44/1751266778/microtrailer.webm","type":"video/webm"},
            {"filename":"250180/16879/6c5e9ecdabb2ec0297cd8a12d0e952a8e0356f44/1751266778/microtrailer.mp4","type":"video/mp4"}]}]}}]}}
        """);
        var media = CompanionMediaSource.ParseSteam(document.RootElement, 250180);
        Assert.Equal(4, media.Count);
        Assert.Contains(media, i => i.Kind == GameMediaKind.Fanart);
        Assert.Contains(media, i => i.Kind == GameMediaKind.Screenshot);
        Assert.Contains(media, i => i.Kind == GameMediaKind.Wheel);
        Assert.Equal("video.fastly.steamstatic.com", media.Single(i => i.IsVideo).RemoteUri!.Host);
        Assert.All(media, item => Assert.Null(item.LocalPath));
    }

    [Theory]
    [InlineData("steam/apps/9/ss.jpg")]
    [InlineData("steam/apps/250180/../ss.jpg")]
    [InlineData("https://evil.example/ss.jpg")]
    [InlineData("file:///sdcard/test.jpg")]
    public void RejectsWrongAppAndUntrustedMediaPaths(string filename)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            response = new { store_items = new[] { new { appid = 250180, success = 1,
                screenshots = new { all_ages_screenshots = new[] { new { filename } } } } } }
        }));
        Assert.Empty(CompanionMediaSource.ParseSteam(document.RootElement, 250180));
    }

    [Fact]
    public void RealStoreAssetsResolveSeparateLogoAndNeverUseStoreBannerAsFanart()
    {
        using var document = JsonDocument.Parse("""
        {"response":{"store_items":[{"appid":447150,"success":1,
          "assets":{"asset_url_format":"steam/apps/447150/${FILENAME}?t=1",
          "library_hero":"library_hero.jpg","header":"header.jpg"}}]}}
        """);
        var media = CompanionMediaSource.ParseSteam(document.RootElement, 447150);
        Assert.Equal("/store_item_assets/steam/apps/447150/logo.png", media.Single(i => i.Kind == GameMediaKind.Wheel).RemoteUri!.AbsolutePath);
        Assert.EndsWith("library_hero.jpg", media.Single(i => i.Kind == GameMediaKind.Fanart).RemoteUri!.AbsolutePath);
        Assert.DoesNotContain(media, i => i.RemoteUri!.AbsolutePath.EndsWith("header.jpg"));
    }

    [Theory]
    [InlineData("776432359d586a2cf428b40cab1c3f827fc8e2dc/logo_2x.png", true)]
    [InlineData("../../private/logo.png", false)]
    [InlineData("https://other.example/logo.png", false)]
    public void HashedLogoMetadataIsBoundToRequestedAppAndSteamCdn(string path, bool valid)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { data = new Dictionary<string, object> {
            ["2062430"] = new { common = new { library_assets_full = new { library_logo = new { image2x = new { english = path } } } } }
        } }));
        var uri = CompanionMediaSource.ParseSteamLibraryLogo(doc.RootElement, 2062430);
        Assert.Equal(valid, uri is not null);
        if (uri is not null) Assert.Equal("shared.fastly.steamstatic.com", uri.Host);
        Assert.Null(CompanionMediaSource.ParseSteamLibraryLogo(doc.RootElement, 123));
    }

    [Fact]
    public void ScreenshotsAndCoversStayInTheGallery()
    {
        var cover = new CompanionMediaItem("Cover", GameMediaKind.BoxFront, "/cover.jpg");
        var media = new CompanionMediaSet(1, "Steam game", [cover]);
        Assert.Null(media.Background);
        Assert.Null(media.Logo);
        var screenshot = new CompanionMediaItem("Screenshot", GameMediaKind.Screenshot, "/shot.jpg");
        Assert.Null((media with { Items = [cover, screenshot] }).Background);
    }
}

public class CompanionMediaCacheTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task OfflineLocalCoverRequiresNoNetworkAndPreservesTheOriginal()
    {
        AppPaths.EnsureDirectoriesExist();
        var cover = Path.Combine(AppPaths.CoversDirectory, "manual.png");
        await File.WriteAllTextAsync(cover, "original", CancellationToken.None);
        using var handler = new StoreHandler();
        using var http = new HttpClient(handler);
        var source = new CompanionMediaSource(new MemoryDetails(), http, new Downloader(BaseDirectory), AppPaths);
        var game = Game() with { CoverPath = cover };
        var media = await source.GetAsync(game, CancellationToken.None);
        Assert.Equal(cover, Assert.Single(media.Items).LocalPath);
        Assert.Equal(0, handler.Calls);
        Assert.Equal("original", await File.ReadAllTextAsync(cover, CancellationToken.None));
    }

    [Fact]
    public async Task ExplicitFetchPersistsArtworkAndSelectionNeverDownloads()
    {
        AppPaths.EnsureDirectoriesExist();
        using var handler = new StoreHandler();
        using var http = new HttpClient(handler);
        var download = new Downloader(BaseDirectory);
        var details = new MemoryDetails();
        var source = new CompanionMediaSource(details, http, download, AppPaths);
        Assert.Empty((await source.GetAsync(Game(), CancellationToken.None)).Items);
        Assert.Equal(0, handler.Calls);
        Assert.Equal(2, await source.FetchMissingAsync(Game(), CancellationToken.None));
        Assert.Equal(2, download.Calls);
        var first = await source.GetAsync(Game(), CancellationToken.None);
        Assert.Equal(2, first.Items.Count);
        Assert.All(first.Items, i => Assert.StartsWith(AppPaths.CoversDirectory, i.LocalPath));
        // Earlier previews only saved cached files: promote them without another request.
        var upgraded = new CompanionMediaSource(new MemoryDetails(), http, download, AppPaths);
        Assert.Equal(2, (await upgraded.GetAsync(Game(), CancellationToken.None)).Items.Count);
        Directory.Delete(Path.Combine(AppPaths.CacheDirectory, "CompanionMedia"), true);
        var offline = await source.GetAsync(Game(), CancellationToken.None);
        Assert.Equal(first.Items, offline.Items);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(2, download.Calls);
    }

    [Theory]
    [InlineData(GameMediaSelectionOrigin.User)]
    [InlineData(GameMediaSelectionOrigin.Provider)]
    public async Task FetchPreservesExistingScreenScraperArtwork(GameMediaSelectionOrigin selection)
    {
        AppPaths.EnsureDirectoriesExist();
        var path = Path.Combine(AppPaths.CoversDirectory, "existing.png");
        await File.WriteAllTextAsync(path, "original");
        var details = new MemoryDetails();
        details.SaveMedia(new(0, 1, GameMediaKind.Fanart, path, true, selection, GameMediaOrigin.Provider,
            "screenscraper", "123", null, null, null, ".png", null, null, null, null, null, DateTimeOffset.UtcNow));
        using var http = new HttpClient(new StoreHandler());
        var download = new Downloader(BaseDirectory);
        var source = new CompanionMediaSource(details, http, download, AppPaths);
        Assert.Equal(1, await source.FetchMissingAsync(Game(), CancellationToken.None));
        Assert.Equal(1, download.Calls);
        var media = await source.GetAsync(Game(), CancellationToken.None);
        Assert.Equal(path, media.Background!.LocalPath);
        Assert.NotNull(media.Logo);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Equal(0, await source.FetchMissingAsync(Game(), CancellationToken.None));
    }

    [Fact]
    public async Task SavedArtworkDoesNotWaitForAnUnrelatedDownload()
    {
        AppPaths.EnsureDirectoriesExist();
        using var http = new HttpClient(new StoreHandler());
        var download = new BlockingDownloader(BaseDirectory);
        var source = new CompanionMediaSource(new MemoryDetails(), http, download, AppPaths);
        await source.FetchMissingAsync(Game(), CancellationToken.None);
        var hero = (await source.GetAsync(Game(), CancellationToken.None)).Background!;
        download.Block = true;
        using var cancel = new CancellationTokenSource();
        var pending = source.GetLocalPathAsync(new("Video", GameMediaKind.Video, null,
            new Uri("https://video.fastly.steamstatic.com/test.mp4")), cancel.Token);
        await download.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var offline = await source.GetAsync(Game(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(hero.LocalPath, offline.Background!.LocalPath);
            Assert.Equal(hero.LocalPath, await source.GetLocalPathAsync(hero, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
    }

    [Fact]
    public async Task AvailableFanartIsPublishedWhileLogoDownloadIsStillPending()
    {
        AppPaths.EnsureDirectoriesExist();
        using var http = new HttpClient(new StoreHandler());
        var downloader = new BlockingDownloader(BaseDirectory) { BlockOnCall = 2 };
        var source = new CompanionMediaSource(new MemoryDetails(), http, downloader, AppPaths);
        var changed = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.ArtworkChanged += id => changed.TrySetResult(id);
        using var cancel = new CancellationTokenSource();
        var fetch = source.FetchMissingAsync(Game(), cancel.Token);
        await downloader.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            Assert.Equal(1, await changed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            var saved = await source.GetAsync(Game(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotNull(saved.Background);
            Assert.Null(saved.Logo);
            Assert.False(fetch.IsCompleted);
        }
        finally
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        }
    }

    private sealed class BlockingDownloader(string root) : IRemoteArtworkDownloader
    {
        public bool Block;
        public int BlockOnCall = int.MaxValue;
        private int _calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DownloadedArtwork?> DownloadFirstAsync(IReadOnlyList<ArtworkCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            if (Block || ++_calls >= BlockOnCall)
            {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            var path = Path.Combine(root, "download-" + Guid.NewGuid());
            await File.WriteAllTextAsync(path, "fixture", cancellationToken);
            return new(candidates[0], path);
        }
    }

    [Fact]
    public async Task VideoUsesTheVideoDownloaderValidationAndSizeLimit()
    {
        AppPaths.EnsureDirectoriesExist();
        using var http = new HttpClient(new StoreHandler());
        var download = new Downloader(BaseDirectory);
        var source = new CompanionMediaSource(new MemoryDetails(), http, download, AppPaths);
        var video = new CompanionMediaItem("Preview", GameMediaKind.Video, null,
            new Uri("https://video.fastly.steamstatic.com/store_trailers/250180/1/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/1/microtrailer.mp4"));
        await source.GetLocalPathAsync(video, CancellationToken.None);
        Assert.Equal(RemoteMediaKind.Video, download.Last!.MediaKind);
    }

    [Fact]
    public async Task EmptyStoreResponseDoesNotClobberACachedManifest()
    {
        AppPaths.EnsureDirectoriesExist();
        var download = new Downloader(BaseDirectory);
        // Seed a good manifest (library_hero + logo) into the on-disk manifest cache.
        using (var goodHttp = new HttpClient(new StoreHandler()))
        {
            var seeded = new CompanionMediaSource(new MemoryDetails(), goodHttp, download, AppPaths);
            Assert.Equal(2, await seeded.FetchMissingAsync(Game(), CancellationToken.None));
        }
        var cachePath = Path.Combine(AppPaths.CacheDirectory, "CompanionMedia", "250180.json");
        var cachedManifest = await File.ReadAllTextAsync(cachePath);
        Assert.Contains("library_hero", cachedManifest);
        // Age it past the seven-day freshness window so the next run must consult the network.
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-8));

        // A fresh instance (empty retry-after + media store) hits an empty live response.
        var emptyHandler = new EmptyStoreHandler();
        using var emptyHttp = new HttpClient(emptyHandler);
        var reopened = new CompanionMediaSource(new MemoryDetails(), emptyHttp, download, AppPaths);
        // The empty/failed response must be ignored: the cached manifest still yields hero + logo,
        // and the cache file itself is left untouched rather than overwritten with the empty response.
        Assert.Equal(2, await reopened.FetchMissingAsync(Game(), CancellationToken.None));
        Assert.Equal(1, emptyHandler.Calls);
        Assert.Equal(cachedManifest, await File.ReadAllTextAsync(cachePath));
    }

    [Fact]
    public async Task AConcurrentSaveDuringDownloadIsNotOverwritten()
    {
        AppPaths.EnsureDirectoriesExist();
        using var http = new HttpClient(new StoreHandler());
        var details = new MemoryDetails();
        var download = new PausableDownloader(BaseDirectory);
        var source = new CompanionMediaSource(details, http, download, AppPaths);

        using var cancel = new CancellationTokenSource();
        var fetch = source.FetchMissingAsync(Game(), cancel.Token);
        // Block until the fetch's own fanart download is in flight.
        await download.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // A competing scrape / manual choice lands a fanart while the fetch is mid-download.
        var rival = Path.Combine(AppPaths.CoversDirectory, "rival-fanart.png");
        await File.WriteAllTextAsync(rival, "rival");
        details.SaveMedia(new(0, 1, GameMediaKind.Fanart, rival, true, GameMediaSelectionOrigin.User,
            GameMediaOrigin.Provider, "screenscraper", "250180", null, null, null, ".png",
            null, null, null, null, null, DateTimeOffset.UtcNow));

        download.Release();
        var applied = await fetch.WaitAsync(TimeSpan.FromSeconds(3));

        // The post-download recheck must discard the fetched fanart in favour of the intervening
        // choice; only the logo (a kind still absent) is saved.
        var fanarts = details.GetDetails(1).Media.Where(i => i.Kind == GameMediaKind.Fanart).ToArray();
        Assert.Equal(rival, Assert.Single(fanarts).LocalPath);
        Assert.Equal(1, applied);
        Assert.Contains(details.GetDetails(1).Media, i => i.Kind == GameMediaKind.Wheel);
    }

    private sealed class EmptyStoreHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new StringContent("""{"response":{"store_items":[]}}""") });
        }
    }

    private sealed class PausableDownloader(string root) : IRemoteArtworkDownloader
    {
        private int _calls;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();
        public async Task<DownloadedArtwork?> DownloadFirstAsync(IReadOnlyList<ArtworkCandidate> candidates,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                Started.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
            var path = Path.Combine(root, "download-" + Guid.NewGuid());
            await File.WriteAllTextAsync(path, "fixture", cancellationToken);
            return new(candidates[0], path);
        }
    }

    private static EmuShelf.Core.Library.Game Game() => new()
    {
        Id = 1, SystemId = "steam", Title = "Game", Path = "/game.steam", ExternalSourceEntryId = "250180"
    };

    private sealed class StoreHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""
                {"response":{"store_items":[{"appid":250180,"success":1,"assets":{"asset_url_format":"steam/apps/250180/${FILENAME}","library_hero":"library_hero.jpg"}}]}}
                """) });
        }
    }

    private sealed class Downloader(string root) : IRemoteArtworkDownloader
    {
        public int Calls;
        public ArtworkCandidate? Last;
        public async Task<DownloadedArtwork?> DownloadFirstAsync(IReadOnlyList<ArtworkCandidate> candidates, CancellationToken cancellationToken = default)
        {
            Calls++;
            Last = candidates[0];
            var path = Path.Combine(root, "download-" + Guid.NewGuid());
            await File.WriteAllTextAsync(path, "fixture", cancellationToken);
            return new(Last, path);
        }
    }

    private sealed class MemoryDetails : IGameDetailsStore
    {
        private readonly List<GameMediaAsset> _media = [];
        public GameDetails GetDetails(long id) => new(id, [], _media.ToArray(), []);
        public bool TryApplyMetadata(GameMetadataValue value, GameMetadataApplyMode mode) => throw new InvalidOperationException("Read-only");
        public GameMediaAsset SaveMedia(GameMediaAsset media, bool overrideUserSelection = false)
        {
            media = media with { Id = _media.Count + 1 };
            _media.Add(media);
            return media;
        }
        public bool SelectMedia(long gameId, GameMediaKind kind, long mediaId) => throw new InvalidOperationException("Read-only");
        public void UpsertProviderMatch(GameProviderMatch match) => throw new InvalidOperationException("Read-only");
    }
}
