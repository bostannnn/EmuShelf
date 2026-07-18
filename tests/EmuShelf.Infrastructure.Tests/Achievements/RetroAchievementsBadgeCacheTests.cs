using System.Net;
using EmuShelf.Infrastructure.Achievements;

namespace EmuShelf.Infrastructure.Tests.Achievements;

public class RetroAchievementsBadgeCacheTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task DownloadedBadge_UsesPortableBadgesDirectory_ThenServesCache()
    {
        var handler = new StubHandler(PngResponse);
        var cache = CreateCache(handler);

        var first = await cache.GetBadgePathAsync("123456", CancellationToken.None);
        var second = await cache.GetBadgePathAsync("123456", CancellationToken.None);

        var expectedDirectory = Path.Combine(
            AppPaths.CacheDirectory, "RetroAchievements", "Badges");
        Assert.NotNull(first);
        Assert.Equal(first, second);
        Assert.Equal(Path.Combine(expectedDirectory, "123456.png"), first);
        Assert.True(File.Exists(first));
        Assert.Equal(1, handler.Requests);
        Assert.Equal("/Badge/123456.png", handler.LastRequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ConcurrentBadgeRequests_AreCoalesced()
    {
        var handler = new StubHandler(PngResponse) { Delay = TimeSpan.FromMilliseconds(50) };
        var cache = CreateCache(handler);

        var paths = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => cache.GetBadgePathAsync("000007", CancellationToken.None)));

        Assert.All(paths, path => Assert.NotNull(path));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task InvalidOrUnavailableBadge_RetainsLocalPlaceholderAndWritesNothing()
    {
        var handler = new StubHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cache = CreateCache(handler);

        var unavailable = await cache.GetBadgePathAsync("missing", CancellationToken.None);
        var invalidName = await cache.GetBadgePathAsync("../../bad", CancellationToken.None);

        Assert.Null(unavailable);
        Assert.Null(invalidName);
        Assert.Equal(1, handler.Requests);
        var directory = Path.Combine(AppPaths.CacheDirectory, "RetroAchievements", "Badges");
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task EntryLimit_PrunesOldBadgeBeforeCachingTheNewOne()
    {
        var handler = new StubHandler(PngResponse);
        var cache = new RetroAchievementsBadgeCache(
            AppPaths,
            new HttpClient(handler),
            maximumEntries: 1,
            maximumBytes: 1000);

        await cache.GetBadgePathAsync("one", CancellationToken.None);
        await cache.GetBadgePathAsync("two", CancellationToken.None);

        var files = Directory.GetFiles(
            Path.Combine(AppPaths.CacheDirectory, "RetroAchievements", "Badges"),
            "*.png");
        Assert.Equal(["two.png"], files.Select(Path.GetFileName));
    }

    private RetroAchievementsBadgeCache CreateCache(StubHandler handler) =>
        new(AppPaths, new HttpClient(handler));

    private static HttpResponseMessage PngResponse() => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent([137, 80, 78, 71, 13, 10, 26, 10, 0, 1, 2, 3]),
    };

    private sealed class StubHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory = responseFactory;
        public int Requests { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public TimeSpan Delay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestUri = request.RequestUri;
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);
            return _responseFactory();
        }
    }
}
