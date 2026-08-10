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
        Assert.Equal(14, game.Media.Count);
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
    public async Task GetGameInfo_AllowsSerialOnlyLookup_ForCompressedDiscContainers()
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
            new ScreenScraperGameRequest(58, "game.chd", 0, Serial: "SLUS-12345"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(requestedUri);
        Assert.Contains("serialnum=SLUS-12345", requestedUri.Query);
        Assert.DoesNotContain("romtaille=", requestedUri.Query);
    }

    [Fact]
    public async Task GetGameInfo_RejectsFilenameFallback_WhenReturnedRomHashDiffersFromQueriedHash()
    {
        // ScreenScraper found no hash match and fell back to the file name, returning a different
        // game whose ROM sha1 is not the one we asked for. The result must be rejected so a wrong
        // game is never applied as an exact hash match.
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(
            "{\"header\":{\"success\":\"true\"},\"response\":{\"jeu\":{\"id\":\"12345\"," +
            "\"rom\":{\"id\":\"67890\",\"romsha1\":\"1111111111111111111111111111111111111111\"}}}}")));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(
                58, "Renamed Game.iso", 100, Sha1: "abcdef0000000000000000000000000000000000"));

        Assert.Equal(ScreenScraperRequestStatus.NotFound, result.Status);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetGameInfo_AcceptsMatch_WhenReturnedRomHashEqualsQueriedHashIgnoringCase()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(
            "{\"header\":{\"success\":\"true\"},\"response\":{\"jeu\":{\"id\":\"12345\"," +
            "\"rom\":{\"id\":\"67890\",\"romsha1\":\"ABCDEF0000000000000000000000000000000000\"}}}}")));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(
                58, "Game.iso", 100, Sha1: "abcdef0000000000000000000000000000000000"));

        Assert.True(result.IsSuccess);
        Assert.Equal("12345", result.Data!.ProviderGameId);
        Assert.Equal("ABCDEF0000000000000000000000000000000000", result.Data.RomSha1);
    }

    [Fact]
    public async Task GetGameInfo_RejectsLookupWithNoHashSerialOrGameId()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(GameFixture())));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 0)));
    }

    [Fact]
    public async Task GetGameInfo_AllowsFileNameOnlyLookup_WhenOptedIn_ForArcadeSets()
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
            new ScreenScraperGameRequest(75, "tmnt.zip", 0, AllowFileNameMatch: true));

        Assert.True(result.IsSuccess);
        Assert.NotNull(requestedUri);
        Assert.Contains("romnom=tmnt.zip", requestedUri.Query);
        Assert.DoesNotContain("crc=", requestedUri.Query);
        Assert.DoesNotContain("serialnum=", requestedUri.Query);
        Assert.DoesNotContain("romtaille=", requestedUri.Query);
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
        Assert.Equal("title", media[GameMediaKind.TitleScreen].ProviderMediaId);
        Assert.Equal("box-back", media[GameMediaKind.BoxBack].ProviderMediaId);
        Assert.Equal("box-side", media[GameMediaKind.BoxSpine].ProviderMediaId);
        Assert.Equal("support", media[GameMediaKind.PhysicalMedia].ProviderMediaId);
        Assert.Equal("support-texture", media[GameMediaKind.PhysicalMediaTexture].ProviderMediaId);
        // Video is opt-in: it is absent from the default media kinds, so it is not selected here.
        Assert.False(media.ContainsKey(GameMediaKind.Video));
    }

    [Fact]
    public async Task MetadataMapper_SelectsVideo_OnlyWhenEnabled_PreferringNormalizedEncode()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(GameFixture())));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);
        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));
        var settings = new ScreenScraperSettings { MediaKinds = [GameMediaKind.Video] };

        var media = ScreenScraperMetadataMapper.SelectMedia(result.Data!, settings);

        Assert.Equal("video-normalized", media[GameMediaKind.Video].ProviderMediaId);
    }

    [Theory]
    [InlineData("arcade", GameMediaKind.TitleScreen)]
    [InlineData("playstation2", GameMediaKind.BoxFront)]
    public void CoverKindFor_UsesTitleScreenForArcade_BoxFrontOtherwise(string systemId, GameMediaKind expected) =>
        Assert.Equal(expected, ScreenScraperMetadataMapper.CoverKindFor(systemId));

    [Fact]
    public async Task GetGameInfo_WithDebugOptions_AppendsDebugParametersToRequest()
    {
        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse(GameFixture());
        }));
        var debug = new ScreenScraperDebugOptions(
            "debug-secret",
            ForceUpdate: true,
            ForceLevel: 30,
            ForceRequestOk: 100,
            ForceRequestKo: 5,
            ForceRequestMin: 20);
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials, debugOptions: debug);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));

        Assert.True(result.IsSuccess);
        Assert.NotNull(requestedUri);
        Assert.Contains("devdebugpassword=debug-secret", requestedUri.Query);
        Assert.Contains("forceupdate=1", requestedUri.Query);
        Assert.Contains("forcelevel=30", requestedUri.Query);
        Assert.Contains("forcerequestok=100", requestedUri.Query);
        Assert.Contains("forcerequestko=5", requestedUri.Query);
        Assert.Contains("forcerequestmin=20", requestedUri.Query);
    }

    [Fact]
    public async Task WithoutDebugOptions_NoDebugParametersAreSent()
    {
        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse(GameFixture());
        }));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));

        Assert.NotNull(requestedUri);
        Assert.DoesNotContain("devdebugpassword", requestedUri.Query);
        Assert.DoesNotContain("forceupdate", requestedUri.Query);
    }

    [Fact]
    public async Task DebugPassword_IsRedactedFromApiErrorMessages()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(
            "{\"header\":{\"success\":\"false\",\"error\":\"Bad devdebugpassword=debug-secret supplied\"}}")));
        var debug = new ScreenScraperDebugOptions("debug-secret");
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials, debugOptions: debug);

        var result = await client.GetGameInfoAsync(
            UserCredentials,
            new ScreenScraperGameRequest(58, "game.iso", 100, Sha1: "ABC"));

        Assert.Equal(ScreenScraperRequestStatus.ApiRejected, result.Status);
        Assert.NotNull(result.Error);
        Assert.DoesNotContain("debug-secret", result.Error);
    }

    [Fact]
    public async Task EmptyDebugPassword_IsRejected()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(GameFixture())));

        Assert.Throws<ArgumentException>(() => new ScreenScraperClient(
            httpClient,
            DeveloperCredentials,
            debugOptions: new ScreenScraperDebugOptions("   ")));
    }

    [Fact]
    public async Task SearchGames_ParsesRankedCandidates_AndScopesToTheSystem()
    {
        Uri? requestedUri = null;
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return JsonResponse(
                "{\"header\":{\"success\":\"true\"},\"response\":{\"jeux\":[" +
                "{\"id\":\"111\",\"noms\":[{\"region\":\"us\",\"text\":\"Zelda Hack\"}]," +
                "\"systeme\":{\"id\":\"4\",\"text\":\"Super Nintendo\"}}," +
                "{\"id\":\"222\",\"noms\":[{\"region\":\"wor\",\"text\":\"Another Game\"}]}]}}");
        }));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.SearchGamesAsync(UserCredentials, 4, "zelda");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("111", result.Data[0].ProviderGameId);
        Assert.Equal("Zelda Hack", result.Data[0].Name);
        Assert.Equal("Super Nintendo", result.Data[0].System);
        Assert.NotNull(requestedUri);
        Assert.EndsWith("/api2/jeuRecherche.php", requestedUri.AbsolutePath);
        Assert.Contains("recherche=zelda", requestedUri.Query);
        Assert.Contains("systemeid=4", requestedUri.Query);
    }

    [Fact]
    public async Task GetSystems_ParsesSystemCatalogue()
    {
        using var httpClient = new HttpClient(new StubHandler(_ => JsonResponse(SystemsFixture())));
        var client = new ScreenScraperClient(httpClient, DeveloperCredentials);

        var result = await client.GetSystemsAsync(UserCredentials);

        Assert.True(result.IsSuccess);
        var systems = result.Data!;
        Assert.Equal(3, systems.Count);
        var megadrive = systems.Single(system => system.Id == 1);
        Assert.Contains("Genesis", megadrive.Names);
        Assert.Contains("Megadrive", megadrive.Names);
        // The "id" field arrives as a JSON string for GameCube; it must still parse.
        Assert.Contains(systems, system => system.Id == 13);
        Assert.Contains(systems, system => system.Id == 58);
    }

    private static string GameFixture() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScreenScraper", "game-info.json"));

    private static string SystemsFixture() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScreenScraper", "systemes-list.json"));

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
