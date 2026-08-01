using System.Net;
using System.Text;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperClientTests
{
    private static readonly ScreenScraperDeveloperCredentials DeveloperCredentials =
        new("dev id", "dev password", "EmuShelf-test");
    private static readonly ScreenScraperUserCredentials UserCredentials =
        new("user name", "user&password");

    [Fact]
    public async Task GetGameInfo_SendsHashAndSize_AndParsesMetadataMediaAndQuota()
    {
        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse(GameFixture());
        }));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(
                58,
                "Game Name.iso",
                4_294_967_296,
                Sha1: "ABCDEF",
                Serial: "SLUS-12345",
                Language: "fr"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(requestedUri);
        Assert.Equal("https", requestedUri.Scheme);
        Assert.EndsWith("/api2/jeuInfos.php", requestedUri.AbsolutePath);
        Assert.Contains("devid=dev%20id", requestedUri.Query);
        Assert.Contains("devpassword=dev%20password", requestedUri.Query);
        Assert.Contains("ssid=user%20name", requestedUri.Query);
        Assert.Contains("sspassword=user%26password", requestedUri.Query);
        Assert.Contains("romtaille=4294967296", requestedUri.Query);
        Assert.Contains("sha1=ABCDEF", requestedUri.Query);
        Assert.Contains("systemeid=58", requestedUri.Query);

        var game = Assert.IsType<ScreenScraperGameInfo>(result.Data);
        Assert.Equal("12345", game.ProviderGameId);
        Assert.Equal("67890", game.ProviderRomId);
        Assert.Contains(game.Names, name => name.Region == "us" && name.Value == "US Title");
        Assert.Contains(game.Descriptions, description => description.Language == "fr");
        Assert.Equal("Fixture Developer", game.Developer);
        Assert.Equal("Fixture Publisher", game.Publisher);
        Assert.Equal("1-2", game.Players);
        Assert.Equal("18.5", game.Rating);
        Assert.Equal(7, game.Media.Count);
        Assert.All(game.Media, media => Assert.Equal("https", media.SourceUri.Scheme));
        Assert.Equal(3, result.Quota!.MaxThreads);
        Assert.Equal(25, result.Quota.RequestsToday);
        Assert.Equal(1000, result.Quota.MaxRequestsPerDay);
    }

    [Fact]
    public async Task GetAccountInfo_ParsesAccountAndQuota()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(GameFixture())));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetAccountInfoAsync(UserCredentials);

        Assert.True(result.IsSuccess);
        Assert.Equal("42", result.Data!.UserId);
        Assert.Equal("fixture-user", result.Data.Username);
        Assert.Equal(3, result.Data.Quota.MaxThreads);
        Assert.Equal(50, result.Data.Quota.MaxFailedRequestsPerDay);
    }

    [Theory]
    [InlineData(403, ScreenScraperRequestStatus.AuthenticationFailed)]
    [InlineData(404, ScreenScraperRequestStatus.NotFound)]
    [InlineData(423, ScreenScraperRequestStatus.ServiceUnavailable)]
    [InlineData(426, ScreenScraperRequestStatus.ClientUpdateRequired)]
    [InlineData(429, ScreenScraperRequestStatus.RateLimited)]
    [InlineData(430, ScreenScraperRequestStatus.DailyQuotaExceeded)]
    [InlineData(431, ScreenScraperRequestStatus.FailedLookupQuotaExceeded)]
    public async Task GetGameInfo_MapsScreenScraperStatusCodes(
        int statusCode,
        ScreenScraperRequestStatus expected)
    {
        using var httpClient = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage((HttpStatusCode)statusCode)));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task NetworkError_DoesNotExposeCredentialBearingRequestUri()
    {
        using var httpClient = new HttpClient(new StubHandler(request =>
            throw new HttpRequestException($"Failed request: {request.RequestUri}")));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));

        Assert.Equal(ScreenScraperRequestStatus.NetworkError, result.Status);
        Assert.DoesNotContain("dev password", result.Error);
        Assert.DoesNotContain("user&password", result.Error);
        Assert.DoesNotContain("devpassword", result.Error);
    }

    [Fact]
    public async Task GetGameInfo_RejectsSerialOnlyAutomaticLookup()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(GameFixture())));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Serial: "SLUS-12345")));
    }

    [Fact]
    public async Task GetGameInfo_AllowsConfirmedProviderGameIdWithoutHash()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(GameFixture())));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 0, ProviderGameId: "12345"));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task KnownDailyQuota_PreventsTheHttpRequest()
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            requestCount++;
            return JsonResponse(GameFixture());
        }));
        var coordinator = new ScreenScraperRequestCoordinator();
        coordinator.ObserveQuota(new ScreenScraperQuota(3, 100, 100, 1, 50, null));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials, coordinator);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));

        Assert.Equal(ScreenScraperRequestStatus.DailyQuotaExceeded, result.Status);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task MetadataMapper_SelectsConfiguredRegionsLanguagesAndHdMedia()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(GameFixture())));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);
        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));
        var settings = new ScreenScraperSettings
        {
            PreferredLanguage = "fr",
            RegionPriority = ["us", "wor"],
        };

        var metadata = ScreenScraperMetadataMapper.MapMetadata(
            99,
            58,
            result.Data!,
            settings,
            DateTimeOffset.UnixEpoch);
        var media = ScreenScraperMetadataMapper.SelectMedia(result.Data!, settings);

        Assert.Equal("US Title", metadata.Single(value => value.Field == GameMetadataField.Title).Value);
        Assert.Equal("Jeu d'action", metadata.Single(value => value.Field == GameMetadataField.Genre).Value);
        Assert.Equal("2001-02-03", metadata.Single(value => value.Field == GameMetadataField.ReleaseDate).Value);
        Assert.Equal("18.5", metadata.Single(value => value.Field == GameMetadataField.Rating).Value);
        Assert.Equal(2, metadata.Count(value => value.Field == GameMetadataField.Description));
        Assert.All(metadata, value => Assert.Equal(ScreenScraperProvider.Id, value.ProviderId));
        Assert.Equal("box-us", media[GameMediaKind.BoxFront].ProviderMediaId);
        Assert.Equal("shot-hd", media[GameMediaKind.Screenshot].ProviderMediaId);
        Assert.Equal("wheel-hd", media[GameMediaKind.Wheel].ProviderMediaId);
        Assert.Equal("fanart", media[GameMediaKind.Fanart].ProviderMediaId);
    }

    private static string GameFixture() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScreenScraper", "game-info.json"));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
